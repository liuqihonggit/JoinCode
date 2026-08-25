namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P0 E2E 集成验收 — 记事本全链路：启动→查找窗口→激活→输入文本→截图→关闭
/// 对应 PRD §6.3 M1 验收场景
/// </summary>
[Trait("Category", "DesktopIntegration")]
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

            await Task.Delay(1500);

            var window = await windows.FindAsync("记事本") ?? await windows.FindAsync("Notepad") ?? await windows.FindAsync("Untitled");
            window.Should().NotBeNull("应能找到记事本窗口");

            var focused = await windows.FocusAsync(window!.Handle);
            focused.Should().BeTrue("应能激活记事本窗口");
            await Task.Delay(500);

            var typeOp = await input.TypeTextAsync("hello world");
            typeOp.Succeeded.Should().BeTrue("输入文本应成功");
            await Task.Delay(500);

            var screenshot = await capture.CaptureWindowAsync(window.Handle);
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
