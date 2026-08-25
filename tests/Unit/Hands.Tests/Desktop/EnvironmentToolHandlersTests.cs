namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// EnvironmentToolHandlers 单元测试 — 验证 get_environment_state/wait_for_idle/undo_last_action/get_operation_history
/// </summary>
public sealed class EnvironmentToolHandlersTests
{
    private static Mock<IEnvironmentAwarenessService> CreateEnvMock()
    {
        var mock = new Mock<IEnvironmentAwarenessService>();
        mock.Setup(e => e.GetCursorStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CursorState.Normal);
        mock.Setup(e => e.DetectPopupAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PopupInfo?)null);
        return mock;
    }

    private static Mock<IUndoStack> CreateUndoMock()
    {
        var mock = new Mock<IUndoStack>();
        mock.SetupGet(u => u.Count).Returns(0);
        return mock;
    }

    [Fact]
    public async Task GetEnvironmentState_NormalCursorNoPopup_ReturnsStateInfo()
    {
        var envMock = CreateEnvMock();
        var undoMock = CreateUndoMock();
        undoMock.SetupGet(u => u.Count).Returns(3);
        var handler = new EnvironmentToolHandlers(envMock.Object, undoMock.Object);

        var result = await handler.GetEnvironmentStateAsync();

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("Normal");
        text.Should().Contain("无弹窗");
        text.Should().Contain("撤销栈深度: 3");
    }

    [Fact]
    public async Task GetEnvironmentState_WaitCursorAndPopup_ReturnsWarning()
    {
        var envMock = new Mock<IEnvironmentAwarenessService>();
        envMock.Setup(e => e.GetCursorStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CursorState.Wait);
        envMock.Setup(e => e.DetectPopupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PopupInfo(IntPtr.Zero, "确认删除", null, PopupCategory.NeedsDecision));
        var undoMock = CreateUndoMock();
        var handler = new EnvironmentToolHandlers(envMock.Object, undoMock.Object);

        var result = await handler.GetEnvironmentStateAsync();

        var text = result.Content[0].Text!;
        text.Should().Contain("Wait");
        text.Should().Contain("异步操作进行中");
        text.Should().Contain("确认删除");
        text.Should().Contain("NeedsDecision");
        text.Should().Contain("需用户决策");
    }

    [Fact]
    public async Task WaitForIdle_ReturnsTrue_WhenIdle()
    {
        var envMock = new Mock<IEnvironmentAwarenessService>();
        envMock.Setup(e => e.WaitForIdleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new EnvironmentToolHandlers(envMock.Object, CreateUndoMock().Object);

        var result = await handler.WaitForIdleAsync(5);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("恢复空闲");
    }

    [Fact]
    public async Task WaitForIdle_ReturnsTimeout_WhenNotIdle()
    {
        var envMock = new Mock<IEnvironmentAwarenessService>();
        envMock.Setup(e => e.WaitForIdleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new EnvironmentToolHandlers(envMock.Object, CreateUndoMock().Object);

        var result = await handler.WaitForIdleAsync(5);

        result.Content[0].Text.Should().Contain("超时");
    }

    [Fact]
    public async Task UndoLastAction_EmptyStack_ReturnsEmptyMessage()
    {
        var undoMock = CreateUndoMock();
        undoMock.Setup(u => u.Pop()).Returns((DesktopOperation?)null);
        var handler = new EnvironmentToolHandlers(CreateEnvMock().Object, undoMock.Object);

        var result = await handler.UndoLastActionAsync();

        result.Content[0].Text.Should().Contain("为空");
    }

    [Fact]
    public async Task UndoLastAction_WithOperation_ReturnsUndoInfo()
    {
        var op = new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null);
        var undoMock = new Mock<IUndoStack>();
        undoMock.Setup(u => u.Pop()).Returns(op);
        undoMock.SetupGet(u => u.Count).Returns(2);
        var handler = new EnvironmentToolHandlers(CreateEnvMock().Object, undoMock.Object);

        var result = await handler.UndoLastActionAsync();

        var text = result.Content[0].Text!;
        text.Should().Contain("已撤销");
        text.Should().Contain("Click");
        text.Should().Contain("(100, 200)");
        text.Should().Contain("剩余可撤销: 2");
    }

    [Fact]
    public async Task GetOperationHistory_Empty_ReturnsEmptyMessage()
    {
        var undoMock = CreateUndoMock();
        undoMock.Setup(u => u.GetRecent(It.IsAny<int>())).Returns(Array.Empty<DesktopOperation>());
        var handler = new EnvironmentToolHandlers(CreateEnvMock().Object, undoMock.Object);

        var result = await handler.GetOperationHistoryAsync();

        result.Content[0].Text.Should().Contain("为空");
    }

    [Fact]
    public async Task GetOperationHistory_WithOperations_ReturnsFormattedHistory()
    {
        var ops = new[]
        {
            new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null),
            new DesktopOperation(DesktopOperationKind.TypeText, 0, 0, "hello", null, null, DateTimeOffset.UtcNow, true, null)
        };
        var undoMock = new Mock<IUndoStack>();
        undoMock.Setup(u => u.GetRecent(It.IsAny<int>())).Returns(ops);
        var handler = new EnvironmentToolHandlers(CreateEnvMock().Object, undoMock.Object);

        var result = await handler.GetOperationHistoryAsync(10);

        var text = result.Content[0].Text!;
        text.Should().Contain("2 步操作");
        text.Should().Contain("Click");
        text.Should().Contain("TypeText");
        text.Should().Contain("hello");
    }
}
