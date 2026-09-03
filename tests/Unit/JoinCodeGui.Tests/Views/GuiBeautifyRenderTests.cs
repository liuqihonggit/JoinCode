#pragma warning disable JCC9001 // 豁免理由：帧图导出属诊断产物（对齐 dumps/ 约定），非被测行为




namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// GUI 美化截图基线 —— 注入四类样例消息（用户/AI 正文/工具调用/工具结果）后捕获主窗口渲染帧，
/// 断言角色色条真实渲染（用户蓝条像素存在），暗/亮主题帧图保存到 dumps/gui-beautify/ 供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class GuiBeautifyRenderTests
{
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 占位会话避免后台引擎加载；
    /// preferencesStore 同样 InMemory 隔离（否则占位会话读真实 ~/.jcc/settings.json 的 theme 覆盖测试主题）</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new JoinCode.Gui.Persistence.GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>注入四类样例消息（覆盖角色色条全部分支）</summary>
    private static void SeedMessages(MainViewModel vm)
    {
        var now = DateTime.Now;
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.User, Content = "帮我写一个快速排序", Timestamp = now });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = "好的，下面是 C# 实现：\n\n```csharp\nint[] QuickSort(int[] a) => a;\n```\n\n如需优化可以继续讨论。",
            Timestamp = now.AddSeconds(3)
        });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant, Kind = ChatUiMessageKind.ToolCall,
            Content = string.Empty, ToolName = "Bash", ToolArguments = "{ \"command\": \"dotnet run\" }",
            Timestamp = now.AddSeconds(5)
        });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant, Kind = ChatUiMessageKind.ToolResult,
            Content = string.Empty, ToolResultText = "生成成功! 0 个警告, 0 个错误", IsToolError = false,
            Timestamp = now.AddSeconds(7)
        });
    }

    /// <summary>定位仓库根目录，dumps 输出到 {root}/dumps/gui-beautify/</summary>
    private static string DumpDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gui.slnx")))
            dir = dir.Parent;
        var dump = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "dumps", "gui-beautify");
        Directory.CreateDirectory(dump);
        return dump;
    }

    private static byte[] ReadPixels(WriteableBitmap frame)
    {
        var bytes = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        using var locked = await frame.TryLockAsync().ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>在消息区扫描用户角色蓝像素（CaptureRenderedFrame 为 RGBA 字节序：[R,G,B,A]；亮暗主题角色色不同）</summary>
    private static bool HasUserBarPixel(WriteableBitmap frame, byte r, byte g, byte b)
    {
        var bytes = ReadPixels(frame);
        int w = frame.PixelSize.Width, h = frame.PixelSize.Height, stride = w * 4;
        for (int y = 60; y < h - 120; y++)
        {
            for (int x = 198; x < w - 20; x++)
            {
                int i = y * stride + x * 4;
                if (Math.Abs(bytes[i] - r) <= 16 && Math.Abs(bytes[i + 1] - g) <= 16 && Math.Abs(bytes[i + 2] - b) <= 16)
                    return true;
            }
        }
        return false;
    }

    private static void SavePng(WriteableBitmap frame, string path) => frame.Save(path);

    /// <summary>打开窗口、注入样例消息并捕获渲染帧</summary>
    private static WriteableBitmap CaptureWithMessages(bool dark)
    {
        GuiPalette.CurrentVariant = dark ? GuiPalette.GuiThemeVariant.Dark : GuiPalette.GuiThemeVariant.Light;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light
        };
        win.Show();
        try
        {
            var vm = (MainViewModel)win.DataContext!;
            SeedMessages(vm);
            Dispatcher.UIThread.RunJobs();
            return win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void MessageCards_RenderRoleBars_InBothThemes()
    {
        var dump = DumpDir();
        var dark = CaptureWithMessages(dark: true);
        SavePng(dark, Path.Combine(dump, "messages-dark.png"));
        Assert.True(HasUserBarPixel(dark, 0x4D, 0xA6, 0xFF), "暗色主题帧应存在用户角色蓝 #4DA6FF 像素（角色色条/标签未渲染？）");

        var light = CaptureWithMessages(dark: false);
        SavePng(light, Path.Combine(dump, "messages-light.png"));
        Assert.True(HasUserBarPixel(light, 0x1A, 0x6B, 0xC0), "亮色主题帧应存在用户角色蓝 #1A6BC0 像素（角色色条/标签未渲染？）");
    }

    [AvaloniaFact]
    public void SettingsPanel_SavesFrameForReview()
    {
        var dump = DumpDir();
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();
        try
        {
            var vm = (MainViewModel)win.DataContext!;
            vm.ToggleSettingsPanelCommand.Execute(null); // 打开右侧设置抽屉
            Dispatcher.UIThread.RunJobs();
            var frame = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            SavePng(frame, Path.Combine(dump, "settings-dark.png"));
            Assert.True(File.Exists(Path.Combine(dump, "settings-dark.png")), "设置面板帧图应已保存供人工核对");
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>把控件边界换算到窗口坐标</summary>
    private static Avalonia.Rect BoundsInWindow(Avalonia.Visual v)
    {
        var root = (Avalonia.Visual)(v.GetVisualRoot() ?? throw new InvalidOperationException("控件不在视觉树中"));
        var topLeft = (v.TransformToVisual(root) ?? throw new InvalidOperationException("坐标换算失败"))
            .Transform(default);
        return new Avalonia.Rect(topLeft, v.Bounds.Size);
    }

    [AvaloniaFact]
    public void TopBar_ConnectionAndModelCombos_AreAdjacent()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            // 顶栏可见下拉：连接 + 模型（引擎就绪态均可见）
            var combos = win.GetVisualDescendants().OfType<ComboBox>()
                .Where(c => c.IsVisible && c.Bounds.Width > 0)
                .Select(c => (Combo: c, Rect: BoundsInWindow(c)))
                .OrderBy(pair => pair.Rect.Left)
                .ToList();
            Assert.True(combos.Count >= 2, $"顶栏应有两个可见下拉，实际 {combos.Count}");

            // 最右侧两个下拉 = 连接 + 模型：必须紧靠（间距 ≤ ColumnSpacing 8 + 亚像素容差）
            var gap = combos[^1].Rect.Left - combos[^2].Rect.Right;
            Assert.True(gap <= 8.75,
                $"连接与模型下拉间距 {gap:F1}px 未紧靠（中间被 * 弹性列拉开？）");
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void StatusBar_HeightAligned_AcrossSidebarAndMain()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            var sidebarBar = win.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "SidebarStatusBar");
            var mainBar = win.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "MainStatusBar");

            var sidebarRect = BoundsInWindow(sidebarBar);
            var mainRect = BoundsInWindow(mainBar);

            // 两条状态栏必须等高（横向分隔线连续，否则侧栏/主区交界出现台阶错位）
            var diff = Math.Abs(sidebarRect.Height - mainRect.Height);
            Assert.True(diff <= 0.75,
                $"侧栏状态栏高 {sidebarRect.Height:F1} 与主状态栏高 {mainRect.Height:F1} 不等（差 {diff:F1}px），横向分隔线错位");

            // 等高还不够 — 顶边必须同一水平线（底边贴窗口底由 Grid Stretch 保证）
            var topDiff = Math.Abs(sidebarRect.Top - mainRect.Top);
            Assert.True(topDiff <= 0.75,
                $"侧栏状态栏顶 {sidebarRect.Top:F1} 与主状态栏顶 {mainRect.Top:F1} 不在同一水平线（差 {topDiff:F1}px）");

            // 文字基线对齐 — 走马灯内部 TextBlock 为侧栏状态文字源（走马灯替代绿点后）
            var sideMarquee = sidebarBar.GetVisualDescendants()
                .OfType<JoinCode.Gui.Views.Controls.MarqueeTextBlock>().First();
            var sideText = sideMarquee.GetVisualDescendants().OfType<TextBlock>().First();
            var mainText = mainBar.GetVisualDescendants().OfType<TextBlock>().First();
            var textDiff = Math.Abs(BoundsInWindow(sideText).Top - BoundsInWindow(mainText).Top);
            Assert.True(textDiff <= 0.75,
                $"侧栏状态文字顶 {BoundsInWindow(sideText).Top:F1} 与主状态栏文字顶 {BoundsInWindow(mainText).Top:F1} 错位 {textDiff:F1}px（内容未垂直居中？）");

            // 真实环境场景：模型徽章显示（SelectedModel 非空）— 徽章高 19px 会撑高主状态栏产生台阶
            var vm2 = (MainViewModel)win.DataContext!;
            vm2.SelectedModel = "sensenova-6.8-flash-lite";
            Dispatcher.UIThread.RunJobs();
            var sideRect2 = BoundsInWindow(win.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "SidebarStatusBar"));
            var mainRect2 = BoundsInWindow(mainBar);
            var diff2 = Math.Abs(sideRect2.Height - mainRect2.Height);
            Assert.True(diff2 <= 0.75,
                $"模型徽章显示后主状态栏高 {mainRect2.Height:F1} vs 侧栏 {sideRect2.Height:F1}（差 {diff2:F1}px）— 徽章撑高了状态栏");
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void SidebarStatus_BindsRealEngineStatus_NotHardcoded()
    {
        GuiPalette.CurrentVariant = GuiPalette.GuiThemeVariant.Dark;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
        };
        win.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var vm = (MainViewModel)win.DataContext!;

            // 硬编码占位文案必须消失（引擎加载后仍显示"本地引擎待接入"是错误信息）
            Assert.DoesNotContain(win.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "本地引擎待接入");

            // 侧栏底部走马灯文本必须与 VM.RunStatus.MarqueeText 同源（走马灯替代绿点后绑定源变更）
            var sidebarMarquee = win.GetVisualDescendants()
                .OfType<JoinCode.Gui.Views.Controls.MarqueeTextBlock>().FirstOrDefault();
            Assert.NotNull(sidebarMarquee);
            Assert.Equal(vm.RunStatus.MarqueeText, sidebarMarquee!.Text);
        }
        finally
        {
            win.Close();
        }
    }
}
