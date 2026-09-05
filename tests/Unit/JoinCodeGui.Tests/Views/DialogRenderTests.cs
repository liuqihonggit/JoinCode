#pragma warning disable JCC9001, JCC3010 // 豁免理由：① 帧图导出属诊断产物（对齐 dumps/ 约定）非被测行为；② 渲染动画由真实时钟合成器驱动，需等待布局完成后再截帧




namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 对话框与主题切换截图测试：
/// ① 主题切换按钮图标随主题切换（暗=☾ / 亮=☀）；
/// ② 三类对话框（确认/权限/提问）主题化渲染，暗色帧保存 dumps/gui-beautify/ 供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class DialogRenderTests
{
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel（preferencesStore 同样隔离，防真实 settings.json 主题覆盖）</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new JoinCode.Gui.Persistence.GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>定位仓库根目录，dumps 输出到 {root}/dumps/gui-beautify/</summary>
    private static string DumpDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gui.slnx")))
            dir = dir.Parent;
        var root = dir?.FullName ?? AppContext.BaseDirectory;
        var dump = Path.Combine(root, "dumps", "gui-beautify");
        Directory.CreateDirectory(dump);
        return dump;
    }

    private static byte[] ReadPixels(WriteableBitmap frame)
    {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        using var locked = frame.Lock();
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>取帧指定像素颜色（RGBA 字节序）</summary>
    private static (byte R, byte G, byte B) PixelAt(byte[] px, int width, int x, int y)
    {
        int i = y * width * 4 + x * 4;
        return (px[i], px[i + 1], px[i + 2]);
    }

    private static void SavePng(WriteableBitmap frame, string path) => frame.Save(path);

    [AvaloniaFact]
    public void ThemeToggle_IconSwitchesWithTheme()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow { DataContext = CreateVm(), Width = 980, Height = 680 };
        win.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var vm = (MainViewModel)win.DataContext!;
            Assert.True(vm.IsDarkTheme, "初始应为暗色主题");

            // 暗色：月亮可见、太阳隐藏
            var moon = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "☾");
            var sun = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "☀");
            Assert.True(moon.IsVisible, "暗色主题应显示月亮图标");
            Assert.False(sun.IsVisible, "暗色主题不应显示太阳图标");

            // 切换到亮色：太阳可见、月亮隐藏（缺陷回归：旧实现 Content 硬编码 ☾ 不随主题切换）
            vm.ToggleThemeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsDarkTheme, "切换后应为亮色主题");
            Assert.False(moon.IsVisible, "亮色主题不应显示月亮图标");
            Assert.True(sun.IsVisible, "亮色主题应显示太阳图标");

            // 帧图保存供人工核对字形（☾ 缺字形会被 fallback 渲染成 "C"）
            var frame = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(DumpDir(), "theme-icon-light.png"));
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThreeDialogs_LightTheme_SavesFramesForReview()
    {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Light;
        var light = Avalonia.Styling.ThemeVariant.Light;

        var confirm = new ConfirmDialogWindow("确认退出 JoinCode？未发送的输入将丢失。")
        {
            RequestedThemeVariant = light // 对话框不设则继承宿主默认（Dark），亮色帧必须显式指定
        };
        confirm.Show();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        SavePng(confirm.CaptureRenderedFrame() ?? throw new InvalidOperationException("capture null"),
            Path.Combine(dump, "confirm-light.png"));
        confirm.Close();

        var ask = new AskUserQuestionDialog(new QuestionItem
        {
            Header = "选择实现方案",
            Question = "补全面板动画采用哪种驱动方式？",
            Options =
            [
                new QuestionOption { Label = "Compositor 过渡", Description = "合成器驱动，桌面流畅" },
                new QuestionOption { Label = "Task.Delay 步进", Description = "headless 与桌面行为一致" }
            ]
        })
        {
            RequestedThemeVariant = light
        };
        ask.Show();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        SavePng(ask.CaptureRenderedFrame() ?? throw new InvalidOperationException("capture null"),
            Path.Combine(dump, "askuser-light.png"));
        ask.Close();

        var perm = new PermissionDialog(new JoinCode.Gui.Hosting.PermissionConfirmationRequest(
            "Bash", "允许在当前目录执行 shell 命令？", "req-1",
            "rule: bash.execute\nscope: workdir\nmode: confirm"))
        {
            RequestedThemeVariant = light
        };
        perm.Show();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        SavePng(perm.CaptureRenderedFrame() ?? throw new InvalidOperationException("capture null"),
            Path.Combine(dump, "permission-light.png"));
        perm.Close();

        Assert.True(File.Exists(Path.Combine(dump, "confirm-light.png")), "亮色确认对话框帧应已保存");
        Assert.True(File.Exists(Path.Combine(dump, "askuser-light.png")), "亮色提问对话框帧应已保存");
        Assert.True(File.Exists(Path.Combine(dump, "permission-light.png")), "亮色权限对话框帧应已保存");
    }

    [AvaloniaFact]
    public async Task ConfirmDialog_DarkTheme_SavesFrameAndUsesThemedBackground()
    {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var dlg = new ConfirmDialogWindow("确认退出 JoinCode？未发送的输入将丢失。");
        dlg.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            var frame = dlg.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(dump, "confirm-dark.png"));

            // 窗口背景必须是主题暗色 #1e1e1e（旧实现无 Background → 亮白窗口在暗色主题下突兀）
            var px = ReadPixels(frame);
            int w = frame.PixelSize.Width;
            var corner = PixelAt(px, w, 5, 5);
            Assert.True(corner.R < 0x30 && corner.G < 0x30 && corner.B < 0x30,
                $"对话框背景非暗色主题色：RGB=({corner.R},{corner.G},{corner.B})，疑似未主题化");
        }
        finally
        {
            dlg.Close();
        }
    }

    [AvaloniaFact]
    public async Task PermissionDialog_DarkTheme_SavesFrameForReview()
    {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var request = new JoinCode.Gui.Hosting.PermissionConfirmationRequest(
            "Bash", "允许在当前目录执行 shell 命令？", "req-1",
            "rule: bash.execute\nscope: workdir\nmode: confirm");
        var dlg = new PermissionDialog(request);
        dlg.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            var frame = dlg.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(dump, "permission-dark.png"));
            var px = ReadPixels(frame);
            int w = frame.PixelSize.Width;
            var corner = PixelAt(px, w, 5, 5);
            Assert.True(corner.R < 0x30 && corner.G < 0x30 && corner.B < 0x30,
                $"权限对话框背景非暗色主题色：RGB=({corner.R},{corner.G},{corner.B})");
        }
        finally
        {
            dlg.Close();
        }
    }

    [AvaloniaFact]
    public async Task AskUserQuestionDialog_DarkTheme_SavesFrameForReview()
    {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var question = new QuestionItem
        {
            Header = "选择实现方案",
            Question = "补全面板动画采用哪种驱动方式？",
            Options =
            [
                new QuestionOption { Label = "Compositor 过渡", Description = "合成器驱动，桌面流畅" },
                new QuestionOption { Label = "Task.Delay 步进", Description = "headless 与桌面行为一致" }
            ]
        };
        var dlg = new AskUserQuestionDialog(question);
        dlg.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            var frame = dlg.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(dump, "askuser-dark.png"));
            var px = ReadPixels(frame);
            int w = frame.PixelSize.Width;
            var corner = PixelAt(px, w, 5, 5);
            Assert.True(corner.R < 0x30 && corner.G < 0x30 && corner.B < 0x30,
                $"提问对话框背景非暗色主题色：RGB=({corner.R},{corner.G},{corner.B})");
        }
        finally
        {
            dlg.Close();
        }
    }
}
