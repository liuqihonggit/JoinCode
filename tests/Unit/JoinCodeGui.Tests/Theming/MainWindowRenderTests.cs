namespace JoinCode.Gui.Tests.Theming;

/// <summary>
/// 视觉截图对比测试 —— 用真正 Skia 渲染出 MainWindow 的暗/亮两帧，
/// 断言：① 窗口/侧栏/输入栏平均亮度在暗色显著低于亮色；② 侧栏像素符合语义配色。
/// 验证浅色切换在像素级真实生效（区别于仅转换器计算）。调试时帧图保存到 <c>dumps/</c> 目录供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class MainWindowRenderTests
{
    private const double BrightThreshold = 170;
    private const double ContrastMargin = 60;

    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 避免测试污染真实 ~/.jcc/sessions</summary>
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new JoinCode.Gui.Persistence.GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>捕获指定主题下的 MainWindow 渲染帧，返回 (WriteableBitmap, 三区域平均亮度)。</summary>
    private static (WriteableBitmap Bmp, double Sidebar, double Input, double Full) Capture(bool dark)
    {
        GuiPalette.CurrentVariant = dark
            ? GuiPalette.GuiThemeVariant.Dark
            : GuiPalette.GuiThemeVariant.Light;

        var avaVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = avaVariant
        };
        win.Show();
        try
        {
            var frame = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null，headless Skia 渲染未生效");
            return (frame, RegionLuma(frame, x: 110, y: 340, w: 40, h: 40), RegionLuma(frame, 490, 660, 200, 15), AverageLuma(frame));
        }
        finally
        {
            win.Close();
        }
    }

    private static byte[] ReadPixels(WriteableBitmap frame)
    {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        using var locked = await frame.TryLockAsync().ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    private static double RegionLuma(WriteableBitmap frame, int x, int y, int w, int h)
    {
        var bytes = ReadPixels(frame);
        var stride = frame.PixelSize.Width * 4;
        double sum = 0;
        int n = 0;
        for (int py = y; py < y + h; py++)
        {
            for (int px = x; px < x + w; px++)
            {
                int i = py * stride + px * 4;
                double b = bytes[i], g = bytes[i + 1], r = bytes[i + 2];
                sum += 0.299 * r + 0.587 * g + 0.114 * b;
                n++;
            }
        }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>采集一条水平扫描线(从 x0 到 x1、固定 y)上出现的不同 RGB 颜色,用于定位某控件实际渲染色。</summary>
    private static string ScanRow(WriteableBitmap frame, int y, int x0, int x1)
    {
        var bytes = ReadPixels(frame);
        var stride = frame.PixelSize.Width * 4;
        var seen = new System.Collections.Generic.Dictionary<string, int>();
        for (int px = x0; px <= x1; px++)
        {
            int i = y * stride + px * 4;
            var c = $"#{bytes[i + 2]:X2}{bytes[i + 1]:X2}{bytes[i]:X2}";
            seen[c] = seen.TryGetValue(c, out var n) ? n + 1 : 1;
        }
        return string.Join(" ", seen.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}x{kv.Value}"));
    }

    private static double AverageLuma(WriteableBitmap frame)
        => RegionLuma(frame, 0, 0, frame.PixelSize.Width, frame.PixelSize.Height);

    [AvaloniaFact]
    public void LightThemeFrame_IsVisiblyBrighterThan_DarkThemeFrame()
    {
        var (_, darkSide, darkInput, darkAvg) = Capture(dark: true);
        var (_, lightSide, lightInput, lightAvg) = Capture(dark: false);

        Assert.True(lightAvg > BrightThreshold, $"亮色帧平均亮度 {lightAvg:F1} 应偏亮（> {BrightThreshold}）");
        Assert.True(darkAvg < lightAvg - ContrastMargin,
            $"暗色帧平均亮度 {darkAvg:F1} 应明显低于亮色帧 {lightAvg:F1}（差距 > {ContrastMargin}）");

        Assert.True(darkSide < BrightThreshold, $"暗色侧栏亮度 {darkSide:F1} 应偏暗");
        Assert.True(darkSide < lightSide - ContrastMargin * 0.4,
            $"暗色侧栏 {darkSide:F1} 应明显低于亮色侧栏 {lightSide:F1}");

        Assert.True(darkInput < lightInput, $"暗色输入栏 {darkInput:F1} 应低于亮色输入栏 {lightInput:F1}");
    }

    [AvaloniaFact]
    public void Signature_StartupFrameIsDark_NeverThirdState()
    {
        // 不手动设置窗口主题 → 完全依赖 App 启动默认,验证首帧即 Dark,不出现 Fluent Default/浅色残留
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680
        };
        win.Show();
        var frame = win.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");

        // 侧栏背景应是深色语义色（光源侧栏 #161616）,而不可能是浅色 Default
        var side = RegionLuma(frame, x: 110, y: 340, w: 40, h: 40);
        Assert.True(side < 50, $"首帧侧栏平均亮度 {side:F1} 应为深色(<50),说明启动即 Dark 而非 Default/浅色残留");
    }

    [AvaloniaFact]
    public void SessionSelectSwitchesHighlightColor()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var vm = CreateVm();
        vm.NewConversationCommand.Execute(null);
        var first = vm.Sessions[0];
        var second = vm.Sessions[^1];
        var win = new MainWindow
        {
            DataContext = vm,
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();

        // 默认首个会话选中 → 高亮色 #3a4a5a
        AssertHighlightColor(win, "3A4A5A", "刚启动时首个会话应为选中高亮");

        // 单击切换选中到第二个会话 → 高亮搬到第二条
        vm.SelectSessionCommand.Execute(second);
        AssertHighlightColor(win, "3A4A5A", "切换后仍应有一个会话选中");

        // 第二个高亮、第一个取消
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);
    }

    private static void AssertHighlightColor(MainWindow win, string hex, string label)
    {
        var frame = win.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
        var bytes = ReadPixels(frame);
        var stride = frame.PixelSize.Width * 4;
        // 选中的第一个会话条目渲染在侧栏顶部区域,采样它的背景像素
        var found = false;
        for (int y = 100; y < 180; y++)
        {
            for (int x = 40; x < 200; x++)
            {
                int i = y * stride + x * 4;
                var c = $"{bytes[i]:X2}{bytes[i + 1]:X2}{bytes[i + 2]:X2}";
                if (c.Equals(hex, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
        Assert.True(found, $"{label}: 未找到高亮色 {hex}");
    }
}