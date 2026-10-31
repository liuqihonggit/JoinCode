namespace NativeTui.Core.Repl;

/// <summary>
/// 注入上下文折叠项（TASK079 用户指令 — 首次注入的提示词在 TUI 可见可查看，且不破坏原生选择复制）：
/// 塌缩单行摘要（📎 首次注入 · 系统提示词 N 字符 · 点击此处查看全文（.md）），
/// "点击此处查看全文" 段渲染为 **OSC 8 file 超链接**（Screen.SetLinks — 终端原生点击打开
/// 快照 .md 文件，无鼠标接管、原生选择复制不受影响 — Claude Code 同款机制）；
/// 终端不支持 OSC 8 时降级纯文本路径展示（Host 已把快照写入 %TEMP%）。
/// /prompt 命令保留（TUI 内全页模态双通道）。摘要行统计由 TokenUsage 回填真实 prompt tokens。
/// </summary>
public sealed class PromptContextItem : IScrollItem
{
    /// <summary>命中标识常量（ReplShell.TryDispatchHitTestClick 分派依据 — 打开提示词详情页）。</summary>
    public const string HitIdValue = "prompt-context";

    /// <summary>摘要前缀（链接段之前的统计文本）。</summary>
    private readonly string _summaryPrefix;

    /// <summary>链接段显示文本。</summary>
    private readonly string _linkText;

    /// <summary>系统提示词字符数。</summary>
    public int PromptChars { get; }

    /// <summary>塌缩摘要行文本（Render/TranscriptLines 投影共用 — 构造时由宿主组装）。</summary>
    public string Summary => _summaryPrefix;

    /// <summary>file URL（快照 .md 文件 — null = 未写文件）。</summary>
    public string? FileUrl { get; }

    /// <summary>终端 OSC 8 支持（false = 链接降级，摘要尾部显示文件路径）。</summary>
    public bool HyperlinkSupported { get; }

    /// <summary>降级路径文本（不支持 OSC 8 时渲染 — Host 写入的临时文件路径）。</summary>
    public string? FallbackPath { get; }

    /// <summary>真实 prompt tokens（TokenUsage 回填 — null = 首轮未完成）。</summary>
    public int? PromptTokens { get; set; }

    /// <summary>展开态（transcript 模态内查看全文 — /prompt 置位）。</summary>
    public bool Expanded { get; set; }

    /// <summary>展开内容（系统提示词全文 + 工具清单 — ReplShell 注入；null = 未注入回退摘要）。</summary>
    public string? ExpandedContent { get; set; }

    public PromptContextItem(string summaryPrefix, int promptChars, string? fileUrl, bool hyperlinkSupported, string? fallbackPath = null)
    {
        _summaryPrefix = summaryPrefix;
        PromptChars = promptChars;
        FileUrl = fileUrl;
        HyperlinkSupported = hyperlinkSupported;
        FallbackPath = fallbackPath;
        _linkText = "点击此处查看全文（.md）";
    }

    /// <summary>塌缩单行摘要（1 行）。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染摘要行：前缀统计 + 链接段（OSC 8 file 链接 / 降级路径）；
    /// 链接段经 Screen.SetLinks 声明（帧重画幂等 — 对齐 MarkdownMessageItem DSG043 P3 先例）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var tokenNote = PromptTokens is { } tokens ? $" · 约 {tokens} tokens" : string.Empty;
        var col = screen.WriteText(frame.Y, frame.X, $"{_summaryPrefix}{tokenNote} · ", dim, frame.Right);

        if (HyperlinkSupported && FileUrl is not null)
        {
            // OSC 8 链接段 — 文本正常写入 cell，链接区间经 SetLinks 声明（渲染层注入 OSC 8 序列）
            var linkStart = col;
            var linkCol = screen.WriteText(frame.Y, col, _linkText, dim, frame.Right);
            screen.SetLinks(frame.Y, [new LinkSpan(linkStart, linkCol, FileUrl)]);
            return;
        }

        if (FallbackPath is not null)
        {
            // 降级 — 终端不支持 OSC 8：显示文件路径（用户手动打开）
            screen.WriteText(frame.Y, col, $"已写入 {FallbackPath}，打开查看", dim, frame.Right);
            return;
        }

        screen.WriteText(frame.Y, col, _linkText, dim, frame.Right);
    }
}
