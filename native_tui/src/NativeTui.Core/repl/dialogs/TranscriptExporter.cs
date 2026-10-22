namespace NativeTui.Core.Repl;

/// <summary>
/// 会话导出格式化器（Export 对话框域）— 对齐基准 commands/export/export.tsx 的 content 组装职责
/// （renderMessagesToPlainText 提取 + ExportDialog content 消费）：本项目以
/// <see cref="CopyTextExtractor.Extract"/> 为唯一提取面（用户消息剥 system-reminder / assistant 纯文本 /
/// Markdown 块模型拍平 / 工具调用主参数 / 工具结果与错误正文 / diff 与子代理分组拼接），
/// 本类只做 Markdown 标题段<b>组装层</b>（提取零重复实现 — 单一数据源委托消费）。
/// 段形态：用户段 "## 用户" / 助手段 "## 助手"（MarkdownMessageItem 归助手段 — 助手侧渲染产物的
/// 导出面对等）/ 工具调用段 "&gt; 工具 {名}" 引用行 + 主参数原文（主参数不可提取时仅保留引用行 —
/// 工具调用发生记录不丢失）/ 工具结果段逐行 "&gt; " 缩进引用块（ToolResultItem / DiffResultItem /
/// SubagentGroupItem）/ 错误段 "**错误**" 加粗标记（导出面纯文本 Markdown 无色彩语义 —
/// TUI 渲染的 Error 红以加粗标记对等标红）。
/// 段间空行分隔（string.Join("\n\n")，LINQ 链式组装）；空会话或全不可导出 → 空串。
/// 剪贴板/文件写入不在本类（宿主接线 — ExportDialog.SelectedTarget 分派，见未决问题）。
/// </summary>
public static class TranscriptExporter
{
    /// <summary>用户段标题（Markdown 二级标题）。</summary>
    private const string UserHeading = "## 用户";

    /// <summary>助手段标题（Markdown 二级标题）。</summary>
    private const string AssistantHeading = "## 助手";

    /// <summary>错误段标记（加粗 — 纯文本 Markdown 无红色语义，标红以加粗对等）。</summary>
    private const string ErrorHeading = "**错误**";

    /// <summary>工具调用引用行前缀（"&gt; 工具 " + 工具名）。</summary>
    private const string ToolCallPrefix = "> 工具 ";

    /// <summary>导出会话为 Markdown 文本（逐项类型分派组装 — 段间空行；空会话空串）。</summary>
    /// <param name="items">会话滚动项集合（ReplView 消息列表快照 — 宿主传入）。</param>
    /// <returns>Markdown 格式导出全文（不可导出项跳过 — banner/spinner 等装饰项零产出）。</returns>
    public static string Export(IReadOnlyList<IScrollItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return string.Join("\n\n", items.Select(SectionOf).Where(static section => section.Length > 0));
    }

    /// <summary>单项段组装（类型分派 — 未知/装饰类型空段由 Where 过滤跳过）。</summary>
    private static string SectionOf(IScrollItem item) => item switch
    {
        UserMessageItem user => HeadingBlock(UserHeading, CopyTextExtractor.Extract(user)),
        AssistantMessageItem assistant => HeadingBlock(AssistantHeading, CopyTextExtractor.Extract(assistant)),
        MarkdownMessageItem markdown => HeadingBlock(AssistantHeading, CopyTextExtractor.Extract(markdown)),
        ErrorMessageItem error => HeadingBlock(ErrorHeading, CopyTextExtractor.Extract(error)),
        IToolCallInfo toolCall => ToolCallBlock(toolCall),
        ToolResultItem or DiffResultItem or SubagentGroupItem => QuoteBlock(CopyTextExtractor.Extract(item)),
        _ => string.Empty,
    };

    /// <summary>标题段 = 标题 + 空行 + 正文（正文 null/空 → 空段跳过 — 空消息无导出价值）。</summary>
    private static string HeadingBlock(string heading, string? text) =>
        text is { Length: > 0 } ? $"{heading}\n\n{text}" : string.Empty;

    /// <summary>工具调用段 = "&gt; 工具 {名}" 引用行 +（主参数非空时）空行 + 主参数原文
    /// （提取面 = CopyTextExtractor 对 IToolCallInfo 的主参数分支 — PrimaryInputMap 容错解析）。</summary>
    private static string ToolCallBlock(IToolCallInfo toolCall) =>
        PrimaryInputMap.ExtractPrimaryInput(toolCall.ToolName, toolCall.Args) is { Length: > 0 } primary
            ? $"{ToolCallPrefix}{toolCall.ToolName}\n\n{primary}"
            : $"{ToolCallPrefix}{toolCall.ToolName}";

    /// <summary>引用块段 = 提取文本逐行加 "&gt; " 前缀（空行裸 "&gt;" 防尾随空格；null/空 → 空段跳过）。</summary>
    private static string QuoteBlock(string? text) =>
        text is { Length: > 0 }
            ? string.Join('\n', text.Split('\n').Select(static line => line.Length == 0 ? ">" : $"> {line}"))
            : string.Empty;
}
