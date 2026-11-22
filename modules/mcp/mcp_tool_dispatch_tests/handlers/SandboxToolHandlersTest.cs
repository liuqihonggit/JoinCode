namespace McpToolRegistry.Tests;

/// <summary>
/// SandboxToolHandlers 纯函数确定性测试 — 执行结果响应构建、隔离级别变更描述
/// </summary>
public class SandboxToolHandlersTest {
    // === BuildExecResultResponse (internal static) ===

    [Fact]
    public void BuildExecResultResponse_Completed_IncludesExitCodeAndOutput() {
        var result = new SandboxExecutionResult {
            State = SandboxExecutionState.Completed,
            ExecutionId = "exec-1",
            ExitCode = 0,
            Stdout = "hello",
            Stderr = "",
            Elapsed = TimeSpan.FromSeconds(5),
            ConfiguredTimeout = TimeSpan.FromMinutes(1)
        };

        var response = SandboxToolHandlers.BuildExecResultResponse(result);
        response.IsError.Should().BeFalse();
        var text = response.GetFirstText();
        text.Should().Contain("Execution ID: exec-1");
        text.Should().Contain("State: completed");
        text.Should().Contain("Exit code: 0");
        text.Should().Contain("--- stdout ---");
        text.Should().Contain("hello");
    }

    [Fact]
    public void BuildExecResultResponse_CompletedWithStderr_IncludesStderrSection() {
        var result = new SandboxExecutionResult {
            State = SandboxExecutionState.Completed,
            ExecutionId = "exec-2",
            ExitCode = 0,
            Stdout = "",
            Stderr = "warning",
            Elapsed = TimeSpan.FromSeconds(1)
        };

        var text = SandboxToolHandlers.BuildExecResultResponse(result).GetFirstText();
        text.Should().Contain("--- stderr ---");
        text.Should().Contain("warning");
    }

    [Fact]
    public void BuildExecResultResponse_TimedOut_IncludesTimeoutNotice() {
        var result = new SandboxExecutionResult {
            State = SandboxExecutionState.TimedOut,
            ExecutionId = "exec-3",
            Stdout = "partial",
            Elapsed = TimeSpan.FromSeconds(60),
            ConfiguredTimeout = TimeSpan.FromMinutes(1)
        };

        var text = SandboxToolHandlers.BuildExecResultResponse(result).GetFirstText();
        text.Should().Contain("State: timed_out");
        text.Should().Contain("已超时");
        text.Should().Contain("stdout (partial)");
        text.Should().Contain("partial");
    }

    [Fact]
    public void BuildExecResultResponse_ForceStopped_IncludesKillNotice() {
        var result = new SandboxExecutionResult {
            State = SandboxExecutionState.ForceStopped,
            ExecutionId = "exec-4",
            Stdout = "before",
            Elapsed = TimeSpan.FromSeconds(2)
        };

        var text = SandboxToolHandlers.BuildExecResultResponse(result).GetFirstText();
        text.Should().Contain("State: force_stopped");
        text.Should().Contain("进程已被强行终止");
        text.Should().Contain("stdout (before kill)");
        text.Should().Contain("before");
    }

    [Fact]
    public void BuildExecResultResponse_Failed_IncludesErrorMessage() {
        var result = new SandboxExecutionResult {
            State = SandboxExecutionState.Failed,
            ExecutionId = "exec-5",
            ErrorMessage = "boom",
            Elapsed = TimeSpan.FromSeconds(0)
        };

        var text = SandboxToolHandlers.BuildExecResultResponse(result).GetFirstText();
        text.Should().Contain("State: failed");
        text.Should().Contain("Error: boom");
    }

    [Fact]
    public void BuildExecResultResponse_AllStates_ReturnsSuccessResult() {
        var states = new[] {
            SandboxExecutionState.Completed,
            SandboxExecutionState.TimedOut,
            SandboxExecutionState.ForceStopped,
            SandboxExecutionState.Failed
        };

        foreach (var state in states) {
            var result = new SandboxExecutionResult {
                State = state,
                ExecutionId = "e",
                Elapsed = TimeSpan.Zero
            };
            var response = SandboxToolHandlers.BuildExecResultResponse(result);
            response.IsError.Should().BeFalse($"state {state} should produce success response");
            response.GetFirstText().Should().NotBeEmpty();
        }
    }

    // === GetIsolationChangeDescription (internal static) ===

    [Fact]
    public void GetIsolationChangeDescription_Upgrade_ReturnsPromotionMessage() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.Soft, SandboxType.Process)
            .Should().Contain("提升");
    }

    [Fact]
    public void GetIsolationChangeDescription_Downgrade_ReturnsWarningMessage() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.Docker, SandboxType.Soft)
            .Should().Contain("降低");
    }

    [Fact]
    public void GetIsolationChangeDescription_SameLevel_ReturnsUnchangedMessage() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.Process, SandboxType.Process)
            .Should().Contain("不变");
    }

    [Theory]
    [InlineData(SandboxType.Soft, SandboxType.Process, "提升")]
    [InlineData(SandboxType.Soft, SandboxType.Bubblewrap, "提升")]
    [InlineData(SandboxType.Soft, SandboxType.Docker, "提升")]
    [InlineData(SandboxType.Process, SandboxType.Bubblewrap, "提升")]
    [InlineData(SandboxType.Process, SandboxType.Docker, "提升")]
    [InlineData(SandboxType.Bubblewrap, SandboxType.Docker, "提升")]
    [InlineData(SandboxType.Docker, SandboxType.Bubblewrap, "降低")]
    [InlineData(SandboxType.Docker, SandboxType.Process, "降低")]
    [InlineData(SandboxType.Docker, SandboxType.Soft, "降低")]
    [InlineData(SandboxType.Bubblewrap, SandboxType.Process, "降低")]
    [InlineData(SandboxType.Bubblewrap, SandboxType.Soft, "降低")]
    [InlineData(SandboxType.Process, SandboxType.Soft, "降低")]
    [InlineData(SandboxType.Soft, SandboxType.Soft, "不变")]
    [InlineData(SandboxType.Bubblewrap, SandboxType.Bubblewrap, "不变")]
    [InlineData(SandboxType.Docker, SandboxType.Docker, "不变")]
    public void GetIsolationChangeDescription_AllPairs_ReturnsExpectedDirection(
        SandboxType from, SandboxType to, string expectedMarker) {
        SandboxToolHandlers.GetIsolationChangeDescription(from, to).Should().Contain(expectedMarker);
    }

    [Fact]
    public void GetIsolationChangeDescription_FromNoneToSoft_TreatedAsUpgrade() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.None, SandboxType.Soft)
            .Should().Contain("提升");
    }

    [Fact]
    public void GetIsolationChangeDescription_FromSoftToNone_TreatedAsDowngrade() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.Soft, SandboxType.None)
            .Should().Contain("降低");
    }

    [Fact]
    public void GetIsolationChangeDescription_NoneToNone_TreatedAsUnchanged() {
        SandboxToolHandlers.GetIsolationChangeDescription(SandboxType.None, SandboxType.None)
            .Should().Contain("不变");
    }
}
