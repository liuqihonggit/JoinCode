namespace Hands.Tests.Shell;

/// <summary>
/// ShellBackgroundMiddleware 单元测试 — 后台上下文所有权必须移交给注册表，禁止中间件提前释放。
/// </summary>
public sealed class ShellBackgroundMiddlewareTests {
    [Fact]
    public async Task Background_RegistrationSucceeds_DoesNotDisposeContext() {
        var resultTcs = new TaskCompletionSource<SystemActuatorExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmdContext = CreateCommandContext("task-1", resultTcs.Task);
        var registry = CreateRegistry(cmdContext.Object);
        await using var sut = new ShellBackgroundMiddleware(registry.Object);

        await sut.InvokeAsync(CreateContext(cmdContext.Object), (_, _) => Task.CompletedTask, CancellationToken.None);

        cmdContext.Verify(c => c.DisposeAsync(), Times.Never, "后台上下文所有权已移交注册表，提前释放会取消进程等待并杀死后台任务");
        registry.Verify(r => r.RegisterContextAsync(cmdContext.Object, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Background_RegistrationThrows_DisposesContextAndRethrows() {
        var resultTcs = new TaskCompletionSource<SystemActuatorExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmdContext = CreateCommandContext("task-2", resultTcs.Task);
        var registry = new Mock<ISystemActuatorRegistry>();
        registry.Setup(r => r.RegisterContextAsync(cmdContext.Object, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("registry failed"));
        await using var sut = new ShellBackgroundMiddleware(registry.Object);

        var act = () => sut.InvokeAsync(CreateContext(cmdContext.Object), (_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("registry failed");
        cmdContext.Verify(c => c.DisposeAsync(), Times.Once, "注册失败时必须释放上下文，避免进程/输出收集器泄漏");
    }

    private static Mock<ISystemActuatorCommandContext> CreateCommandContext(string taskId, Task<SystemActuatorExecutionResult> resultTask) {
        var cmdContext = new Mock<ISystemActuatorCommandContext>();
        cmdContext.SetupGet(c => c.TaskId).Returns(taskId);
        cmdContext.SetupGet(c => c.Command).Returns("echo bg");
        cmdContext.SetupGet(c => c.ResultTask).Returns(resultTask);
        cmdContext.Setup(c => c.Background(It.IsAny<string>())).Returns(true);
        cmdContext.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return cmdContext;
    }

    private static Mock<ISystemActuatorRegistry> CreateRegistry(ISystemActuatorCommandContext cmdContext) {
        var registry = new Mock<ISystemActuatorRegistry>();
        registry.Setup(r => r.RegisterContextAsync(cmdContext, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemActuatorBackgroundTaskInfo {
                TaskId = cmdContext.TaskId,
                Command = cmdContext.Command,
                Status = TaskExecutionStatus.Running
            });
        return registry;
    }

    private static ShellPipelineContext CreateContext(ISystemActuatorCommandContext cmdContext) {
        var provider = new Mock<ISystemActuator>();
        provider.SetupGet(p => p.Kind).Returns(SystemActuatorKind.Bash);
        provider.Setup(p => p.StartWithBackgroundSupportAsync(
                "echo bg", null, null, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmdContext);
        return new ShellPipelineContext {
            Command = "echo bg",
            Provider = provider.Object,
            Background = true
        };
    }
}
