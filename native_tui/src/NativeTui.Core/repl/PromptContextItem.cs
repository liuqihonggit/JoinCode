namespace NativeTui.Core.Repl;

/// <summary>
/// 注入上下文折叠项（TASK079 用户终版指令 — 首次注入的提示词在 TUI 可见可查看）：
/// 塌缩单行摘要（📎 首次注入 · 系统提示词 N 字符 · 点击展开）。
/// 鼠标左键点击 → ReplShell.TryDispatchHitTestClick 分派 → TogglePromptContext
/// → 进入 transcript 备用屏（DEC 1049 第二屏）查看全文，esc 退出回主屏。
/// ⚠ 此项**不得**渲染 OSC 8 超链接 — 终端拦截超链接点击打开外部程序，
/// 鼠标事件到不了应用命中表（用户实测）。摘要行统计由 TokenUsage 回填真实 prompt tokens。
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

    /// <summary>展开内容（系统提示词全文 + 工具清单 — ReplShell 注入；null = 未注入回退摘要）。</summary>
    public string? ExpandedContent { get; set; }

    public PromptContextItem(string summaryPrefix, int promptChars)
    {
        PromptChars = promptChars;
    }

    /// <summary>命中标识 — 非空即参与鼠标单击命中（左键进 transcript 备用屏）。</summary>
    public string? HitTestId => HitIdValue;

    /// <summary>塌缩单行摘要（1 行）。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染摘要行（dim 纯文本 — 无 OSC 8 无 SetLinks，点击直达应用命中表）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        screen.WriteText(frame.Y, frame.X,
            $"📎 首次注入 · 系统提示词 {PromptChars} 字符{tokenNote} · 点击展开",
            dim, frame.Right);
    }
}
