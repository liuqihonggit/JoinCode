namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P1 E2E 集成验收 — 视觉理解引导桌面操作全链路（PRD §6.3 M2 验收场景）
/// 串行化运行，用 process.MainWindowHandle 精确关联窗口，每步验证前台句柄一致性
/// </summary>
[Trait("Category", "DesktopIntegration")]
[Collection("DesktopIntegration")]
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
            await Task.Delay(2500);
            notepad.Refresh();
            var hwnd = notepad.MainWindowHandle;
            hwnd.Should().NotBe(IntPtr.Zero, "应能获取记事本主窗口句柄");

            await windows.FocusAsync(hwnd);
            await Task.Delay(800);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "激活后前台应是记事本");

            User32NativeMethods.GetWindowRect(hwnd, out var winRect);
            var editorCenterX = winRect.Left + winRect.Width / 2;
            var editorCenterY = winRect.Top + winRect.Height * 2 / 3;
            var editorW = winRect.Width / 2;
            var editorH = winRect.Height / 3;
            var editorX = editorCenterX - editorW / 2;
            var editorY = editorCenterY - editorH / 2;

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

            if (User32NativeMethods.GetForegroundWindow() != hwnd)
            {
                await windows.FocusAsync(hwnd);
                await Task.Delay(500);
            }

            var findResult = await visionHandler.FindElementAsync("文本编辑区");
            findResult.IsError.Should().BeFalse();
            findResult.Content[0].Text.Should().Contain("mouse_click");

            if (User32NativeMethods.GetForegroundWindow() != hwnd)
            {
                await windows.FocusAsync(hwnd);
                await Task.Delay(500);
            }

            var clickOp = await input.ClickAsync(editorCenterX, editorCenterY, MouseAction.Click);
            clickOp.Succeeded.Should().BeTrue("视觉引导点击应成功");
            await Task.Delay(800);

            if (User32NativeMethods.GetForegroundWindow() != hwnd)
            {
                await windows.FocusAsync(hwnd);
                await Task.Delay(500);
            }
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "点击后焦点应仍在记事本");

            var typeOp = await input.TypeTextAsync("Hello from vision");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(800);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "输入后焦点应仍在记事本");

            notepad.HasExited.Should().BeFalse("记事本应仍在运行");
            var screenshot = await capture.CaptureWindowAsync(hwnd);
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
