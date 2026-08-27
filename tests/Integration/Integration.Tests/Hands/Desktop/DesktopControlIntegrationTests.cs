namespace Integration.Tests;

/// <summary>
/// P0 E2E 集成验收 — 记事本全链路：启动→查找窗口→激活→输入文本→截图→关闭
/// 对应 PRD §6.3 M1 验收场景，串行化运行
/// </summary>
[Trait("Category", "Integration")]
[Collection("DesktopIntegration")]
public sealed class DesktopControlIntegrationTests
{
    [Fact]
    public async Task FullFlow_Notepad_FindFocusTypeScreenshot_Close()
    {
        var input = new Win32DesktopInputService(new NoOpDesktopSafetyChecker());
        var windows = new Win32WindowManagementService();
        var capture = new GdiScreenCaptureService();

        var notepad = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            notepad.Should().NotBeNull();

            await Task.Delay(2500);
            notepad.Refresh();
            var hwnd = notepad.MainWindowHandle;
            hwnd.Should().NotBe(IntPtr.Zero, "应能获取记事本主窗口句柄");

            var focused = await windows.FocusAsync(hwnd);
            focused.Should().BeTrue("应能激活记事本窗口");
            await Task.Delay(800);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "激活后前台应是记事本");

            var typeOp = await input.TypeTextAsync("hello world");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(500);
            User32NativeMethods.GetForegroundWindow().Should().Be(hwnd, "输入后焦点应仍在记事本");

            var screenshot = await capture.CaptureWindowAsync(hwnd);
            screenshot.Should().NotBeEmpty("窗口截图应返回非空 base64");
            screenshot.Should().StartWith("iVBORw0KGgo", "应为 PNG base64 格式");

            var listOp = await windows.EnumerateAsync();
            listOp.Should().NotBeEmpty("枚举窗口应返回列表");
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
    public async Task ScreenCapture_FullScreen_ReturnsValidPng()
    {
        var capture = new GdiScreenCaptureService();

        var base64 = await capture.CaptureFullScreenAsync();

        base64.Should().NotBeEmpty("全屏截图应返回非空");
        base64.Should().StartWith("iVBORw0KGgo", "应为 PNG base64");
    }

    [Fact]
    public async Task WindowManager_Enumerate_ReturnsNonEmptyOnDesktop()
    {
        var windows = new Win32WindowManagementService();

        var list = await windows.EnumerateAsync();

        list.Should().NotBeEmpty("桌面环境应至少有一个可见窗口");
    }
}
