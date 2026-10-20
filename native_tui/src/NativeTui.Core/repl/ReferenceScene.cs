namespace NativeTui.Core.Repl;

/// <summary>
/// 固定基准采集场景 — L2 截图校准（tool/screenshot）的 C# 侧确定性数据源：
/// 80×24 屏 + banner（Clawd + fixture 信息列）+ 一条静态助手消息 + 默认 footer/输入框。
/// fixture 值硬编码（非进程快照）保证整帧字节稳定 — 可作 approved 快照与像素比对基线。
/// region 约定（与 compare_reference.py 硬编码对齐）：前三行×前 9 列 = clawd；
/// 以全宽 ─ 线开头的行 = prompt 输入框边框。
/// </summary>
public static class ReferenceScene
{
    /// <summary>采集屏宽（与 py region 提取的 80 列约定一致）。</summary>
    public const int Width = 80;

    /// <summary>采集屏高。</summary>
    public const int Height = 24;

    /// <summary>fixture 版本号（确定性）。</summary>
    private const string FixtureVersion = "1.0.0";

    /// <summary>fixture 模型名（确定性）。</summary>
    private const string FixtureModel = "fixture-model";

    /// <summary>fixture 计费方式（确定性）。</summary>
    private const string FixtureBilling = "Pro";

    /// <summary>fixture 工作目录（确定性）。</summary>
    private const string FixtureCwd = @"D:\fixture";

    /// <summary>fixture 输入文本（与 TS 参考端 reference.tsx 的输入框 fixture 对齐 — 像素比对两侧同文本）。</summary>
    private const string FixtureInput = "fixture input";

    /// <summary>
    /// 渲染整帧 ANSI（Screen.ToAnsiText — L1 golden 形态：SGR transition + 行尾裁剪/重置）。
    /// </summary>
    public static string ComposeAnsi()
    {
        using var screen = new Screen(Width, Height);
        var view = new ReplView();
        view.Messages.Add(new BannerItem(FixtureVersion, FixtureModel, FixtureBilling, FixtureCwd));
        view.Messages.Add(new AssistantMessageItem("reference scene fixture"));
        view.Input = FixtureInput;
        view.CursorCol = FixtureInput.Length;
        view.Render(screen, Theme.Dark);
        return screen.ToAnsiText();
    }
}
