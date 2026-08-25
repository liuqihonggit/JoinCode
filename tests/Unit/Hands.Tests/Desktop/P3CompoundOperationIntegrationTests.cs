namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P3 复合操作集成测试 — 真实桌面执行右键菜单链/多步点击/拖拽悬停，截图验证效果
/// 关键：每步验证焦点一致性，操作后截图确认效果
/// </summary>
[Trait("Category", "DesktopIntegration")]
public sealed class P3CompoundOperationIntegrationTests
{
    /// <summary>右键菜单链：记事本编辑区右键 → 等待菜单弹出 → 截图验证 → Escape 关闭</summary>
    [Fact]
    public async Task RightClickMenu_Notepad_PopupAppears_ThenEscape()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();
        var handler = new CompoundOperationToolHandlers(input);

        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            await Task.Delay(2000);

            var window = await windows.FindAsync("记事本") ?? await windows.FindAsync("Notepad") ?? await windows.FindAsync("Untitled");
            window.Should().NotBeNull("应能找到记事本窗口");
            var hwnd = window!.Handle;

            await windows.FocusAsync(hwnd);
            await Task.Delay(800);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "激活后前台应是记事本");

            var rect = window.Rect;
            var centerX = rect.X + rect.Width / 2;
            var centerY = rect.Y + rect.Height * 2 / 3;

            var typeOp = await input.TypeTextAsync("Hello P3 test");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(500);

            var screenshotBefore = await capture.CaptureFullScreenAsync();
            screenshotBefore.Should().StartWith("iVBORw0KGgo");

            var rightClickOp = await input.ClickAsync(centerX, centerY, MouseAction.RightClick);
            rightClickOp.Succeeded.Should().BeTrue("右键点击应成功");
            await Task.Delay(800);

            var screenshotWithMenu = await capture.CaptureFullScreenAsync();
            screenshotWithMenu.Should().StartWith("iVBORw0KGgo");
            screenshotWithMenu.Should().NotBe(screenshotBefore, "右键菜单弹出后截图应与之前不同");

            var escapeOp = await input.KeyPressAsync(0x1B, KeyModifier.None);
            escapeOp.Succeeded.Should().BeTrue("按 Escape 应成功");
            await Task.Delay(500);

            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "关闭菜单后焦点应回到记事本");

            var screenshotAfterEscape = await capture.CaptureFullScreenAsync();
            screenshotAfterEscape.Should().StartWith("iVBORw0KGgo");
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

        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            await Task.Delay(2000);

            var window = await windows.FindAsync("记事本") ?? await windows.FindAsync("Notepad") ?? await windows.FindAsync("Untitled");
            window.Should().NotBeNull();
            var hwnd = window!.Handle;

            await windows.FocusAsync(hwnd);
            await Task.Delay(800);

            var rect = window.Rect;
            var p1 = $"{rect.X + rect.Width / 3},{rect.Y + rect.Height * 2 / 3}";
            var p2 = $"{rect.X + rect.Width / 2},{rect.Y + rect.Height * 2 / 3}";
            var p3 = $"{rect.X + rect.Width * 2 / 3},{rect.Y + rect.Height * 2 / 3}";
            var coords = $"{p1};{p2};{p3}";

            var result = await handler.MultiClickAsync(coords, 300);
            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("3 步点击序列");

            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "多步点击后焦点应仍在记事本");

            var typeOp = await input.TypeTextAsync("multi-click works");
            typeOp.Succeeded.Should().BeTrue("点击后输入应成功");

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

    /// <summary>拖拽悬停：在记事本内拖拽选中文本，验证不崩溃</summary>
    [Fact]
    public async Task DragWithHover_Notepad_NoCrash()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();
        var handler = new CompoundOperationToolHandlers(input);

        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            await Task.Delay(2000);

            var window = await windows.FindAsync("记事本") ?? await windows.FindAsync("Notepad") ?? await windows.FindAsync("Untitled");
            window.Should().NotBeNull();
            var hwnd = window!.Handle;

            await windows.FocusAsync(hwnd);
            await Task.Delay(800);

            var typeOp = await input.TypeTextAsync("Drag this text to select");
            typeOp.Succeeded.Should().BeTrue();
            await Task.Delay(500);

            var rect = window.Rect;
            var fromX = rect.X + rect.Width / 3;
            var fromY = rect.Y + rect.Height * 2 / 3;
            var toX = rect.X + rect.Width * 2 / 3;
            var toY = fromY;

            var result = await handler.DragWithHoverAsync(fromX, fromY, toX, toY, 300);
            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("拖拽完成");

            await Task.Delay(500);

            var screenshot = await capture.CaptureWindowAsync(hwnd);
            screenshot.Should().StartWith("iVBORw0KGgo");

            var copyOp = await input.KeyPressAsync(0x43, KeyModifier.Control);
            copyOp.Succeeded.Should().BeTrue("Ctrl+C 复制应成功");
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
