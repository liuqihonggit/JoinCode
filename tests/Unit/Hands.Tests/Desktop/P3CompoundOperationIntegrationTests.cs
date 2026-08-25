namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P3 复合操作集成测试 — 真实桌面执行右键菜单链/多步点击/拖拽悬停，截图验证效果
/// 串行化运行（防止多测试并行启动多个记事本互相干扰）
/// 用 process.MainWindowHandle 精确关联窗口，每步验证前台句柄一致性
/// </summary>
[Trait("Category", "Integration")]
[Collection("DesktopIntegration")]
public sealed class P3CompoundOperationIntegrationTests
{
    /// <summary>启动记事本并获取精确窗口句柄</summary>
    private static async Task<(System.Diagnostics.Process Process, IntPtr Hwnd, RECT Rect)> StartNotepadAsync(Win32WindowManagementService windows)
    {
        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        await Task.Delay(2500);
        notepad.Refresh();
        var hwnd = notepad.MainWindowHandle;
        hwnd.Should().NotBe(IntPtr.Zero, "应能获取记事本主窗口句柄");

        await windows.FocusAsync(hwnd);
        await Task.Delay(800);
        User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "激活后前台应是记事本");

        User32NativeMethods.GetWindowRect(hwnd, out var rect);
        rect.Width.Should().BeGreaterThan(0, "窗口宽度应大于0");
        return (notepad, hwnd, rect);
    }

    /// <summary>右键菜单链：记事本编辑区右键 → 等待菜单弹出 → 截图验证 → Escape 关闭</summary>
    [Fact]
    public async Task RightClickMenu_Notepad_PopupAppears_ThenEscape()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();
        var handler = new CompoundOperationToolHandlers(input);

        var (notepad, hwnd, rect) = await StartNotepadAsync(windows);
        try
        {
            var centerX = rect.Left + rect.Width / 2;
            var centerY = rect.Top + rect.Height * 2 / 3;

            var typeOp = await input.TypeTextAsync("Hello P3 test");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "输入后焦点应仍在记事本");

            var screenshotBefore = await capture.CaptureFullScreenAsync();
            screenshotBefore.Should().StartWith("iVBORw0KGgo");

            var rightClickOp = await input.ClickAsync(centerX, centerY, MouseAction.RightClick);
            rightClickOp.Succeeded.Should().BeTrue("右键点击应成功");
            await Task.Delay(800);

            var screenshotWithMenu = await capture.CaptureFullScreenAsync();
            screenshotWithMenu.Should().StartWith("iVBORw0KGgo");
            screenshotWithMenu.Should().NotBe(screenshotBefore, "右键菜单弹出/光标变化后截图应与之前不同");

            var escapeOp = await input.KeyPressAsync(0x1B, KeyModifier.None);
            escapeOp.Succeeded.Should().BeTrue("按 Escape 应成功");
            await Task.Delay(500);

            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "关闭菜单后焦点应回到记事本");
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

    /// <summary>多步点击：在记事本编辑区点击多个位置，验证焦点保持</summary>
    [Fact]
    public async Task MultiClick_Notepad_FocusPreserved()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();
        var handler = new CompoundOperationToolHandlers(input);

        var (notepad, hwnd, rect) = await StartNotepadAsync(windows);
        try
        {
            var p1 = $"{rect.Left + rect.Width / 3},{rect.Top + rect.Height * 2 / 3}";
            var p2 = $"{rect.Left + rect.Width / 2},{rect.Top + rect.Height * 2 / 3}";
            var p3 = $"{rect.Left + rect.Width * 2 / 3},{rect.Top + rect.Height * 2 / 3}";
            var coords = $"{p1};{p2};{p3}";

            var result = await handler.MultiClickAsync(coords, 300);
            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("3 步点击序列");

            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "多步点击后焦点应仍在记事本");

            var typeOp = await input.TypeTextAsync("multi-click works");
            typeOp.Succeeded.Should().BeTrue("点击后输入应成功");
            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "输入后焦点应仍在记事本");

            var screenshot = await capture.CaptureWindowAsync(hwnd);
            screenshot.Should().StartWith("iVBORw0KGgo");
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

    /// <summary>拖拽悬停：在记事本内拖拽选中文本，验证不崩溃且焦点保持</summary>
    [Fact]
    public async Task DragWithHover_Notepad_NoCrash()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();
        var handler = new CompoundOperationToolHandlers(input);

        var (notepad, hwnd, rect) = await StartNotepadAsync(windows);
        try
        {
            var typeOp = await input.TypeTextAsync("Drag this text to select");
            typeOp.Succeeded.Should().BeTrue();
            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "输入后焦点应仍在记事本");

            var fromX = rect.Left + rect.Width / 3;
            var fromY = rect.Top + rect.Height * 2 / 3;
            var toX = rect.Left + rect.Width * 2 / 3;
            var toY = fromY;

            var result = await handler.DragWithHoverAsync(fromX, fromY, toX, toY, 300);
            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("拖拽完成");

            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "拖拽后焦点应仍在记事本");

            var copyOp = await input.KeyPressAsync(0x43, KeyModifier.Control);
            copyOp.Succeeded.Should().BeTrue("Ctrl+C 复制应成功");
            await Task.Delay(300);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "复制后焦点应仍在记事本");
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
}
