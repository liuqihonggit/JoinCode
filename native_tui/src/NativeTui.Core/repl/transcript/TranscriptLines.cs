namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 快照行投影器（S6 收敛 — TranscriptController 快照提供器的数据面）：
/// 消息滚动项 → 全文纯文本行序列（verbose 形态 — CoT 全文 / 工具输出全量 / Markdown 块展开，
/// 对齐基准 transcript verbose 渲染）。行前缀对齐主屏渲染形态（❯ 用户 / ● 消息 / ⎿ 结果缩进 /
/// ∴ CoT）供 transcript 全屏阅读定位；样式归 TranscriptRenderer 统一处理（本投影纯文本零样式）。
/// CoT 行数守卫 200（对齐 AssistantMessageItem.MaxThinkingLines — 刷屏防护）。
/// 装饰项（banner/spinner/对话框/分隔线）投影为空（transcript 只看消息流）。
/// </summary>
public static class TranscriptLines
{
    /// <summary>CoT 全文行数上限守卫（对齐消息项截断先例 — CoT 刷屏防护）。</summary>
    private const int MaxThinkingLines = 200;

    /// <summary>工具结果缩进前缀（对齐 MessageRenderer 渲染形态 — transcript 行可读定位）。</summary>
    private const string ToolResultIndent = Figures.ToolResultIndent;

    /// <summary>流式正文续行缩进（对齐 StreamingTextItem 渲染形态）。</summary>
    private const string StreamingContinuation = "      ";

    /// <summary>投影消息项的全文行（空序列 = 装饰项跳过）。</summary>
    public static IEnumerable<string> Project(IScrollItem item)
    {
        return item switch
        {
            UserMessageItem user => PrefixedLines($"{Figures.Pointer} ", user.Text),
            AssistantMessageItem assistant => ThinkingLines(assistant.ThinkingContent)
                .Concat(PrefixedLines($"{Figures.MessageBullet} ", assistant.Text)),
            MarkdownMessageItem markdown => ThinkingLines(markdown.ThinkingContent)
                .Concat(MarkdownLines(markdown.Document)),
            ToolCallItem toolCall => ToolCallLines(toolCall),
            ToolResultItem result => ToolResultLines(result),
            DiffResultItem diff => DiffLines(diff),
            ErrorMessageItem error => PrefixedLines($"{Figures.MessageBullet} ", error.Text),
            StreamingTextItem streaming => IndentedLines(streaming.Text),
            SubagentGroupItem group => SubagentLines(group),
            CollapsedToolGroupItem collapsed => CollapsedGroupLines(collapsed),
            CondensedToolProgressItem condensed => CondensedLines(condensed),
            _ => [],
        };
    }

    /// <summary>带前缀的文本行（\n 分行 — 首行带前缀，续行原样）。</summary>
    private static IEnumerable<string> PrefixedLines(string prefix, string text)
    {
        if (text.Length == 0)
        {
            yield return prefix.TrimEnd();
            yield break;
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            yield return i == 0 ? prefix + lines[i] : lines[i];
        }
    }

    /// <summary>CoT 全文行（截断守卫 — 超限截断附提示行；null/空 = 空）。</summary>
    private static IEnumerable<string> ThinkingLines(string? thinkingContent)
    {
        if (thinkingContent is not { Length: > 0 })
        {
            return [];
        }

        var lines = thinkingContent.Split('\n');
        return lines.Length > MaxThinkingLines
            ? lines[..MaxThinkingLines].Append($"… 思考内容超 {MaxThinkingLines} 行已截断")
            : lines;
    }

    /// <summary>Markdown 块模型 → 行（块间空行 — 阅读形态还原）。</summary>
    private static IEnumerable<string> MarkdownLines(MarkdownDocument document)
    {
        var blocks = document.Blocks.SelectMany(BlockToLines).Where(static line => line.Length > 0).ToList();
        // 圆点行占位（对齐主屏 ● col0 形态 — 空文档也保底 1 行）
        var firstLine = blocks.Count > 0 ? blocks[0] : string.Empty;
        yield return $"{Figures.MessageBullet} {firstLine}";
        foreach (var line in blocks.Skip(1))
        {
            yield return line;
        }
    }

    /// <summary>单块 → 行（段落/标题一行；代码多行；引用/列表/表格展开）。</summary>
    private static IEnumerable<string> BlockToLines(MarkdownBlock block) => block switch
    {
        MarkdownParagraph paragraph => [SegmentsText(paragraph.Segments)],
        MarkdownHeading heading => [SegmentsText(heading.Segments)],
        MarkdownCode code => SplitLines(code.Text),
        MarkdownQuote quote => quote.Children.SelectMany(BlockToLines),
        MarkdownList list => list.Items.Select((item, index) =>
            $"{ListMarker(list, index)}{ItemText(item)}"),
        MarkdownTable table => TableLines(table),
        MarkdownRule => ["---"],
        _ => [],
    };

    /// <summary>行内段拼接（样式丢弃 — 纯文本面）。</summary>
    private static string SegmentsText(IReadOnlyList<MarkdownSegment> segments) =>
        string.Join(string.Empty, segments.Select(static segment => segment.Text));

    /// <summary>列表项标记（有序 `N. ` / 无序 `- `）。</summary>
    private static string ListMarker(MarkdownList list, int index) =>
        list.Ordered ? $"{list.Start + index}. " : "- ";

    /// <summary>列表项拍平（项内子块空格拼接 — 通常单段落）。</summary>
    private static string ItemText(MarkdownListItem item) =>
        string.Join(' ', item.Children.Select(BlockToLines).Select(static lines => string.Join(' ', lines)));

    /// <summary>表格 → 行（表头 + 数据行，单元格间 " | " — 源形态还原）。</summary>
    private static IEnumerable<string> TableLines(MarkdownTable table) =>
        new[] { RowText(table.Headers) }.Concat(table.Rows.Select(static row => RowText(row)));

    /// <summary>单表格行拼接。</summary>
    private static string RowText(IReadOnlyList<IReadOnlyList<MarkdownSegment>> cells) =>
        string.Join(" | ", cells.Select(SegmentsText));

    /// <summary>工具调用行 — `● Name args` 标题行 + 参数续行（2 空格缩进近似渲染对齐）。</summary>
    private static IEnumerable<string> ToolCallLines(ToolCallItem toolCall)
    {
        yield return $"{Figures.MessageBullet} {toolCall.DisplayToolName} {FirstLine(toolCall.DisplayArgs)}";
        foreach (var continuation in RestLines(toolCall.DisplayArgs))
        {
            yield return $"  {continuation}";
        }
    }

    /// <summary>工具结果行（verbose 全量）— summary ⎿ 行 + patch hunks + RawOutput 全行。</summary>
    private static IEnumerable<string> ToolResultLines(ToolResultItem result)
    {
        if (result.Summary.Length > 0)
        {
            yield return $"{ToolResultIndent}{result.Summary}";
        }

        foreach (var line in PatchLines(result.Patch))
        {
            yield return line;
        }

        if (result.RawOutput is { Length: > 0 } rawOutput)
        {
            foreach (var line in IndentedLines(rawOutput.TrimEnd()))
            {
                yield return line;
            }
        }
    }

    /// <summary>diff 结果行 — summary + patch hunks（无输出块形态）。</summary>
    private static IEnumerable<string> DiffLines(DiffResultItem diff)
    {
        yield return $"{ToolResultIndent}{diff.Summary}";
        foreach (var line in PatchLines(diff.Patch))
        {
            yield return line;
        }
    }

    /// <summary>patch hunks → 行（header + +/-/空格 前缀 diff 行 — 源形态还原）。</summary>
    private static IEnumerable<string> PatchLines(DiffHunk[]? patch)
    {
        foreach (var hunk in patch ?? [])
        {
            yield return hunk.Header;
            foreach (var line in hunk.Lines)
            {
                yield return line.Kind switch
                {
                    DiffLineKind.Added => $"+{line.Content}",
                    DiffLineKind.Removed => $"-{line.Content}",
                    _ => $" {line.Content}",
                };
            }
        }
    }

    /// <summary>流式/输出正文行 — 首行 ⎿ 前缀 + 续行缩进（对齐 StreamingTextItem 形态）。</summary>
    private static IEnumerable<string> IndentedLines(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            yield return i == 0 ? $"{ToolResultIndent}{lines[i]}" : $"{StreamingContinuation}{lines[i]}";
        }
    }

    /// <summary>子代理分组行（verbose 全量）— 组头 + 逐条目状态行 + 活动日志行。</summary>
    private static IEnumerable<string> SubagentLines(SubagentGroupItem group)
    {
        yield return $"{Figures.MessageBullet} {group.Coordinator.HeaderText}";
        foreach (var agent in group.Coordinator.Agents)
        {
            yield return $"{ToolResultIndent}{agent.AgentType}（{agent.Description}）· {agent.ToolUseCount} tool uses · {agent.TokenCount} tokens";
            foreach (var activity in agent.RecentActivities)
            {
                yield return $"{StreamingContinuation}{activity.ToolName}: {activity.Summary}";
            }
        }
    }

    /// <summary>聚合组行 — 摘要行 + hint 行（活动态时）。</summary>
    private static IEnumerable<string> CollapsedGroupLines(CollapsedToolGroupItem collapsed)
    {
        yield return $"{Figures.MessageBullet} {collapsed.Aggregator.SummaryText}";
        if (collapsed.Aggregator.HintText is { Length: > 0 } hint && collapsed.IsActive)
        {
            yield return $"{ToolResultIndent}{hint}";
        }
    }

    /// <summary>塌缩进度行（对齐 MessageRenderer.RenderCondensedToolProgress 文案）。</summary>
    private static IEnumerable<string> CondensedLines(CondensedToolProgressItem condensed) =>
        [$"{ToolResultIndent}In progress… · {condensed.ToolUses} tool uses"];

    /// <summary>首行（无 \n 原样；空文本空串）。</summary>
    private static string FirstLine(string text)
    {
        var index = text.IndexOf('\n');
        return index < 0 ? text : text[..index];
    }

    /// <summary>续行（首行之后的行；单行文本空序列）。</summary>
    private static IEnumerable<string> RestLines(string text)
    {
        var index = text.IndexOf('\n');
        if (index < 0)
        {
            yield break;
        }

        foreach (var line in text[(index + 1)..].Split('\n'))
        {
            yield return line;
        }
    }

    /// <summary>\n 分行（空文本单空行）。</summary>
    private static string[] SplitLines(string text) =>
        text.Length == 0 ? [string.Empty] : text.Split('\n');
}
