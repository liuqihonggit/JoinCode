namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P2 环境感知 + 撤销元意识单元测试
/// </summary>
public sealed class P2EnvironmentTests
{
    #region UndoStack

    [Fact]
    public void UndoStack_PushPop_LifoOrder()
    {
        var stack = new UndoStack();
        var op1 = new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null);
        var op2 = new DesktopOperation(DesktopOperationKind.KeyPress, 0, 0, "Enter", null, KeyModifier.None, DateTimeOffset.UtcNow, true, null);

        stack.Push(op1);
        stack.Push(op2);

        stack.Count.Should().Be(2);
        var popped = stack.Pop();
        popped.Should().Be(op2);
        popped = stack.Pop();
        popped.Should().Be(op1);
        stack.Count.Should().Be(0);
    }

    [Fact]
    public void UndoStack_PopEmpty_ReturnsNull()
    {
        var stack = new UndoStack();
        stack.Pop().Should().BeNull();
        stack.Count.Should().Be(0);
    }

    [Fact]
    public void UndoStack_Peek_DoesNotRemove()
    {
        var stack = new UndoStack();
        var op = new DesktopOperation(DesktopOperationKind.Click, 10, 20, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null);

        stack.Push(op);
        var peeked = stack.Peek();
        peeked.Should().Be(op);
        stack.Count.Should().Be(1);
    }

    [Fact]
    public void UndoStack_PeekEmpty_ReturnsNull()
    {
        var stack = new UndoStack();
        stack.Peek().Should().BeNull();
    }

    [Fact]
    public void UndoStack_GetRecent_ReturnsLatestN()
    {
        var stack = new UndoStack();
        for (var i = 0; i < 5; i++)
        {
            stack.Push(new DesktopOperation(DesktopOperationKind.Click, i, 0, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null));
        }

        var recent = stack.GetRecent(3);
        recent.Should().HaveCount(3);
        recent[0].X.Should().Be(4);
        recent[1].X.Should().Be(3);
        recent[2].X.Should().Be(2);
    }

    [Fact]
    public void UndoStack_GetRecent_ZeroOrNegative_ReturnsEmpty()
    {
        var stack = new UndoStack();
        stack.Push(new DesktopOperation(DesktopOperationKind.Click, 0, 0, null, null, null, DateTimeOffset.UtcNow, true, null));

        stack.GetRecent(0).Should().BeEmpty();
        stack.GetRecent(-1).Should().BeEmpty();
    }

    [Fact]
    public void UndoStack_Clear_EmptiesStack()
    {
        var stack = new UndoStack();
        stack.Push(new DesktopOperation(DesktopOperationKind.Click, 0, 0, null, null, null, DateTimeOffset.UtcNow, true, null));
        stack.Push(new DesktopOperation(DesktopOperationKind.Click, 1, 0, null, null, null, DateTimeOffset.UtcNow, true, null));

        stack.Clear();
        stack.Count.Should().Be(0);
        stack.Pop().Should().BeNull();
    }

    #endregion

    #region DesktopSafetyChecker

    [Fact]
    public async Task SafetyChecker_NoZones_CheckClickReturnsNone()
    {
        var checker = new DesktopSafetyChecker();
        var result = await checker.CheckClickAsync(100, 200);
        result.Should().Be(UnsafeOperationKind.None);
    }

    [Fact]
    public async Task SafetyChecker_RegisteredZone_CheckClickHitsReturnsDangerous()
    {
        var checker = new DesktopSafetyChecker();
        checker.RegisterDangerousZone(100, 200, 80, 30);

        var result = await checker.CheckClickAsync(140, 215);
        result.Should().Be(UnsafeOperationKind.DangerousCoordinate);
    }

    [Fact]
    public async Task SafetyChecker_RegisteredZone_CheckClickMissesReturnsNone()
    {
        var checker = new DesktopSafetyChecker();
        checker.RegisterDangerousZone(100, 200, 80, 30);

        var result = await checker.CheckClickAsync(500, 500);
        result.Should().Be(UnsafeOperationKind.None);
    }

    [Fact]
    public async Task SafetyChecker_ClearZones_CheckClickReturnsNone()
    {
        var checker = new DesktopSafetyChecker();
        checker.RegisterDangerousZone(100, 200, 80, 30);
        checker.ClearDangerousZones();

        var result = await checker.CheckClickAsync(140, 215);
        result.Should().Be(UnsafeOperationKind.None);
        checker.DangerousZoneCount.Should().Be(0);
    }

    [Fact]
    public async Task SafetyChecker_MultipleZones_CheckAnyHit()
    {
        var checker = new DesktopSafetyChecker();
        checker.RegisterDangerousZone(100, 200, 80, 30);
        checker.RegisterDangerousZone(500, 600, 100, 50);

        checker.DangerousZoneCount.Should().Be(2);
        (await checker.CheckClickAsync(140, 215)).Should().Be(UnsafeOperationKind.DangerousCoordinate);
        (await checker.CheckClickAsync(550, 625)).Should().Be(UnsafeOperationKind.DangerousCoordinate);
        (await checker.CheckClickAsync(300, 300)).Should().Be(UnsafeOperationKind.None);
    }

    [Fact]
    public async Task SafetyChecker_ZeroHandle_CheckWindowCloseReturnsNone()
    {
        var checker = new DesktopSafetyChecker();
        var result = await checker.CheckWindowCloseAsync(IntPtr.Zero);
        result.Should().Be(UnsafeOperationKind.None);
    }

    #endregion

    #region Win32EnvironmentAwarenessService.ClassifyPopup

    [Theory]
    [InlineData("确认删除", PopupCategory.NeedsDecision)]
    [InlineData("保存覆盖确认", PopupCategory.NeedsDecision)]
    [InlineData("是否替换文件", PopupCategory.NeedsDecision)]
    [InlineData("网络错误", PopupCategory.Retryable)]
    [InlineData("操作超时", PopupCategory.Retryable)]
    [InlineData("无法连接服务器", PopupCategory.Retryable)]
    [InlineData("系统通知", PopupCategory.Closeable)]
    [InlineData("提示信息", PopupCategory.Closeable)]
    public void ClassifyPopup_VariousTitles_ReturnsCorrectCategory(string title, PopupCategory expected)
    {
        Win32EnvironmentAwarenessService.ClassifyPopup(title).Should().Be(expected);
    }

    #endregion

    #region Win32EnvironmentAwarenessService.GetCursorState (real desktop)

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetCursorState_RealDesktop_ReturnsValidState()
    {
        var service = new Win32EnvironmentAwarenessService();
        var state = await service.GetCursorStateAsync();

        state.Should().BeOneOf(CursorState.Normal, CursorState.Wait, CursorState.AppStarting, CursorState.Help, CursorState.Unknown);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DetectPopup_RealDesktop_ReturnsNullOrPopupInfo()
    {
        var service = new Win32EnvironmentAwarenessService();
        var popup = await service.DetectPopupAsync();

        if (popup is not null)
        {
            popup.Category.Should().BeOneOf(PopupCategory.Closeable, PopupCategory.NeedsDecision, PopupCategory.Retryable);
            popup.Title.Should().NotBeEmpty();
        }
    }

    #endregion
}
