namespace NativeTui.Tests.Repl.CommandsUx;

/// <summary>
/// BashDirectExecutor 测试 — 真实进程执行（非 mock）：stdout/stderr 采集、退出码透传、
/// 取消中断（Kill 进程树 + Interrupted 标记）、shell 缺失降级失败结果、参数校验。
/// 平台分支：Windows 用 cmd.exe /c，Unix 用 /bin/sh -c。
/// </summary>
public sealed class CommandBashDirectTests
{
    /// <summary>按平台构造执行器（测试环境 shell 注入面）。</summary>
    private static BashDirectExecutor CreatePlatformExecutor() => OperatingSystem.IsWindows()
        ? new BashDirectExecutor("cmd.exe", "/c")
        : new BashDirectExecutor("/bin/sh", "-c");

    /// <summary>按平台的长驻命令（取消用例 — 至少运行 30 秒）。</summary>
    private static string LongRunningCommand => OperatingSystem.IsWindows()
        ? "ping -n 30 127.0.0.1"
        : "sleep 30";

    /// <summary>echo 输出：stdout 采集 + 退出码 0。</summary>
    [Fact]
    public async Task ExecuteAsync_Echo_CapturesStdoutWithZeroExitCode()
    {
        var result = await CreatePlatformExecutor().ExecuteAsync("echo nativetui-bash-direct");

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Interrupted);
        Assert.Contains("nativetui-bash-direct", result.StdOut);
    }

    /// <summary>非零退出码透传（cmd exit /b 7 / sh exit 7）。</summary>
    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_Passthrough()
    {
        var result = await CreatePlatformExecutor().ExecuteAsync("exit 7");

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Interrupted);
    }

    /// <summary>stderr 采集（标准错误重定向分支）。</summary>
    [Fact]
    public async Task ExecuteAsync_StderrRedirect_CapturedSeparately()
    {
        var result = await CreatePlatformExecutor().ExecuteAsync("echo nativetui-bash-stderr 1>&2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("nativetui-bash-stderr", result.StdErr);
        Assert.DoesNotContain("nativetui-bash-stderr", result.StdOut);
    }

    /// <summary>取消：Kill 进程树 + Interrupted 标记 + 退出码 -1（部分输出返回不抛）。</summary>
    [Fact]
    public async Task ExecuteAsync_Cancellation_KillsAndReturnsInterrupted()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await CreatePlatformExecutor().ExecuteAsync(LongRunningCommand, cts.Token);

        Assert.True(result.Interrupted);
        Assert.Equal(-1, result.ExitCode);
    }

    /// <summary>shell 不存在：降级失败结果（StdErr 失败消息 + 退出码 -1，不抛异常）。</summary>
    [Fact]
    public async Task ExecuteAsync_MissingShell_ReturnsFailureResult()
    {
        var executor = new BashDirectExecutor("nativetui-missing-shell-xyz", "-c");
        var result = await executor.ExecuteAsync("echo hi");

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Interrupted);
        Assert.Contains("命令执行失败", result.StdErr);
    }

    /// <summary>参数校验：空命令抛 ArgumentException。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExecuteAsync_EmptyCommand_Throws(string command)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreatePlatformExecutor().ExecuteAsync(command));
    }

    /// <summary>构造校验：空 shell 名/空参数开关抛 ArgumentException。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_EmptyShellName_Throws(string shellFileName)
    {
        Assert.Throws<ArgumentException>(() => new BashDirectExecutor(shellFileName));
    }

    /// <summary>默认 shell 契约：bash + -c（基准 bash 直通形态）。</summary>
    [Fact]
    public void Defaults_AreBashWithDashC()
    {
        Assert.Equal("bash", BashDirectExecutor.DefaultShellFileName);
        Assert.Equal("-c", BashDirectExecutor.DefaultCommandFlag);
    }
}
