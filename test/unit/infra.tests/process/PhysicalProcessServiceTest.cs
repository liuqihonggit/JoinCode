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
}
