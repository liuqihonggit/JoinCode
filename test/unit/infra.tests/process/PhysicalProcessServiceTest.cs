namespace Infra.Tests.Process;

/// <summary>
/// PhysicalProcessService 纯计算方法单元测试(阶段2.9 拆分)。
/// <para>测试 internal static 方法:ComputeArgsDisplay / CreateTimeoutResult。</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class PhysicalProcessServiceTest {
    // ===== ComputeArgsDisplay =====

    [Fact]
    public void ComputeArgsDisplay_NonEmptyArgumentList_JoinsWithSpace() {
        PhysicalProcessService.ComputeArgsDisplay(["build", "-c", "Release"], "--fallback")
            .Should().Be("build -c Release");
    }

    [Fact]
    public void ComputeArgsDisplay_EmptyArgumentList_FallsBackToArguments() {
        PhysicalProcessService.ComputeArgsDisplay(Array.Empty<string>(), "--flag value")
            .Should().Be("--flag value");
    }

    [Fact]
    public void ComputeArgsDisplay_SingleElementArgumentList_ReturnsSingleElement() {
        PhysicalProcessService.ComputeArgsDisplay(["single"], "fallback")
            .Should().Be("single");
    }

    [Fact]
    public void ComputeArgsDisplay_EmptyArgumentListAndEmptyArguments_ReturnsEmpty() {
        PhysicalProcessService.ComputeArgsDisplay(Array.Empty<string>(), "")
            .Should().BeEmpty();
    }

    [Fact]
    public void ComputeArgsDisplay_ThroughProcessOptions_ArgumentListTakesPrecedence() {
        // 验证与 ProcessOptions 集成:ArgumentList 非空时忽略 Arguments
        var options = new ProcessOptions {
            FileName = "dotnet",
            Arguments = "--should-be-ignored",
            ArgumentList = ["build", "-c", "Debug"]
        };

        PhysicalProcessService.ComputeArgsDisplay(options.ArgumentList, options.Arguments)
            .Should().Be("build -c Debug");
    }

    [Fact]
    public void ComputeArgsDisplay_ThroughProcessOptions_EmptyArgumentListFallsBackToArguments() {
        var options = new ProcessOptions {
            FileName = "dotnet",
            Arguments = "build -c Release"
        };

        PhysicalProcessService.ComputeArgsDisplay(options.ArgumentList, options.Arguments)
            .Should().Be("build -c Release");
    }

    // ===== CreateTimeoutResult =====

    [Fact]
    public void CreateTimeoutResult_ReturnsExitCodeMinusOne() {
        var result = PhysicalProcessService.CreateTimeoutResult(TimeSpan.FromSeconds(5));
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public void CreateTimeoutResult_ReturnsEmptyStandardOutput() {
        var result = PhysicalProcessService.CreateTimeoutResult(TimeSpan.Zero);
        result.StandardOutput.Should().BeEmpty();
    }

    [Fact]
    public void CreateTimeoutResult_ReturnsTimeoutErrorMessage() {
        var result = PhysicalProcessService.CreateTimeoutResult(TimeSpan.Zero);
        result.StandardError.Should().Be("进程执行超时");
    }

    [Fact]
    public void CreateTimeoutResult_PreservesElapsedExecutionTime() {
        var elapsed = TimeSpan.FromMilliseconds(1234);
        var result = PhysicalProcessService.CreateTimeoutResult(elapsed);
        result.ExecutionTime.Should().Be(elapsed);
    }

    [Fact]
    public void CreateTimeoutResult_SuccessIsFalseDueToNonZeroExitCode() {
        var result = PhysicalProcessService.CreateTimeoutResult(TimeSpan.Zero);
        result.Success.Should().BeFalse();
    }

    // ===== PhysicalInteractiveProcess 子进程编码（改动 P3）=====

    /// <summary>
    /// 交互式进程的 StandardInput 编码应使用 Console.InputEncoding（对齐系统代码页）。
    /// </summary>
    [Fact]
    public async Task 交互式进程_StandardInput编码_应为ConsoleInputEncoding() {
        await using var interactive = await StartTestInteractiveProcessAsync().ConfigureAwait(true);
        interactive.StandardInput.Encoding.Should().BeSameAs(Console.InputEncoding);
    }

    /// <summary>
    /// 交互式进程的 StandardOutput 编码应使用 Console.OutputEncoding（对齐系统代码页）。
    /// </summary>
    [Fact]
    public async Task 交互式进程_StandardOutput编码_应为ConsoleOutputEncoding() {
        await using var interactive = await StartTestInteractiveProcessAsync().ConfigureAwait(true);
        interactive.StandardOutput.CurrentEncoding.Should().BeSameAs(Console.OutputEncoding);
    }

    /// <summary>
    /// 交互式进程的 StandardInput 和 StandardOutput 编码应分别对应 Console 的输入/输出编码。
    /// </summary>
    [Fact]
    public async Task 交互式进程_输入输出编码_应分别对齐Console编码() {
        await using var interactive = await StartTestInteractiveProcessAsync().ConfigureAwait(true);
        interactive.StandardInput.Encoding.Should().BeSameAs(Console.InputEncoding);
        interactive.StandardOutput.CurrentEncoding.Should().BeSameAs(Console.OutputEncoding);
    }

    /// <summary>
    /// 启动一个测试用交互式进程（跨平台），调用方用 await using 释放。
    /// </summary>
    private static async Task<IInteractiveProcess> StartTestInteractiveProcessAsync() {
        var builder = new ProcessStartInfoBuilder(new ProcessEncodingProvider());
        var service = new PhysicalProcessService(builder);

        // Windows: cmd.exe 保持运行等待输入；Linux: /bin/sh 保持运行等待输入
        var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows() ? "" : "";

        var options = new InteractiveProcessOptions {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
        };

        var interactive = await service.StartInteractiveAsync(options, CancellationToken.None).ConfigureAwait(true);
        return interactive;
    }
}
