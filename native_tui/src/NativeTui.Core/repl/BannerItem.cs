namespace NativeTui.Core.Repl;

/// <summary>
/// 启动 banner — 像素级对齐基准 CondensedLogo + Clawd（default pose）：
/// logo 三行 9 列 Box（row0 " ▐▛███▜▌" / row1 "▝▜█████▛▘" / row2 "  ▘▘ ▝▝  "，
/// clawd_body 前景；row0/row1 中段 5 列叠加 clawd_background 背景）
/// + gap 2 + 信息列三行（bold "jcc" + 默认空格 + dim "v{version}" / dim 模型·计费 / dim cwd）。
/// 信息字段可写 — bridge 模式收到引擎 session_info 事件后原地更新（模型未设置为"当前模型未设置"）。
/// 消息列表首项（无 marginTop，贴顶）；高度恒 3（窄屏截断不折行，对齐基准 truncate 语义）。
/// </summary>
public sealed class BannerItem(string version, string model, string billing, string cwd) : IScrollItem
{
    /// <summary>banner 标题（产品 CLI 名）。</summary>
    private const string Title = "jcc";

    /// <summary>logo 占位宽度（Clawd Box 最宽行 9 列）。</summary>
    private const int LogoWidth = 9;

    /// <summary>logo 与信息列间距（基准 Box row gap=2）。</summary>
    private const int Gap = 2;

    /// <summary>引擎版本（bridge 模式由 session_info 更新，初始为 TUI 程序集版本占位）。</summary>
    public string Version { get; set; } = version;

    /// <summary>模型名（引擎未设置时为"当前模型未设置"）。</summary>
    public string Model { get; set; } = model;

    /// <summary>供应商（引擎上报时 banner 第二行显示 "{vendor}/{model}"；空退化无前缀）。</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>计费方式（无真实登录态可查，模拟配置）。</summary>
    public string Billing { get; set; } = billing;

    /// <summary>引擎工作目录（bridge 模式由 session_info 更新）。</summary>
    public string Cwd { get; set; } = cwd;

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

        // 信息列：bold 名 + 默认空格 + dim 版本 / dim 供应商/模型·计费 / dim cwd
        var col = screen.WriteText(row, right, Title, bold, infoRight);
        col = screen.WriteText(row, col, " ", 0, infoRight);
        screen.WriteText(row, col, $"v{Version}", dim, infoRight);
        var vendorPrefix = Vendor.Length > 0 ? $"{Vendor}/" : string.Empty;
        screen.WriteText(row + 1, right, $"{vendorPrefix}{Model} · {Billing}", dim, infoRight);
        screen.WriteText(row + 2, right, Cwd, dim, infoRight);
    }
}
