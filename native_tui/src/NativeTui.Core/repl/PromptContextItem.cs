namespace NativeTui.Core.Repl;

/// <summary>
/// 注入上下文折叠项（TASK079 用户指令 — 首次注入的提示词/工具清单在 TUI 可见可查看）：
/// 塌缩单行摘要（📎 首次注入 · 系统提示词 N 字符 · 点击查看），鼠标左键点击 / /prompt 命令
/// 挂载 PromptDetailDialog 全页视图（ReplShell.TryDispatchHitTestClick 分派 — 命中表基建）。
/// 工具统计惰性求值（页面打开时注册表已装载 — shell 构造早于工具装载，急切得 0 用户实测）。
/// </summary>
public sealed class PromptContextItem : IScrollItem
{
    /// <summary>命中标识（ReplShell.TryDispatchHitTestClick 分派依据 — 打开详情页）。</summary>
    public const string HitIdValue = "prompt-context";

    /// <summary>系统提示词字符数（摘要行展示）。</summary>
    public int PromptChars { get; }

    /// <summary>摘要行文本（静态结构 — PromptDetailDialog 持真实数据）。</summary>
    public string Summary { get; }

    /// <summary>真实 prompt tokens（TokenUsage 回填 — null = 首轮未完成）。</summary>
    public int? PromptTokens { get; set; }

    public PromptContextItem(string summary, int promptChars)
    {
        Summary = summary;
        PromptChars = promptChars;
    }

    /// <summary>命中标识 — 非空即参与鼠标单击命中（左键打开详情页）。</summary>
    public string? HitTestId => HitIdValue;

    /// <summary>塌缩单行摘要。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染摘要行（dim — tokenNote 回填后追加）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        screen.WriteText(frame.Y, frame.X, $"{Summary}{tokenNote}", dim, frame.Right);
    }
}
