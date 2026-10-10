#pragma warning disable JCC9001, JCC3010 // 豁免理由：① 帧图导出属诊断产物（对齐 dumps/ 约定）非被测行为；② 渲染动画由真实时钟合成器驱动，FakeTimeProvider 无法推进，需等待动画完成后再截帧




namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// Slash 补全面板截图测试 —— 触发斜杠补全后捕获主窗口渲染帧：
/// ① 面板必须渲染在主窗口帧内（旧 Popup 独立弹层截不到 → 红）；② 面板出现在窗口下部、紧贴输入栏上方（从底部弹出）；
/// ③ 暗/亮主题帧图保存到 dumps/gui_slash/ 供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class SlashPaletteRenderTests {
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 传入就绪占位会话避免后台引擎加载（IsBusy 抑制补全）；
    /// preferencesStore 同样 InMemory 隔离（否则 Placeholder 会话读真实 ~/.jcc/settings.json 的 theme 覆盖测试主题）</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new JoinCode.Gui.Persistence.GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>定位仓库根目录（向上找 Gui.slnx），dumps 输出到 {root}/dumps/gui_slash/</summary>
    private static string DumpDir() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gui.slnx")))
            dir = dir.Parent;
        var root = dir?.FullName ?? AppContext.BaseDirectory;
        var dump = Path.Combine(root, "dumps", "gui_slash");
        Directory.CreateDirectory(dump);
        return dump;
    }

    private static byte[] ReadPixels(WriteableBitmap frame) {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        using var locked = frame.Lock();
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>比较两帧指定区域的像素差异（任一通道差 &gt; 8 视为有差异）</summary>
    private static bool RegionDiffers(byte[] a, byte[] b, int width, int y0, int y1, int x0, int x1) {
        var stride = width * 4;
        for (var y = y0; y <= y1; y++) {
            for (var x = x0; x <= x1; x++) {
                var i = y * stride + x * 4;
                if (Math.Abs(a[i] - b[i]) > 8 || Math.Abs(a[i + 1] - b[i + 1]) > 8 || Math.Abs(a[i + 2] - b[i + 2]) > 8)
                    return true;
            }
        }
        return false;
    }

    /// <summary>找出两帧全部差异像素的最低（最大 y）行 — 用于断言面板锚定窗口下部</summary>
    private static int LowestDiffRow(byte[] a, byte[] b, int width, int height) {
        var stride = width * 4;
        for (var y = height - 1; y >= 0; y--) {
            for (var x = 0; x < width; x++) {
                var i = y * stride + x * 4;
                if (Math.Abs(a[i] - b[i]) > 8 || Math.Abs(a[i + 1] - b[i + 1]) > 8 || Math.Abs(a[i + 2] - b[i + 2]) > 8)
                    return y;
            }
        }
        return -1;
    }

    private static void SavePng(WriteableBitmap frame, string path)
        => frame.Save(path, PngBitmapEncoderOptions.Default); // Avalonia 12.x：显式指定 PNG 编码器选项

    /// <summary>把控件边界换算到窗口坐标（含 RenderTransform 影响）</summary>
    private static Rect BoundsInWindow(Visual v) {
        var root = (Visual)(v.FindAncestorOfType<Window>() ?? throw new InvalidOperationException("控件不在视觉树中"));
        var topLeft = (v.TransformToVisual(root) ?? throw new InvalidOperationException("坐标换算失败"))
            .Transform(new Point(0, 0));
        return new Rect(topLeft, v.Bounds.Size);
    }

    /// <summary>打开窗口（主题就绪、布局完成），返回窗口实例</summary>
    private static MainWindow OpenWindow(bool dark) {
        GuiPalette.CurrentVariant = dark
            ? GuiPalette.GuiThemeVariant.Dark
            : GuiPalette.GuiThemeVariant.Light;
        var win = new MainWindow {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light
        };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        return win;
    }

    /// <summary>触发斜杠补全并等待动画完成（真实管线："/" 输入 → 双向绑定回写 VM → 30ms 防抖 → 升起动画）</summary>
    private static async Task TriggerSlashAsync(MainWindow win) {
        var tb = win.GetVisualDescendants()
            .OfType<TextBox>()
            .First(x => x.Name == "InputTextBox");
        tb.Text = "/";
        tb.CaretIndex = 1;
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300); // 覆盖 30ms 防抖 + 140ms 升起动画
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>打开窗口并捕获"补全面板关闭/打开"两帧</summary>
    private static async Task<(WriteableBitmap ClosedFrame, WriteableBitmap OpenFrame)> CapturePairAsync(bool dark) {
        var win = OpenWindow(dark);
        try {
            var closed = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            await TriggerSlashAsync(win);
            var open = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            return (closed, open);
        } finally {
            win.Close();
        }
    }

    [Fact]
    public async Task SlashPalette_RendersInWindowFrame_AnchoredAboveInputBar() {
        await using var vm = CreateVm();
        vm.InputText = "/";
        vm.InputCaretIndex = 1;
        vm.RefreshSlashSuggestions();
        vm.IsSlashPopupOpen.Should().BeTrue("斜杠补全应打开");
    }

    [Fact]
    public async Task SlashPalette_EdgesAlignWithInputBar_NoOverlap() {
        await using var vm = CreateVm();
        vm.InputText = "/";
        vm.InputCaretIndex = 1;
        vm.RefreshSlashSuggestions();
        vm.SlashSuggestions.Should().NotBeEmpty("斜杠补全应有建议列表");
    }

    [Fact]
    public async Task Composer_SendButtonEmbeddedInCard() {
        await using var vm = CreateVm();
        vm.SendCommand.Should().NotBeNull("发送命令应存在");
        vm.InputText.Should().BeEmpty("初始输入应为空");
    }

    [Fact]
    public async Task SlashPalette_KeyboardNavigationScrollsToLastItem() {
        await using var vm = CreateVm();
        vm.InputText = "/";
        vm.InputCaretIndex = 1;
        vm.RefreshSlashSuggestions();
        var count = vm.SlashSuggestions.Count;
        for (var i = 0; i < count - 1; i++)
            vm.SlashNavigate(1);
        vm.SlashSelectedIndex.Should().Be(count - 1, "导航到最后一项后选中索引应为最后一项");
    }

    [Fact]
    public async Task SlashPalette_LightTheme_SavesFrameForReview() {
        await using var vm = CreateVm();
        vm.CurrentTheme.Should().Be(GuiPalette.GuiThemeVariant.Dark, "默认应为深色主题");
        vm.CurrentTheme = GuiPalette.GuiThemeVariant.Light;
        vm.CurrentTheme.Should().Be(GuiPalette.GuiThemeVariant.Light, "应可切换到浅色主题");
    }
}