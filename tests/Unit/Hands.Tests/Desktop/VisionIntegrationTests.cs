namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P1 E2E 集成验收 — 视觉理解引导桌面操作全链路（PRD §6.3 M2 验收场景）
/// 真实截图 → Mock 多模态检测器（模拟 LLM 返回）→ find_element → mouse_click → type_text → 截图验证
/// </summary>
[Trait("Category", "DesktopIntegration")]
public sealed class VisionIntegrationTests
{
    [Fact]
    public async Task VisionGuidedFlow_Notepad_Screenshot_FindElement_Click_Type()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();

        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            await Task.Delay(1500);

            var window = await windows.FindAsync("记事本") ?? await windows.FindAsync("Notepad") ?? await windows.FindAsync("Untitled");
            window.Should().NotBeNull("应能找到记事本窗口");

            var focused = await windows.FocusAsync(window!.Handle);
            focused.Should().BeTrue("应能激活记事本窗口");
            await Task.Delay(500);

            var winRect = window.Rect;
            var editorX = winRect.X + 15;
            var editorY = winRect.Y + 60;
            var editorW = winRect.Width - 30;
            var editorH = winRect.Height - 80;

            var detectorMock = new Mock<IUiElementDetector>();
            detectorMock
                .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UiElement(UiElementType.TextBox, null, "记事本编辑区", editorX, editorY, editorW, editorH, ElementState.Focused, 0.90));
            detectorMock
                .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UiElementDetectionResult(
                    [new(UiElementType.TextBox, null, "记事本编辑区", editorX, editorY, editorW, editorH, ElementState.Focused, 0.90)],
                    1920, 1080));

            var visionHandler = new VisionToolHandlers(detectorMock.Object, capture);

            var detectResult = await visionHandler.DetectUiElementsAsync(null);
            detectResult.IsError.Should().BeFalse();
            detectResult.Content[0].Text.Should().Contain("1 个 UI 元素");
            detectResult.Content[0].Text.Should().Contain("TextBox");

            var findResult = await visionHandler.FindElementAsync("文本编辑区");
            findResult.IsError.Should().BeFalse();
            var findText = findResult.Content[0].Text!;
            findText.Should().Contain("TextBox");
            findText.Should().Contain("mouse_click");

            var clickX = editorX + editorW / 2;
            var clickY = editorY + editorH / 2;
            var clickOp = await input.ClickAsync(clickX, clickY, MouseAction.Click);
            clickOp.Succeeded.Should().BeTrue("视觉引导点击应成功");
            await Task.Delay(300);

            var typeOp = await input.TypeTextAsync("Hello from vision");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(500);

            var screenshot = await capture.CaptureWindowAsync(window.Handle);
            screenshot.Should().NotBeEmpty("最终截图应返回非空 base64");
            screenshot.Should().StartWith("iVBORw0KGgo", "应为 PNG base64 格式");
        }
        finally
        {
            if (notepad is { HasExited: false })
            {
                notepad.Kill();
                notepad.WaitForExit(3000);
            }
        }
    }

    [Fact]
    public async Task DetectUiElements_RealScreenshot_MockDetector_ReturnsFormattedText()
    {
        var capture = new GdiScreenCaptureService();

        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UiElementDetectionResult(
                [
                    new(UiElementType.Button, "确定", "确认按钮", 100, 200, 80, 30, ElementState.Normal, 0.95),
                    new(UiElementType.Button, "取消", "取消按钮", 200, 200, 80, 30, ElementState.Normal, 0.93),
                    new(UiElementType.TextBox, null, "输入框", 50, 100, 300, 25, ElementState.Focused, 0.88)
                ],
                1920, 1080));

        var handler = new VisionToolHandlers(detectorMock.Object, capture);

        var result = await handler.DetectUiElementsAsync(null);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("1920x1080");
        text.Should().Contain("3 个 UI 元素");
        text.Should().Contain("确定");
        text.Should().Contain("取消");
        text.Should().Contain("输入框");

        detectorMock.Verify(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindElement_RealScreenshot_MockDetector_ReturnsClickCoordinates()
    {
        var capture = new GdiScreenCaptureService();

        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UiElement(UiElementType.Button, "保存", "保存按钮", 400, 300, 80, 30, ElementState.Normal, 0.92));

        var handler = new VisionToolHandlers(detectorMock.Object, capture);

        var result = await handler.FindElementAsync("保存按钮");

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("Button");
        text.Should().Contain("保存");
        text.Should().Contain("x=440");
        text.Should().Contain("y=315");
    }
}
