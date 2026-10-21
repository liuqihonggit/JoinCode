namespace NativeTui.Core.Repl;

/// <summary>
/// 工具调用数据契约（消息操作域）— 暴露工具名与原始参数串供提取器/操作条消费。
/// ToolCallItem 主构造参数当前无公开面（S2 域实现接线，见未决问题）；未实现前
/// 提取器对该项按不可复制处理（null）。
/// </summary>
public interface IToolCallInfo
{
    /// <summary>工具名（主参数映射查表键 — 精确匹配）。</summary>
    string ToolName { get; }

    /// <summary>原始参数串（JSON object 或简写摘要 — PrimaryInputMap 容错解析）。</summary>
    string Args { get; }
}

/// <summary>
/// 复制全文提取器（消息操作域）— 对齐基准 messageActions.tsx copyTextOf：按消息项类型提取可复制全文：
/// 用户消息原文（剥离 system-reminder 块）/ assistant 纯文本 / Markdown 纯文本（块模型拍平）/
/// 工具调用主参数值（对齐基准 assistant tool_use 分支 — 工具消息的可复制面即主参数）/
/// 工具结果与错误正文 / diff 结果 summary+patch 拼接 / 子代理分组逐条目拼接（基准 grouped_tool_use
/// results.join('\n\n') 语义 — 本项目以活动日志文本面对等）。不可复制类型返回 null。
/// 剪贴板写入不在本类（宿主/S6 域接线）— 只产出文本。
/// </summary>
public static partial class CopyTextExtractor
{
    /// <summary>提取消息项的可复制全文（null = 不可复制类型 — banner/spinner 等装饰项）。</summary>
    public static string? Extract(IScrollItem item) => item switch
    {
        UserMessageItem user => StripSystemReminders(user.Text),
        AssistantMessageItem assistant => assistant.Text,
        MarkdownMessageItem markdown => MarkdownToPlainText(markdown.Document),
        ToolResultItem result => result.Text,
        ErrorMessageItem error => error.Text,
        DiffResultItem diff => DiffToText(diff),
        SubagentGroupItem group => GroupToText(group.Coordinator),
        IToolCallInfo toolCall => PrimaryInputMap.ExtractPrimaryInput(toolCall.ToolName, toolCall.Args),
        _ => null,
    };

    /// <summary>剥离 system-reminder 块（全文正则替换 — 连同块前空白一起吃掉防双空格残留 + 首尾去空白；
    /// 基准 stripSystemReminders 仅剥头部连续块，本项目推广到中部注入场景覆盖更广）。</summary>
    public static string StripSystemReminders(string text) =>
        text.Length == 0 ? text : SystemReminderRegex().Replace(text, string.Empty).Trim();

    /// <summary>system-reminder 块正则（块前空白吸收 + 非贪婪跨行 — GeneratedRegex AOT 源生成）。</summary>
    [GeneratedRegex(@"\s*<system-reminder>.*?</system-reminder>", RegexOptions.Singleline)]
    private static partial Regex SystemReminderRegex();

    /// <summary>Markdown 文档拍平纯文本（块间空行 — 复制面还原 Markdown 源阅读形态）。</summary>
    private static string MarkdownToPlainText(MarkdownDocument document) =>
        string.Join("\n\n", document.Blocks.Select(BlockToPlainText).Where(static text => text.Length > 0));

    /// <summary>单块拍平纯文本。</summary>
    private static string BlockToPlainText(MarkdownBlock block) => block switch
    {
        MarkdownParagraph paragraph => SegmentsToPlainText(paragraph.Segments),
        MarkdownHeading heading => SegmentsToPlainText(heading.Segments),
        MarkdownCode code => code.Text,
        MarkdownQuote quote => string.Join("\n\n", quote.Children.Select(BlockToPlainText).Where(static text => text.Length > 0)),
        MarkdownList list => string.Join('\n', list.Items.Select((item, index) =>
            $"{(list.Ordered ? $"{list.Start + index}. " : "- ")}{ItemToPlainText(item)}")),
        MarkdownTable table => TableToPlainText(table),
        MarkdownRule => "---",
        _ => string.Empty,
    };

    /// <summary>行内段拼接（样式丢弃 — 纯文本面）。</summary>
    private static string SegmentsToPlainText(IReadOnlyList<MarkdownSegment> segments) =>
        string.Join(string.Empty, segments.Select(static segment => segment.Text));

    /// <summary>列表项拍平（项内子块空格拼接 — 通常单段落）。</summary>
    private static string ItemToPlainText(MarkdownListItem item) =>
        string.Join(' ', item.Children.Select(BlockToPlainText).Where(static text => text.Length > 0));

    /// <summary>表格拍平（表头 + 数据行，单元格段拼接、列间 "| " — 对齐 Markdown 表格源形态）。</summary>
    private static string TableToPlainText(MarkdownTable table) =>
        string.Join('\n', new[] { RowCells(table.Headers) }.Concat(table.Rows.Select(static row => RowCells(row))));

    /// <summary>单表格行拼接（单元格间 "| "）。</summary>
    private static string RowCells(IReadOnlyList<IReadOnlyList<MarkdownSegment>> cells) =>
        string.Join(" | ", cells.Select(SegmentsToPlainText));

    /// <summary>diff 结果拍平（summary + 逐 hunk：header + 前缀行 +/-/空格）。</summary>
    private static string DiffToText(DiffResultItem diff) =>
        string.Join('\n', new[] { diff.Summary }.Concat(diff.Patch.Select(HunkToText)));

    /// <summary>单 hunk 拍平（header + 逐 diff 行 diff 前缀形态）。</summary>
    private static string HunkToText(DiffHunk hunk) =>
        string.Join('\n', new[] { hunk.Header }.Concat(hunk.Lines.Select(static line => line.Kind switch
        {
            DiffLineKind.Added => $"+{line.Content}",
            DiffLineKind.Removed => $"-{line.Content}",
            _ => $" {line.Content}",
        })));

    /// <summary>子代理分组拍平（逐条目状态行 + 活动日志行，条目间空行 — 对齐基准 grouped_tool_use
    /// results.map(...).filter(Boolean).join('\n\n') 语义，本项目以活动日志文本面对等工具结果）。</summary>
    private static string? GroupToText(SubagentGroupCoordinator coordinator)
    {
        var blocks = coordinator.Agents
            .Select(static entry => string.Join('\n',
                new[] { $"{entry.AgentType} ({entry.Description}) · {entry.ToolUseCount} tool uses · {entry.TokenCount} tokens" }
                    .Concat(entry.RecentActivities.Select(static activity => $"{activity.ToolName}: {activity.Summary}"))))
            .Where(static block => block.Length > 0)
            .ToList();
        return blocks.Count == 0 ? null : string.Join("\n\n", blocks);
    }
}
