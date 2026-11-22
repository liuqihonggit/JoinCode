namespace Hands.Tests.Shell;

/// <summary>
/// 真机路径集成：真实 <see cref="SystemActuatorRegistry"/> 的状态机 / 等待轮询 / 输出拼接，
/// 只把进程上下文替换成可控假件（serve 模式下 bash 需人工确认，无法非交互真跑进程）。
/// </summary>
public sealed class ShellBackgroundOutputIntegrationTests {
    [Fact]
    public async Task RealRegistry_NotReadyThenTimeoutThenSuccess() {
        await using var registry = new SystemActuatorRegistry(TestFileSystem.Current);
        var resultTcs = new TaskCompletionSource<SystemActuatorExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = "partial line";
        var context = new Mock<ISystemActuatorCommandContext>();
        context.SetupGet(c => c.TaskId).Returns("real-task-1");
        context.SetupGet(c => c.Command).Returns("sleep 6");
        context.SetupGet(c => c.ResultTask).Returns(resultTcs.Task);
        context.Setup(c => c.GetCurrentStdout()).Returns(() => stdout);
        context.Setup(c => c.GetCurrentStderr()).Returns(string.Empty);

        await registry.RegisterContextAsync(context.Object);

        var notReady = await ShellToolHandlers.ReadBackgroundOutputAsync(
            registry, "real-task-1", block: false, timeoutMs: null, CancellationToken.None);
        notReady.IsError.Should().BeFalse();
        notReady.Content[0].Text.Should()
            .Contain("<retrieval_status>not_ready</retrieval_status>")
            .And.Contain("<status>running</status>")
            .And.Contain("partial line");

        var timedOut = await ShellToolHandlers.ReadBackgroundOutputAsync(
            registry, "real-task-1", block: true, timeoutMs: 150, CancellationToken.None);
        timedOut.IsError.Should().BeFalse();
        timedOut.Content[0].Text.Should()
            .Contain("<retrieval_status>timeout</retrieval_status>")
            .And.Contain("<status>running</status>");

        stdout = "partial line\nfinal line";
        resultTcs.SetResult(new SystemActuatorExecutionResult { Stdout = "final line", Stderr = string.Empty, ExitCode = 0 });

        var success = await ShellToolHandlers.ReadBackgroundOutputAsync(
            registry, "real-task-1", block: true, timeoutMs: 3_000, CancellationToken.None);
        success.IsError.Should().BeFalse();
        success.Content[0].Text.Should()
            .Contain("<retrieval_status>success</retrieval_status>")
            .And.Contain("<status>completed</status>")
            .And.Contain("<exit_code>0</exit_code>")
            .And.Contain("final line")
            .And.NotContain("仍在后台运行");
    }
}
