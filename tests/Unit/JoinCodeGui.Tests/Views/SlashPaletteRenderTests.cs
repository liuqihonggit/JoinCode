#pragma warning disable JCC9001, JCC3010 // 豁免理由：① 帧图导出属诊断产物（对齐 dumps/ 约定）非被测行为；② 渲染动画由真实时钟合成器驱动，FakeTimeProvider 无法推进，需等待动画完成后再截帧

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JoinCode.Gui.Persistence;
using JoinCode.Gui.Theming;
using JoinCode.Gui.ViewModels;
using JoinCode.Gui.Views;

namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// Slash 补全面板截图测试 —— 触发斜杠补全后捕获主窗口渲染帧：
/// ① 面板必须渲染在主窗口帧内（旧 Popup 独立弹层截不到 → 红）；② 面板出现在窗口下部、紧贴输入栏上方（从底部弹出）；
/// ③ 暗/亮主题帧图保存到 dumps/gui-slash/ 供人工核对。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class SlashPaletteRenderTests
{
    /// <summary>创建注入 InMemoryFileSystem 会话存储的 ViewModel — 传入就绪占位会话避免后台引擎加载（IsBusy 抑制补全）</summary>
    private static MainViewModel CreateVm() => new(
        new JoinCode.Gui.Hosting.PlaceholderChatSession(),
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));

    /// <summary>定位仓库根目录（向上找 Gui.slnx），dumps 输出到 {root}/dumps/gui-slash/</summary>
    private static string DumpDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gui.slnx")))
            dir = dir.Parent;
        var root = dir?.FullName ?? AppContext.BaseDirectory;
        var dump = Path.Combine(root, "dumps", "gui-slash");
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

    /// <summary>比较两帧指定区域的像素差异（任一通道差 &gt; 8 视为有差异）</summary>
    private static bool RegionDiffers(byte[] a, byte[] b, int width, int y0, int y1, int x0, int x1)
    {
        var stride = width * 4;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = y * stride + x * 4;
                if (Math.Abs(a[i] - b[i]) > 8 || Math.Abs(a[i + 1] - b[i + 1]) > 8 || Math.Abs(a[i + 2] - b[i + 2]) > 8)
                    return true;
            }
        }
        return false;
    }

    /// <summary>找出两帧全部差异像素的最低（最大 y）行 — 用于断言面板锚定窗口下部</summary>
    private static int LowestDiffRow(byte[] a, byte[] b, int width, int height)
    {
        var stride = width * 4;
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                if (Math.Abs(a[i] - b[i]) > 8 || Math.Abs(a[i + 1] - b[i + 1]) > 8 || Math.Abs(a[i + 2] - b[i + 2]) > 8)
                    return y;
            }
        }
        return -1;
    }

    private static void SavePng(WriteableBitmap frame, string path)
        => frame.Save(path); // Avalonia 11.3：按扩展名选择编码器，.png → PNG

    /// <summary>打开窗口并捕获"补全面板关闭/打开"两帧</summary>
    private static async Task<(WriteableBitmap ClosedFrame, WriteableBitmap OpenFrame)> CapturePairAsync(bool dark)
    {
        GuiPalette.CurrentVariant = dark
            ? GuiPalette.GuiThemeVariant.Dark
            : GuiPalette.GuiThemeVariant.Light;
        var win = new MainWindow
        {
            DataContext = CreateVm(),
            Width = 980,
            Height = 680,
            RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light
        };
        win.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var closed = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");

            // 触发斜杠补全：走真实管线 — TextBox 输入 "/"（双向绑定回写 VM，30ms 防抖后按光标位置刷新）
            var tb = win.GetVisualDescendants()
                .OfType<TextBox>()
                .First(x => x.Name == "InputTextBox");
            tb.Text = "/";
            tb.CaretIndex = 1;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(300); // 覆盖 30ms 防抖 + 140ms 升起动画
            Dispatcher.UIThread.RunJobs();

            var open = win.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("CaptureRenderedFrame 返回 null");
            return (closed, open);
        }
        finally
        {
            win.Close();
        }
    }

    [AvaloniaFact]
    public async Task SlashPalette_RendersInWindowFrame_AnchoredAboveInputBar()
    {
        var dump = DumpDir();
        var (closedFrame, openFrame) = await CapturePairAsync(dark: true);
        SavePng(closedFrame, Path.Combine(dump, "slash-closed-dark.png"));
        SavePng(openFrame, Path.Combine(dump, "slash-open-dark.png"));

        var closedBytes = ReadPixels(closedFrame);
        var openBytes = ReadPixels(openFrame);
        int w = openFrame.PixelSize.Width, h = openFrame.PixelSize.Height;

        // 探针A：面板中部（窗口高度 ~65% 处）— 旧 Popup 截不到此区域 → 断言失败即红
        Assert.True(RegionDiffers(closedBytes, openBytes, w, (int)(h * 0.60), (int)(h * 0.68), 430, 590),
            "面板中部区域与关闭态无像素差异：补全面板未渲染在主窗口帧内（Popup 独立弹层？）");

        // 底部锚定：全部差异的最低行必须位于窗口下部 12% 高度带内（紧贴输入栏上沿，而非悬浮中部）
        var lowest = LowestDiffRow(closedBytes, openBytes, w, h);
        Assert.True(lowest >= h * 0.88,
            $"差异最低行 y={lowest} 未落在窗口下部（应 ≥ {h * 0.88:F0}）：面板未从底部弹出");
    }

    [AvaloniaFact]
    public async Task SlashPalette_LightTheme_SavesFrameForReview()
    {
        var dump = DumpDir();
        var (_, openFrame) = await CapturePairAsync(dark: false);
        SavePng(openFrame, Path.Combine(dump, "slash-open-light.png"));
        Assert.True(File.Exists(Path.Combine(dump, "slash-open-light.png")), "亮色主题帧图应已保存到 dumps/gui-slash/");
    }
}
