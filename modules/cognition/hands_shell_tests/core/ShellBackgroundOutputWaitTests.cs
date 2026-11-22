namespace Hands.Tests.Shell;

/// <summary>
/// shell_background_output 的等待语义 — 对齐基准 TaskOutput：
/// 默认阻塞、超时返回部分输出且不取消任务、block=false 立即返回当前快照。
/// </summary>
public sealed class ShellBackgroundOutputWaitTests {
    private const string TaskId = "task-1";

    [Fact]
    public void ResolveWaitOptions_NoArguments_DefaultsToBlocking30s() {
        var options = ShellToolHandlers.ResolveWaitOptions(block: null, timeoutMs: null);

        options.Block.Should().BeTrue("基准 block 默认 true");
        options.TimeoutMs.Should().Be(ShellToolHandlers.DefaultOutputTimeoutMs);
        options.TimeoutMs.Should().Be(30_000);
    }

    [Fact]
    public void ResolveWaitOptions_NonBlocking_KeepsDefaultTimeout() {
        var options = ShellToolHandlers.ResolveWaitOptions(block: false, timeoutMs: null);

        options.Block.Should().BeFalse();
        options.TimeoutMs.Should().Be(30_000);
    }

    [Theory]
    [InlineData(900_000, 600_000)]
    [InlineData(600_001, 600_000)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    public void ResolveWaitOptions_OutOfRangeTimeout_ClampsToBaselineBounds(int requested, int expected) {
        var options = ShellToolHandlers.ResolveWaitOptions(block: true, timeoutMs: requested);

        options.TimeoutMs.Should().Be(expected, "基准约束 timeout ∈ [0, 600000]");
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Completed, true, "Success")]
    [InlineData(TaskExecutionStatus.Failed, true, "Success")]
    [InlineData(TaskExecutionStatus.Cancelled, true, "Success")]
    [InlineData(TaskExecutionStatus.Running, true, "Timeout")]
    [InlineData(TaskExecutionStatus.Running, false, "NotReady")]
    [InlineData(TaskExecutionStatus.Pending, false, "NotReady")]
    public void ResolveRetrieval_FollowsStatusAndBlockIntent(
        TaskExecutionStatus status,
        bool block,
        string expected) {
        // 枚举是 internal，公开测试不能直接引用；按名称比对等价且不牺牲覆盖。
        ShellToolHandlers.ResolveRetrieval(status, block).ToString().Should().Be(expected);
    }

    [Fact]
    public async Task ReadBackgroundOutputAsync_CompletedTask_ReturnsSuccessWithOutputAndExitCode() {
        var registry = new Mock<ISystemActuatorRegistry>();
        var info = CreateInfo(TaskExecutionStatus.Completed, exitCode: 0);
        registry.Setup(r => r.GetTaskAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        registry.Setup(r => r.WaitForTaskAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        registry.Setup(r => r.GetTaskOutputAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync("build ok");

        var result = await ShellToolHandlers.ReadBackgroundOutputAsync(registry.Object, TaskId, block: null, timeoutMs: null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should()
            .Contain("<retrieval_status>success</retrieval_status>")
            .And.Contain("<status>completed</status>")
            .And.Contain("<exit_code>0</exit_code>")
            .And.Contain("build ok")
            .And.NotContain("仍在运行");
    }

    [Fact]
    public async Task ReadBackgroundOutputAsync_NonBlockingWhileRunning_SkipsWaitAndReportsNotReady() {
        var registry = new Mock<ISystemActuatorRegistry>();
        var info = CreateInfo(TaskExecutionStatus.Running);
        registry.Setup(r => r.GetTaskAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        registry.Setup(r => r.GetTaskOutputAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync("line 1");

        var result = await ShellToolHandlers.ReadBackgroundOutputAsync(registry.Object, TaskId, block: false, timeoutMs: null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should()
            .Contain("<retrieval_status>not_ready</retrieval_status>")
            .And.Contain("line 1");
        registry.Verify(r => r.WaitForTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "block=false 必须立即返回当前快照，不得进入等待");
    }

    [Fact]
    public async Task ReadBackgroundOutputAsync_BlockingTimeout_ReturnsPartialOutputAndKeepsTaskRunning() {
        var registry = new Mock<ISystemActuatorRegistry>();
        var info = CreateInfo(TaskExecutionStatus.Running);
        registry.Setup(r => r.GetTaskAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        registry.Setup(r => r.WaitForTaskAsync(TaskId, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) => {
                await Task.Delay(Timeout.Infinite, ct);
                return info;
            });
        registry.Setup(r => r.GetTaskOutputAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync("partial line");

        var result = await ShellToolHandlers.ReadBackgroundOutputAsync(registry.Object, TaskId, block: true, timeoutMs: 50, CancellationToken.None);

        result.IsError.Should().BeFalse("等待超时是正常状态，不是工具失败");
        result.Content[0].Text.Should()
            .Contain("<retrieval_status>timeout</retrieval_status>")
            .And.Contain("<status>running</status>")
            .And.Contain("partial line");
        registry.Verify(r => r.CancelTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "超时只结束本次等待，必须保留仍在运行的后台任务");
    }

    [Fact]
    public async Task ReadBackgroundOutputAsync_CallerCancelled_PropagatesCancellation() {
        var registry = new Mock<ISystemActuatorRegistry>();
        var info = CreateInfo(TaskExecutionStatus.Running);
        registry.Setup(r => r.GetTaskAsync(TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        registry.Setup(r => r.WaitForTaskAsync(TaskId, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) => {
                await Task.Delay(Timeout.Infinite, ct);
                return info;
            });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ShellToolHandlers.ReadBackgroundOutputAsync(registry.Object, TaskId, block: true, timeoutMs: 600_000, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("调用方取消（Ctrl+C/TUI 停止）不能被误判成等待超时");
    }

    [Fact]
    public async Task ReadBackgroundOutputAsync_UnknownTask_ReturnsErrorResult() {
        var registry = new Mock<ISystemActuatorRegistry>();
        registry.Setup(r => r.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemActuatorBackgroundTaskInfo?)null);

        var result = await ShellToolHandlers.ReadBackgroundOutputAsync(registry.Object, "missing", block: null, timeoutMs: null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Task not found");
    }

    [Fact]
    public void FormatBackgroundOutput_EmptyOutput_SaysNoOutputYet() {
        var text = ShellToolHandlers.FormatBackgroundOutput(
            CreateInfo(TaskExecutionStatus.Completed),
            ShellToolHandlers.ShellOutputRetrieval.Success,
            output: null);

        text.Should().Contain("<output>(no output yet)</output>");
    }

    private static SystemActuatorBackgroundTaskInfo CreateInfo(TaskExecutionStatus status, int? exitCode = null) =>
        new() {
            TaskId = TaskId,
            Command = "echo bg",
            Status = status,
            ExitCode = exitCode,
        };
}
