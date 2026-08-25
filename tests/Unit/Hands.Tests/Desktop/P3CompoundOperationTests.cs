namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P3 复合操作 + 进程干预单元测试
/// </summary>
public sealed class P3CompoundOperationTests
{
    private static DesktopOperation SuccessOp() =>
        new(DesktopOperationKind.Click, 0, 0, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null);

    private static DesktopOperation FailOp(string error) =>
        new(DesktopOperationKind.Click, 0, 0, null, MouseAction.Click, null, DateTimeOffset.UtcNow, false, error);

    #region ParseCoordinateList

    [Fact]
    public void ParseCoordinateList_ValidInput_ReturnsPoints()
    {
        var result = CompoundOperationToolHandlers.ParseCoordinateList("100,200;300,400;500,600");

        result.Should().HaveCount(3);
        result[0].Should().Be((100, 200));
        result[1].Should().Be((300, 400));
        result[2].Should().Be((500, 600));
    }

    [Fact]
    public void ParseCoordinateList_SinglePair_ReturnsOnePoint()
    {
        var result = CompoundOperationToolHandlers.ParseCoordinateList("50,75");
        result.Should().HaveCount(1);
        result[0].Should().Be((50, 75));
    }

    [Fact]
    public void ParseCoordinateList_InvalidInput_ReturnsEmpty()
    {
        CompoundOperationToolHandlers.ParseCoordinateList("").Should().BeEmpty();
        CompoundOperationToolHandlers.ParseCoordinateList("abc").Should().BeEmpty();
        CompoundOperationToolHandlers.ParseCoordinateList("1,2;abc;3,4").Should().HaveCount(2);
    }

    [Fact]
    public void ParseCoordinateList_WithSpaces_ParsesCorrectly()
    {
        var result = CompoundOperationToolHandlers.ParseCoordinateList(" 100 , 200 ; 300 , 400 ");
        result.Should().HaveCount(2);
        result[0].Should().Be((100, 200));
    }

    #endregion

    #region RightClickMenuAsync

    [Fact]
    public async Task RightClickMenu_BothClicksSucceed_ReturnsSuccess()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp());
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.RightClickMenuAsync(100, 200, 150, 250, 300);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("右键菜单链完成");
        result.Content[0].Text.Should().Contain("(100,200)");
        result.Content[0].Text.Should().Contain("(150,250)");

        inputMock.Verify(i => i.ClickAsync(100, 200, MouseAction.RightClick, It.IsAny<CancellationToken>()), Times.Once);
        inputMock.Verify(i => i.ClickAsync(150, 250, MouseAction.Click, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RightClickMenu_RightClickFails_ReturnsError()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), MouseAction.RightClick, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailOp("右键失败"));
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.RightClickMenuAsync(100, 200, 150, 250);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("右键点击失败");
    }

    #endregion

    #region DragWithHoverAsync

    [Fact]
    public async Task DragWithHover_DragSucceeds_NoPopupItem_ReturnsSuccess()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.DragAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp());
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.DragWithHoverAsync(10, 20, 100, 200, 500);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("拖拽完成");
        inputMock.Verify(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), MouseAction.Click, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DragWithHover_WithPopupItem_ClicksPopupItem()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.DragAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp());
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp());
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.DragWithHoverAsync(10, 20, 100, 200, 500, 150, 250);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("点击弹出项");
        inputMock.Verify(i => i.ClickAsync(150, 250, MouseAction.Click, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DragWithHover_DragFails_ReturnsError()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.DragAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailOp("拖拽失败"));
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.DragWithHoverAsync(10, 20, 100, 200);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("拖拽失败");
    }

    #endregion

    #region MultiClickAsync

    [Fact]
    public async Task MultiClick_ValidCoordinates_ClicksAllPoints()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp());
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.MultiClickAsync("100,200;300,400;500,600", 100);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("3 步点击序列");
        inputMock.Verify(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task MultiClick_InvalidCoordinates_ReturnsError()
    {
        var inputMock = new Mock<IDesktopInputService>();
        var handler = new CompoundOperationToolHandlers(inputMock.Object);

        var result = await handler.MultiClickAsync("invalid");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("解析失败");
    }

    #endregion

    #region ProcessToolHandlers

    [Fact]
    public async Task ListProcesses_NoFilter_ReturnsNonEmptyList()
    {
        var handler = new ProcessToolHandlers();

        var result = await handler.ListProcessesAsync();

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("进程");
    }

    [Fact]
    public async Task ListProcesses_WithNameFilter_ReturnsFiltered()
    {
        var handler = new ProcessToolHandlers();

        var result = await handler.ListProcessesAsync("notepad");

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("过滤: notepad");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartAndKillProcess_Notepad_StartedThenKilled()
    {
        var handler = new ProcessToolHandlers();

        var startResult = await handler.StartProcessAsync("notepad.exe");
        startResult.IsError.Should().BeFalse();
        startResult.Content[0].Text.Should().Contain("PID=");

        var pidText = startResult.Content[0].Text!;
        var pidStart = pidText.IndexOf("PID=") + 4;
        var pid = int.Parse(pidText.AsSpan(pidStart));

        await Task.Delay(1000);

        var killResult = await handler.KillProcessAsync(pid: pid);
        killResult.IsError.Should().BeFalse();
        killResult.Content[0].Text.Should().Contain("成功");
    }

    [Fact]
    public async Task KillProcess_NonExistentName_ReturnsError()
    {
        var handler = new ProcessToolHandlers();

        var result = await handler.KillProcessAsync(name: "nonexistent_process_xyz_12345");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("未找到");
    }

    #endregion
}
