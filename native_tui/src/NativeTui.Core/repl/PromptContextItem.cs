namespace NativeTui.Core.Repl;

/// <summary>
/// 注入提示词折叠项（TASK145 — 每轮一条：本轮真实注入的系统提示词在 TUI 可见可回看）：
/// 塌缩单行摘要（📎 第 N 轮注入 · 提示词 N 字符 · 约 N tokens · 点击展开）。
/// 应用内命中模式 — 鼠标左键点击经 ReplShell.TryDispatchHitTestClick 分派 →
/// 打开该轮注入详情（transcript 备用屏）；hover 高亮经 Move 事件 + IsHovered（需鼠标追踪）。
/// 无鼠标追踪时降级 dim 纯文本（/prompt 键盘入口仍可用）。
/// 摘要行统计由 TokenUsage 回填本轮真实 prompt tokens。
/// </summary>
public sealed class PromptContextItem(int round, int promptChars, string content) : IScrollItem
{
    /// <summary>命中标识前缀（后接轮次 — 多条注入项按 hit id 区分点击目标）。</summary>
    public const string HitIdPrefix = "prompt-context:";

    /// <summary>轮次序号（会话级 — 1 起，按注入先后递增）。</summary>
    public int Round { get; } = round;

    /// <summary>本轮注入的系统提示词字符数。</summary>
    public int PromptChars { get; } = promptChars;

    /// <summary>本轮注入的系统提示词全文（预处理后从上下文管理器读取）。</summary>
    public string Content { get; } = content;

    /// <summary>真实 prompt tokens（本轮 TokenUsage 回填 — null = 本轮未完成）。</summary>
    public int? PromptTokens { get; set; }

    /// <summary>塌缩摘要行文本（TranscriptLines 投影共用）。</summary>
    public string Summary => $"📎 第 {Round} 轮注入 · 提示词 {PromptChars} 字符";

    /// <summary>展开态（transcript 模态内查看全文 — /prompt 或点击置位）。</summary>
    public bool Expanded { get; set; }

    /// <summary>鼠标悬停态（应用内命中模式 — Move 事件经 HitTest 查询置位，渲染时切换样式）。</summary>
    public bool IsHovered { get; set; }

    /// <summary>展开内容（本轮系统提示词全文）。</summary>
    public string? ExpandedContent => Content;

    /// <summary>命中标识 — 每轮唯一，参与鼠标单击命中（左键打开该轮注入详情）。</summary>
    public string HitTestId => $"{HitIdPrefix}{Round}";

    /// <summary>命中标识是否为注入项（前缀匹配 — 点击分派与 hover 共用）。</summary>
    public static bool IsPromptHit(string? hitId)
        => hitId is not null && hitId.StartsWith(HitIdPrefix, StringComparison.Ordinal);

    /// <summary>塌缩单行摘要（1 行）。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染摘要行。
    /// 鼠标追踪启用 → 应用内命中模式：hover 时下划线高亮，非 hover 时 dim — 点击直达注入详情。
    /// 鼠标追踪禁用 → dim 纯文本（仅 /prompt 或 ctrl+o 键盘入口）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        var text = $"{Summary}{tokenNote} · 点击展开";
        if (JccFeatureFlags.MouseTrackingEnabled && IsHovered)
        {
            var hoverStyle = screen.Styles.GetOrAdd(new TextStyle { Underline = true });
            screen.WriteText(frame.Y, frame.X, text, hoverStyle, frame.Right);
        }
        else
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(frame.Y, frame.X, text, dim, frame.Right);
        }
    }
}