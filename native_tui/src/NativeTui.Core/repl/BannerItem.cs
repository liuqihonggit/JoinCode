namespace NativeTui.Core.Repl;

/// <summary>
/// 启动 banner — 像素级对齐基准 CondensedLogo + Clawd（default pose）：
/// logo 三行 9 列 Box（row0 " ▐▛███▜▌" / row1 "▝▜█████▛▘" / row2 "  ▘▘ ▝▝  "，
/// clawd_body 前景；row0/row1 中段 5 列叠加 clawd_background 背景）
/// + gap 2 + 信息列三行（bold "Claude Code" + 默认空格 + dim "v{version}" / dim 模型·计费 / dim cwd）。
/// 消息列表首项（无 marginTop，贴顶）；高度恒 3（窄屏截断不折行，对齐基准 truncate 语义）。
/// </summary>
public sealed class BannerItem(string version, string model, string billing, string cwd) : IScrollItem
{
    /// <summary>logo 占位宽度（Clawd Box 最宽行 9 列）。</summary>
    private const int LogoWidth = 9;

    /// <summary>logo 与信息列间距（基准 Box row gap=2）。</summary>
    private const int Gap = 2;

    /// <summary>内容高度 = 3 行（与宽度无关）。</summary>
    public int MeasureHeight(int width) => 3;

    /// <summary>渲染三行：Clawd 像素画段 + gap + 信息列。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.ClawdBody });
        var bodyOnBg = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.ClawdBody, Background = theme.ClawdBackground });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });

        var row = frame.Y;
        var right = frame.X + LogoWidth + Gap;
        var infoRight = frame.Right;

        // Clawd default pose：边段仅前景，中段前景 + clawd_background 背景
        screen.WriteText(row, frame.X, " ▐", body, right);
        screen.WriteText(row, frame.X + 2, "▛███▜", bodyOnBg, right);
        screen.WriteText(row, frame.X + 7, "▌", body, right);
        screen.WriteText(row + 1, frame.X, "▝▜", body, right);
        screen.WriteText(row + 1, frame.X + 2, "█████", bodyOnBg, right);
        screen.WriteText(row + 1, frame.X + 7, "▛▘", body, right);
        screen.WriteText(row + 2, frame.X, "  ▘▘ ▝▝  ", body, right);

        // 信息列：bold 名 + 默认空格 + dim 版本 / dim 模型·计费 / dim cwd
        var col = screen.WriteText(row, right, "Claude Code", bold, infoRight);
        col = screen.WriteText(row, col, " ", 0, infoRight);
        screen.WriteText(row, col, $"v{version}", dim, infoRight);
        screen.WriteText(row + 1, right, $"{model} · {billing}", dim, infoRight);
        screen.WriteText(row + 2, right, cwd, dim, infoRight);
    }
}
