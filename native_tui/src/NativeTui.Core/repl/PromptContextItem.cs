namespace NativeTui.Core.Repl;

/// <summary>
/// 注入上下文折叠项（TASK079 用户终版指令 — 首次注入的提示词在 TUI 可见可查看）：
/// 塌缩单行摘要（📎 首次注入 · 系统提示词 N 字符 · 点击展开）。
/// 双模式渲染：
/// ① OSC 8 超链接模式（FileUrl 非 null）— 终端原生 hover 下划线高亮 + 点击用默认编辑器打开 .md 快照；
///   不依赖鼠标追踪模式，终端原生选区不受影响。ctrl+o 键盘入口进应用内第二屏。
/// ② 应用内命中模式（FileUrl null）— 鼠标左键点击经 ReplShell.TryDispatchHitTestClick 分派 →
///   TogglePromptContext → transcript 备用屏（DEC 1049）；hover 高亮经 Move 事件 + IsHovered（需鼠标追踪）。
/// 摘要行统计由 TokenUsage 回填真实 prompt tokens。
/// </summary>
public sealed class PromptContextItem : IScrollItem
{
    /// <summary>命中标识常量（ReplShell.TryDispatchHitTestClick 分派依据 — 打开 transcript 备用屏）。</summary>
    public const string HitIdValue = "prompt-context";

    /// <summary>系统提示词字符数。</summary>
    public int PromptChars { get; }

    /// <summary>真实 prompt tokens（TokenUsage 回填 — null = 首轮未完成）。</summary>
    public int? PromptTokens { get; set; }

    /// <summary>塌缩摘要行文本（TranscriptLines 投影共用）。</summary>
    public string Summary => $"📎 首次注入 · 系统提示词 {PromptChars} 字符";

    /// <summary>展开态（transcript 模态内查看全文 — /prompt 置位）。</summary>
    public bool Expanded { get; set; }

    /// <summary>鼠标悬停态（应用内命中模式 — Move 事件经 HitTest 查询置位，渲染时切换样式；OSC 8 模式不用）。</summary>
    public bool IsHovered { get; set; }

    /// <summary>展开内容（系统提示词全文 + 工具清单 — ReplShell 注入；null = 未注入回退摘要）。</summary>
    public string? ExpandedContent { get; set; }

    /// <summary>OSC 8 超链接目标 URL（file:// 指向 .md 快照 — null = 应用内命中模式）。</summary>
    public string? FileUrl { get; }

    public PromptContextItem(string summaryPrefix, int promptChars, string? fileUrl = null)
    {
        PromptChars = promptChars;
        FileUrl = fileUrl;
    }

    /// <summary>命中标识 — 非空即参与鼠标单击命中（左键进 transcript 备用屏）。</summary>
    public string? HitTestId => HitIdValue;

    /// <summary>塌缩单行摘要（1 行）。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染摘要行。
    /// 鼠标追踪启用 → 应用内命中模式：hover 时下划线高亮，非 hover 时 dim — 点击直达应用命中表进第二屏。
    /// 鼠标追踪禁用 + FileUrl 非 null → OSC 8 模式：dim + SetLinks — 终端原生 hover 下划线 + 点击打开 .md。
    /// 鼠标追踪禁用 + FileUrl null → dim 纯文本（仅 ctrl+o 键盘入口）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        var text = $"📎 首次注入 · 系统提示词 {PromptChars} 字符{tokenNote} · 点击展开";
        if (JccFeatureFlags.MouseTrackingEnabled && IsHovered)
        {
            var hoverStyle = screen.Styles.GetOrAdd(new TextStyle { Underline = true });
            screen.WriteText(frame.Y, frame.X, text, hoverStyle, frame.Right);
        }
        else if (!JccFeatureFlags.MouseTrackingEnabled && FileUrl is { Length: > 0 } url)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(frame.Y, frame.X, text, dim, frame.Right);
            screen.SetLinks(frame.Y, [new LinkSpan(frame.X, frame.X + StringWidth.Of(text), url)]);
        }
        else
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(frame.Y, frame.X, text, dim, frame.Right);
        }
    }
}
