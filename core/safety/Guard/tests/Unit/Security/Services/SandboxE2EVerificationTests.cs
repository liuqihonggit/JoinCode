namespace Guard.Tests.Security.Services;

using JoinCode.Abstractions.Security.Sandbox;

public sealed class SandboxE2EVerificationTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task SoftSandbox_PathRedirection_PreventsEscape()
    {
        var provider = new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance);
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        var outsidePath = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32\config\SAM"
            : "/etc/shadow";

        var resolvedPath = provider.ResolvePath(outsidePath, info.SandboxId);

        var sandboxRoot = Path.GetFullPath(info.RootPath);
        resolvedPath.Should().StartWith(sandboxRoot, because: "高危路径应被重定向到沙箱内");

        var isInSandbox = await provider.IsPathInSandboxAsync(resolvedPath, info.SandboxId).ConfigureAwait(true);
        isInSandbox.Should().BeTrue(because: "重定向后的路径应在沙箱范围内");

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task SoftSandbox_ExplicitTraversal_ThrowsException()
    {
        var provider = new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance);
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true
        };

        var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

        var sandboxRoot = Path.GetFullPath(info.RootPath);
        var traversalTarget = Path.GetFullPath(Path.Combine(sandboxRoot, "..", "..", "..", "Windows", "System32"));
        var traversalPath = Path.Combine(sandboxRoot, "..", "..", "..", "Windows", "System32", "config", "SAM");

        var resolvedPath = provider.ResolvePath(traversalPath, info.SandboxId);
        resolvedPath.Should().StartWith(sandboxRoot, because: "遍历路径应被重定向回沙箱内，不允许逃出");

        await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task SandboxManager_Fallback_WhenTypeUnavailable()
    {
        var providers = new List<ISandboxProvider>
        {
            new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance)
        };

        var manager = new SandboxManager(providers, _fs, NullLogger<SandboxManager>.Instance);

        var result = await manager.TryEnterWithFallbackAsync(new SandboxOptions
        {
            Type = SandboxType.Docker,
            RestrictFileSystem = true,
            RestrictNetwork = true
        }).ConfigureAwait(true);

        result.WasDegraded.Should().BeTrue(because: "Docker 不可用时应自动降级");
        result.ActualType.Should().Be(SandboxType.Soft, because: "应降级到 Soft");
        result.Message.Should().NotBeNullOrEmpty(because: "降级时应提供用户提示");
        result.Info.Should().NotBeNull(because: "降级后应成功创建沙箱");

        await manager.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SandboxManager_AllTypesUnavailable_ReturnsNone()
    {
        var manager = new SandboxManager([], _fs, NullLogger<SandboxManager>.Instance);

        var result = await manager.TryEnterWithFallbackAsync(new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true
        }).ConfigureAwait(true);

        result.ActualType.Should().Be(SandboxType.None, because: "无可用 Provider 时应返回 None");
        result.Info.Should().BeNull(because: "无可用 Provider 时不应创建沙箱");
        result.Message.Should().Contain("不可用", because: "应告知用户所有类型不可用");
    }

    [Fact]
    public async Task SandboxManager_ExecuteInSandbox_FastCommand_Completes()
    {
        var providers = new List<ISandboxProvider>
        {
            new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance)
        };

        var manager = new SandboxManager(providers, _fs, NullLogger<SandboxManager>.Instance);

        await manager.EnterSandboxAsync(new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true,
            RestrictNetwork = true
        }).ConfigureAwait(true);

        var execOptions = new SandboxExecutionOptions
        {
            TimeoutPreset = SandboxExecutionTimeout.TwoMinutes
        };

        var result = await manager.ExecuteInSandboxAsync("echo hello_sandbox", execOptions).ConfigureAwait(true);

        result.State.Should().Be(SandboxExecutionState.Completed, because: "快速命令应正常完成");
        result.Stdout.Should().Contain("hello_sandbox", because: "应捕获命令输出");
        result.ExitCode.Should().Be(0);

        await manager.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SandboxManager_ExecuteInSandbox_TimedOut_ReturnsLlmDecision()
    {
        var providers = new List<ISandboxProvider>
        {
            new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance)
        };

        var manager = new SandboxManager(providers, _fs, NullLogger<SandboxManager>.Instance);

        await manager.EnterSandboxAsync(new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true,
            RestrictNetwork = true
        }).ConfigureAwait(true);

        var execOptions = new SandboxExecutionOptions
        {
            TimeoutPreset = SandboxExecutionTimeout.Custom,
            CustomTimeoutSeconds = 2
        };

        var longRunningCommand = OperatingSystem.IsWindows()
            ? "powershell -Command \"Start-Sleep -Seconds 60\""
            : "sleep 60";

        var result = await manager.ExecuteInSandboxAsync(longRunningCommand, execOptions).ConfigureAwait(true);

        result.State.Should().Be(SandboxExecutionState.TimedOut, because: "命令超过2秒应超时");
        result.NeedsLlmDecision.Should().BeTrue(because: "超时后应需要LLM决策");
        result.GetLlmPrompt().Should().Contain("sandbox_exec_continue", because: "提示应包含继续操作指引");
        result.ExecutionId.Should().NotBeNullOrEmpty();

        var stopResult = await manager.ContinueExecutionAsync(result.ExecutionId, "stop").ConfigureAwait(true);
        stopResult.State.Should().Be(SandboxExecutionState.ForceStopped, because: "stop操作应强行终止");

        await manager.ExitSandboxAsync().ConfigureAwait(true);
    }
}
