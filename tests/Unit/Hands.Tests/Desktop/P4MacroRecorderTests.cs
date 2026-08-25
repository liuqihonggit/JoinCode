namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P4 宏录制单元测试
/// </summary>
public sealed class P4MacroRecorderTests
{
    private static DesktopOperation SuccessOp(DesktopOperationKind kind = DesktopOperationKind.Click) =>
        new(kind, 0, 0, null, null, null, DateTimeOffset.UtcNow, true, null);

    private static Mock<IDesktopInputService> CreateInputMock()
    {
        var mock = new Mock<IDesktopInputService>();
        mock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp(DesktopOperationKind.Click));
        mock.Setup(i => i.KeyPressAsync(It.IsAny<int>(), It.IsAny<KeyModifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp(DesktopOperationKind.KeyPress));
        mock.Setup(i => i.TypeTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp(DesktopOperationKind.TypeText));
        mock.Setup(i => i.MoveToAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp(DesktopOperationKind.Move));
        return mock;
    }

    #region Recording

    [Fact]
    public void StartRecording_SetsIsRecordingTrue_AndClearsPrevious()
    {
        var recorder = new MacroRecorder(CreateInputMock().Object, new Mock<IFileSystem>().Object);

        recorder.IsRecording.Should().BeFalse();
        recorder.StartRecording("test-macro");
        recorder.IsRecording.Should().BeTrue();

        recorder.RecordOperation(SuccessOp());
        recorder.RecordOperation(SuccessOp());

        recorder.StartRecording("test-macro-2");
        recorder.IsRecording.Should().BeTrue();

        var macro = recorder.StopRecording();
        macro.Operations.Should().HaveCount(0, "重新开始录制应清空之前的操作");
    }

    [Fact]
    public void RecordOperation_WhenNotRecording_DoesNothing()
    {
        var recorder = new MacroRecorder(CreateInputMock().Object, new Mock<IFileSystem>().Object);

        recorder.RecordOperation(SuccessOp());

        recorder.StartRecording("test");
        var macro = recorder.StopRecording();
        macro.Operations.Should().HaveCount(0, "未开始录制时记录的操作不应保存");
    }

    [Fact]
    public void StopRecording_ReturnsMacroWithRecordedOperations()
    {
        var recorder = new MacroRecorder(CreateInputMock().Object, new Mock<IFileSystem>().Object);

        recorder.StartRecording("my-macro");
        recorder.RecordOperation(new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null));
        recorder.RecordOperation(new DesktopOperation(DesktopOperationKind.TypeText, 0, 0, "hello", null, null, DateTimeOffset.UtcNow, true, null));

        var macro = recorder.StopRecording();

        macro.Name.Should().Be("my-macro");
        macro.Operations.Should().HaveCount(2);
        macro.Operations[0].Kind.Should().Be(DesktopOperationKind.Click);
        macro.Operations[1].Text.Should().Be("hello");
        recorder.IsRecording.Should().BeFalse();
    }

    #endregion

    #region Playback

    [Fact]
    public async Task PlayAsync_ExecutesAllOperations_ReturnsResult()
    {
        var inputMock = CreateInputMock();
        var recorder = new MacroRecorder(inputMock.Object, new Mock<IFileSystem>().Object);

        var macro = new Macro("test", new[]
        {
            new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null),
            new DesktopOperation(DesktopOperationKind.KeyPress, 0x0D, 0, null, null, KeyModifier.None, DateTimeOffset.UtcNow, true, null),
            new DesktopOperation(DesktopOperationKind.TypeText, 0, 0, "hello", null, null, DateTimeOffset.UtcNow, true, null)
        }, DateTimeOffset.UtcNow);

        var result = await recorder.PlayAsync(macro, speedMultiplier: 0);

        result.TotalSteps.Should().Be(3);
        result.SucceededSteps.Should().Be(3);
        result.FailedSteps.Should().Be(0);

        inputMock.Verify(i => i.ClickAsync(100, 200, MouseAction.Click, It.IsAny<CancellationToken>()), Times.Once);
        inputMock.Verify(i => i.KeyPressAsync(0x0D, KeyModifier.None, It.IsAny<CancellationToken>()), Times.Once);
        inputMock.Verify(i => i.TypeTextAsync("hello", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlayAsync_WithFailures_CountsFailedSteps()
    {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopOperation(DesktopOperationKind.Click, 0, 0, null, MouseAction.Click, null, DateTimeOffset.UtcNow, false, "error"));
        inputMock.Setup(i => i.TypeTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessOp(DesktopOperationKind.TypeText));

        var recorder = new MacroRecorder(inputMock.Object, new Mock<IFileSystem>().Object);

        var macro = new Macro("test", new[]
        {
            new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null),
            new DesktopOperation(DesktopOperationKind.TypeText, 0, 0, "hello", null, null, DateTimeOffset.UtcNow, true, null)
        }, DateTimeOffset.UtcNow);

        var result = await recorder.PlayAsync(macro, speedMultiplier: 0);

        result.SucceededSteps.Should().Be(1);
        result.FailedSteps.Should().Be(1);
    }

    [Fact]
    public async Task PlayAsync_EmptyMacro_ReturnsZero()
    {
        var recorder = new MacroRecorder(CreateInputMock().Object, new Mock<IFileSystem>().Object);
        var macro = new Macro("empty", [], DateTimeOffset.UtcNow);

        var result = await recorder.PlayAsync(macro);

        result.TotalSteps.Should().Be(0);
        result.SucceededSteps.Should().Be(0);
    }

    #endregion

    #region Save/Load

    [Fact]
    public void SaveMacro_WritesJsonToFile()
    {
        var fsMock = new Mock<IFileSystem>();
        var recorder = new MacroRecorder(CreateInputMock().Object, fsMock.Object);

        var macro = new Macro("test", new[]
        {
            new DesktopOperation(DesktopOperationKind.Click, 100, 200, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null)
        }, DateTimeOffset.UtcNow);

        recorder.SaveMacro(macro, "/tmp/test.json");

        fsMock.Verify(fs => fs.WriteAllText("/tmp/test.json", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void LoadMacro_ReadsJsonFromFile()
    {
        var json = """{"Name":"loaded","Operations":[{"Kind":1,"X":100,"Y":200,"Text":null,"MouseAction":1,"Modifiers":null,"Timestamp":"2026-01-01T00:00:00Z","Succeeded":true,"Error":null}],"CreatedAt":"2026-01-01T00:00:00Z"}""";
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(fs => fs.ReadAllText("/tmp/test.json")).Returns(json);

        var recorder = new MacroRecorder(CreateInputMock().Object, fsMock.Object);

        var macro = recorder.LoadMacro("/tmp/test.json");

        macro.Name.Should().Be("loaded");
        macro.Operations.Should().HaveCount(1);
        macro.Operations[0].Kind.Should().Be(DesktopOperationKind.Click);
        macro.Operations[0].X.Should().Be(100);
    }

    #endregion

    #region MacroToolHandlers

    [Fact]
    public async Task StartRecording_ValidName_ReturnsSuccess()
    {
        var recorderMock = new Mock<IMacroRecorder>();
        var handler = new MacroToolHandlers(recorderMock.Object, new Mock<IFileSystem>().Object);

        var result = await handler.StartRecordingAsync("test-macro");

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("test-macro");
        recorderMock.Verify(r => r.StartRecording("test-macro"), Times.Once);
    }

    [Fact]
    public async Task StopRecording_NotRecording_ReturnsError()
    {
        var recorderMock = new Mock<IMacroRecorder>();
        recorderMock.SetupGet(r => r.IsRecording).Returns(false);
        var handler = new MacroToolHandlers(recorderMock.Object, new Mock<IFileSystem>().Object);

        var result = await handler.StopRecordingAsync();

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("未在录制");
    }

    [Fact]
    public async Task StopRecording_Recording_ReturnsMacroInfo()
    {
        var recorderMock = new Mock<IMacroRecorder>();
        recorderMock.SetupGet(r => r.IsRecording).Returns(true);
        recorderMock.Setup(r => r.StopRecording())
            .Returns(new Macro("test", new[] { SuccessOp() }, DateTimeOffset.UtcNow));
        var handler = new MacroToolHandlers(recorderMock.Object, new Mock<IFileSystem>().Object);

        var result = await handler.StopRecordingAsync();

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("test");
        result.Content[0].Text.Should().Contain("1 步");
    }

    [Fact]
    public async Task PlayMacro_LoadFails_ReturnsError()
    {
        var recorderMock = new Mock<IMacroRecorder>();
        recorderMock.Setup(r => r.LoadMacro(It.IsAny<string>()))
            .Throws(new FileNotFoundException("文件不存在"));
        var handler = new MacroToolHandlers(recorderMock.Object, new Mock<IFileSystem>().Object);

        var result = await handler.PlayMacroAsync("/nonexistent.json");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("加载宏失败");
    }

    [Fact]
    public async Task PlayMacro_LoadSucceeds_ReturnsPlaybackResult()
    {
        var macro = new Macro("test", new[] { SuccessOp() }, DateTimeOffset.UtcNow);
        var recorderMock = new Mock<IMacroRecorder>();
        recorderMock.Setup(r => r.LoadMacro(It.IsAny<string>())).Returns(macro);
        recorderMock.Setup(r => r.PlayAsync(It.IsAny<Macro>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MacroPlaybackResult(1, 1, 0, TimeSpan.FromMilliseconds(100)));
        var handler = new MacroToolHandlers(recorderMock.Object, new Mock<IFileSystem>().Object);

        var result = await handler.PlayMacroAsync("/tmp/test.json", 2);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("总步骤: 1");
        result.Content[0].Text.Should().Contain("成功: 1");
        result.Content[0].Text.Should().Contain("2x");
    }

    #endregion
}
