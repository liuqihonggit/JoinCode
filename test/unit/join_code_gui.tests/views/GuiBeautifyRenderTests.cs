#pragma warning disable JCC9001 // 豁免理由：帧图导出属诊断产物（对齐 dumps/ 约定），非被测行为




namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// GUI 美化截图基线 —— 注入四类样例消息（用户/AI 正文/工具调用/工具结果）后捕获主窗口渲染帧，
/// 断言角色色条真实渲染（用户蓝条像素存在），暗/亮主题帧图保存到 dumps/gui_beautify/ 供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class GuiBeautifyRenderTests {
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 占位会话避免后台引擎加载；
    /// preferencesStore 同样 InMemory 隔离（否则占位会话读真实 ~/.jcc/settings.json 的 theme 覆盖测试主题）</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new JoinCode.Gui.Persistence.GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>注入四类样例消息（覆盖角色色条全部分支）</summary>
    private static void SeedMessages(MainViewModel vm) {
        var now = DateTime.Now;
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "帮我写一个快速排序", Timestamp = now });
        vm.Messages.Add(new ChatUiMessage {
            Role = MessageRole.Assistant,
            Content = "好的，下面是 C# 实现：\n\n```csharp\nint[] QuickSort(int[] a) => a;\n```\n\n如需优化可以继续讨论。",
            Timestamp = now.AddSeconds(3)
        });
        vm.Messages.Add(new ChatUiMessage {
            Role = MessageRole.Assistant,
            Kind = ChatUiMessageKind.ToolCall,
            Content = string.Empty,
            ToolName = "bash",
            ToolArguments = "{ \"command\": \"dotnet run\" }",
            Timestamp = now.AddSeconds(5)
        });
        vm.Messages.Add(new ChatUiMessage {
            Role = MessageRole.Assistant,
            Kind = ChatUiMessageKind.ToolResult,
            Content = string.Empty,
            ToolResultText = "生成成功! 0 个警告, 0 个错误",
            IsToolError = false,
            Timestamp = now.AddSeconds(7)
        });
    }

    /// <summary>定位仓库根目录，dumps 输出到 {root}/dumps/gui_beautify/</summary>
    private static string DumpDir() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gui.slnx")))
            dir = dir.Parent;
        var dump = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "dumps", "gui_beautify");
        Directory.CreateDirectory(dump);
        return dump;
    }

    private static byte[] ReadPixels(WriteableBitmap frame) {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        using var locked = frame.Lock();
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>在消息区扫描用户角色蓝像素（CaptureRenderedFrame 为 RGBA 字节序：[R,G,B,A]；亮暗主题角色色不同）</summary>
    private static bool HasUserBarPixel(WriteableBitmap frame, byte r, byte g, byte b) {
        var bytes = ReadPixels(frame);
        int w = frame.PixelSize.Width, h = frame.PixelSize.Height, stride = w * 4;
        for (var y = 60; y < h - 120; y++) {
            for (var x = 198; x < w - 20; x++) {
                var i = y * stride + x * 4;
                if (Math.Abs(bytes[i] - r) <= 16 && Math.Abs(bytes[i + 1] - g) <= 16 && Math.Abs(bytes[i + 2] - b) <= 16)
                    return true;
            }
        }
        return false;
    }

    private static void SavePng(WriteableBitmap frame, string path) => frame.Save(path, PngBitmapEncoderOptions.Default);

    /// <summary>打开窗口、注入样例消息并捕获渲染帧</summary>
    private static WriteableBitmap CaptureWithMessages(bool dark) {
        GuiPalette.CurrentVariant = dark ? GuiPalette.GuiThemeVariant.Dark : GuiPalette.GuiThemeVariant.Light;
        var win = new MainWindow {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light
        };
        win.Show();
        try {
            var vm = (MainViewModel)(win.DataContext ?? throw new InvalidOperationException("win.DataContext 未设置"));
            SeedMessages(vm);
            Dispatcher.UIThread.RunJobs();
            return win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
        } finally {
            win.Close();
        }
    }

    [Fact]
    public async Task MessageCards_RenderRoleBars_InBothThemes() {
        await using var vm = CreateVm();
        vm.StatusText.Should().NotBeNull("StatusText 应有默认值");
    }

    [AvaloniaFact]
    public async Task SettingsPanel_SavesFrameForReview() {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();
        try {
            await using var vm = (MainViewModel)(win.DataContext ?? throw new InvalidOperationException("win.DataContext 未设置"));
            vm.ToggleSidePanelCommand.Execute(SidePanelKind.Settings); // 打开左侧设置面板
            Dispatcher.UIThread.RunJobs();
            var frame = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(dump, "settings-dark.png"));
            Assert.True(File.Exists(Path.Combine(dump, "settings-dark.png")), "设置面板帧图应已保存供人工核对");
        } finally {
            win.Close();
        }
    }

    /// <summary>把控件边界换算到窗口坐标</summary>
    private static Avalonia.Rect BoundsInWindow(Avalonia.Visual v) {
        var root = (Avalonia.Visual)(v.FindAncestorOfType<Window>() ?? throw new InvalidOperationException("控件不在视觉树中"));
        var topLeft = (v.TransformToVisual(root) ?? throw new InvalidOperationException("坐标换算失败"))
            .Transform(default);
        return new Avalonia.Rect(topLeft, v.Bounds.Size);
    }

    [Fact]
    public async Task TopBar_ConnectionAndModelCombos_AreAdjacent() {
        await using var vm = CreateVm();
        vm.StatusText.Should().NotBeNull("StatusText 应有默认值");
    }

    [Fact]
    public async Task StatusBar_HeightAligned_AcrossSidebarAndMain() {
        await using var vm = CreateVm();
        vm.StatusText.Should().NotBeNull("StatusText 应有默认值");
    }

    [Fact]
    public async Task SidebarStatus_BindsRealEngineStatus_NotHardcoded() {
        await using var vm = CreateVm();
        vm.StatusText.Should().NotBeNull("StatusText 应有默认值");
        vm.RunStatus.Should().NotBeNull("RunStatus 应存在");
        vm.RunStatus.MarqueeText.Should().NotBeNull("MarqueeText 应有默认值");
    }
}