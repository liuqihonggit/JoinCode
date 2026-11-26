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
            AssistantMessageItem assistant => ThinkingLinesFor(assistant.ThinkingRedacted, assistant.ThinkingContent)
                .Concat(PrefixedLines($"{Figures.MessageBullet} ", assistant.Text)),
            MarkdownMessageItem markdown => ThinkingLinesFor(markdown.ThinkingRedacted, markdown.ThinkingContent)
                .Concat(MarkdownLines(markdown.Document)),
            ToolCallItem toolCall => ToolCallLines(toolCall),
            ToolResultItem result => ToolResultLines(result),
            DiffResultItem diff => DiffLines(diff),
            ErrorMessageItem error => PrefixedLines($"{Figures.MessageBullet} ", error.Text),
            StreamingTextItem streaming => IndentedLines(streaming.Text),
            SubagentGroupItem group => SubagentLines(group),
            CollapsedToolGroupItem collapsed => CollapsedGroupLines(collapsed),
            CondensedToolProgressItem condensed => CondensedLines(condensed),
            // TASK079 — 注入上下文折叠项：展开态投影全文（系统提示词+工具清单），塌缩态投影摘要行
            // TASK107 P10 — Summary/Render 自带 "📎 " 前缀，此处不得再补（否则 "📎 📎 首次注入…"）；
            // 展开态正文是裸系统提示词，前缀仍需补。
            PromptContextItem prompt => prompt.Expanded && prompt.ExpandedContent is { } content
                ? PrefixedLines("📎 ", content)
                : [prompt.Summary],
            _ => [],
        };
    }

    /// <summary>
    /// 按显示宽度折行（TASK106 真机缺陷 — transcript 页是绝对定位写入，右缘即裁，没有终端软折行：
    /// 长段落必须自己折，否则详情页整段被切在右边界，读不到句尾）。
    /// 宽度按 <see cref="RuneWidth"/> 计（CJK 双宽）；ASCII 尽量在空白处断（回落硬断）；
    /// 折出的小段不带前导空白（空白不落行首）。width &lt;= 0 或本行不超宽时原样返回。
    /// </summary>
    public static IEnumerable<string> Wrap(string line, int width)
    {
        if (width <= 0 || RuneWidth.OfText(line) <= width)
        {
            return [line];
        }

        var runes = line.EnumerateRunes().ToList();
        var result = new List<string>();
        var start = 0;
        while (start < runes.Count)
        {
            var used = 0;
            var cursor = start;
            var lastBreak = -1;
            while (cursor < runes.Count)
            {
                var runeWidth = RuneWidth.Of(runes[cursor]);
                if (used + runeWidth > width)
                {
                    break;
                }

                used += runeWidth;
                if (cursor + 1 < runes.Count && IsBreakBoundary(runes[cursor], runes[cursor + 1]))
                {
                    lastBreak = cursor + 1;
                }

                cursor++;
            }

            if (cursor >= runes.Count)
            {
                result.Add(Slice(runes, start, runes.Count).TrimEnd());
                break;
            }

            // 优先在分词边界断（英文单词不被腰斩）；整段无边界（超长 token）才硬断
            var end = lastBreak > start ? lastBreak : cursor;
            result.Add(Slice(runes, start, end).TrimEnd());
            start = end;
            while (start < runes.Count && runes[start].Value == ' ')
            {
                start++;
            }
        }

        return result;
    }

    /// <summary>是否允许在 next 之前断行 — 只要两侧不都是 ASCII 字母数字（同一个英文单词）就可以断；
    /// CJK/空白/标点天然是断点（中文没有空格分词）。</summary>
    private static bool IsBreakBoundary(Rune current, Rune next) =>
        !(IsWordChar(current) && IsWordChar(next));

    private static bool IsWordChar(Rune rune) =>
        rune.Value < 128 && (char.IsLetterOrDigit((char)rune.Value) || rune.Value is '_' or '-');

    private static string Slice(List<Rune> runes, int start, int end)
    {
        var builder = new StringBuilder(end - start);
        for (var i = start; i < end; i++)
        {
            builder.Append(runes[i].ToString());
        }

        return builder.ToString();
    }

    /// <summary>带前缀的文本行（\n 分行 — 首行带前缀，续行原样）。</summary>
    private static IEnumerable<string> PrefixedLines(string prefix, string text)
    {        if (text.Length == 0)
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
    /// <summary>TASK069 #18 — 思考行投影：脱敏块无 CoT 文本，投影 "✻ Thinking…" 存根
    ///（对齐基准 AssistantRedactedThinkingMessage — transcript/verbose 才可见）。</summary>
    private static IEnumerable<string> ThinkingLinesFor(bool redacted, string? thinkingContent) =>
        redacted ? [$"{Figures.TeardropAsterisk} Thinking…"] : ThinkingLines(thinkingContent);

    private static IEnumerable<string> ThinkingLines(string? thinkingContent)
    {
        if (thinkingContent is not { Length: > 0 })
        {
            return [];
        }

        var lines = thinkingContent.Split('\n');
        var capped = lines.Length > MaxThinkingLines
            ? lines[..MaxThinkingLines].Append($"… 思考内容超 {MaxThinkingLines} 行已截断")
            : lines;
        // TASK107 — 每行加 ∴ 前缀：详情页里 CoT 与正文同为裸文本，无前缀时读不出边界
        return capped.Select(line => $"{Figures.ThereforeSign} {line}");
    }

    /// <summary>Markdown 块模型 → 行（块间空行 — 阅读形态还原）。
    /// TASK107 P7/P8 — 详情页曾自带一套"块拍平"投影（表格按 `a | b` 源形态、嵌套列表拼进父项、
    /// 分隔行 `|---|---|` 直接上屏），与主屏 box 边框/缩进形态不一致。改为复用
    /// <see cref="MarkdownLayout"/>（主屏同一套布局器），保证两处渲染同源；
    /// 宽度取无界值：折行交给 <see cref="Wrap"/>（详情页有真实宽度时统一折）。</summary>
    private static IEnumerable<string> MarkdownLines(MarkdownDocument document)
    {
        var blocks = MarkdownLayout.Layout(document, UnboundedLayoutWidth)
            .Select(static segments => string.Concat(segments.Select(static segment => segment.Text)))
            .Where(static line => line.Length > 0)
            .ToList();
        // 圆点行占位（对齐主屏 ● col0 形态 — 空文档也保底 1 行）
        var firstLine = blocks.Count > 0 ? blocks[0] : string.Empty;
        yield return $"{Figures.MessageBullet} {firstLine}";
        foreach (var line in blocks.Skip(1))
        {
            yield return line;
        }
    }

    /// <summary>详情页布局宽度（无界 — 段落不在布局层折行，折行由 <see cref="Wrap"/> 按真实宽度统一处理）。</summary>
    private const int UnboundedLayoutWidth = int.MaxValue;

    /// <summary>工具调用行 — `● Name(args)` 标题行 + 参数续行（对齐主屏
    /// <see cref="MessageRenderer.RenderToolCall"/> 形态：单行闭括号、多行末行闭括号）。
    /// TASK107 P11 — 详情页曾省略包裹括号（"● Bash echo hi"），与主屏 "● Bash(echo hi)" 不一致。</summary>
    private static IEnumerable<string> ToolCallLines(ToolCallItem toolCall)
    {
        var lines = toolCall.DisplayArgs.Split('\n');
        var first = lines[0].TrimEnd('\r');
        yield return lines.Length == 1
            ? $"{Figures.MessageBullet} {toolCall.DisplayToolName}({first})"
            : $"{Figures.MessageBullet} {toolCall.DisplayToolName}({first}";
        for (var i = 1; i < lines.Length; i++)
        {
            var text = lines[i].TrimEnd('\r');
            yield return i == lines.Length - 1 ? $"  {text})" : $"  {text}";
        }
    }

    /// <summary>工具结果行（verbose 全量）— summary ⎿ 行 + patch hunks + RawOutput 全行；
    /// 错误形态（TASK069-M5）投影归一后的错误块全文（与渲染同源，不叠摘要 + 输出两份）。</summary>
    private static IEnumerable<string> ToolResultLines(ToolResultItem result)
    {
        if (result.IsError)
        {
            foreach (var line in ErrorBlockLines(result.ErrorText))
            {
                yield return line;
            }

            foreach (var line in PatchLines(result.Patch))
            {
                yield return line;
            }

            yield break;
        }

        // TASK107 — 摘要取首个非空行（多行 resultText 直接拼 "\n" 会在详情页挤成一行；
        // 全文由下方 RawOutput 承载，摘要只做定位锚）。diff 统计行优先（与主屏一致）。
        var summaryLine = result.Stats is { IsEmpty: false } ? result.DisplaySummary : result.SummaryLine;
        if (summaryLine.Length > 0)
        {
            yield return $"{ToolResultIndent}{summaryLine}";
        }

        foreach (var line in PatchLines(result.Patch))
        {
            yield return line;
        }

        if (result.RawOutput is { Length: > 0 } rawOutput)
        {
            var output = rawOutput.TrimEnd();
            // TASK107 — 输出首行与摘要行同源（引擎 summary = 结果文本前 200 字符），
            // 详情页再画一次就是同一句连续出现两遍；首行已由摘要行承载，此处跳过。
            var newlineIndex = output.IndexOf('\n', StringComparison.Ordinal);
            if (summaryLine.Length > 0
                && newlineIndex > 0
                && output[..newlineIndex].TrimEnd(' ', '\r').Equals(summaryLine, StringComparison.Ordinal))
            {
                output = output[(newlineIndex + 1)..];
            }

            // TASK107 P9 — 摘要行已带 "  ⎿  " 前缀；输出块此时整体作为续行缩进，
            // 否则详情页出现连续两行 "⎿"（摘要 + 输出首行各一次，读起来像两条结果）。
            foreach (var line in IndentedLines(output, firstWithPrefix: summaryLine.Length == 0))
            {
                yield return line;
            }
        }
    }

    /// <summary>错误块行（首行 ⎿ 前缀，续行等宽缩进 — 对齐 MessageRenderer.RenderToolErrorLine）。</summary>
    private static IEnumerable<string> ErrorBlockLines(string content)
    {
        var continuation = new string(' ', ToolResultIndent.Length);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            yield return i == 0 ? $"{ToolResultIndent}{lines[i]}" : $"{continuation}{lines[i]}";
        }
    }

    /// <summary>diff 结果行 — 首行（有 patch 时为增删统计）+ patch hunks（无输出块形态）。</summary>
    private static IEnumerable<string> DiffLines(DiffResultItem diff)
    {
        yield return $"{ToolResultIndent}{diff.DisplaySummary}";
        foreach (var line in PatchLines(diff.Patch))
        {
            yield return line;
        }
    }

    /// <summary>patch hunks → 行（4 缩进 hunk 头 + 行号 gutter + 标记 + 正文 — 与主屏
    /// <see cref="MessageRenderer.RenderDiffLine"/> 同源列格式；TASK108 P16 — 此前详情页只有
    /// +/- 前缀没有行号，改文件后无法按行定位）。</summary>
    private static IEnumerable<string> PatchLines(DiffHunk[]? patch)
    {
        var hunks = patch ?? [];
        var gutterDigits = hunks.Length > 0 ? DiffGutter.GutterDigits(hunks) : 0;
        foreach (var hunk in hunks)
        {
            yield return $"    {hunk.Header}";
            var lineNumbers = DiffGutter.AssignLineNumbers(hunk);
            for (var index = 0; index < hunk.Lines.Length; index++)
            {
                var line = hunk.Lines[index];
                var marker = line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => " ",
                };
                var gutter = MessageRenderer.GutterText(lineNumbers[index], gutterDigits);
                yield return $"    {gutter}{marker}{line.Content}";
            }
        }
    }

    /// <summary>流式/输出正文行 — 首行 ⎿ 前缀 + 续行缩进（对齐 StreamingTextItem 形态）。
    /// firstWithPrefix = false 时首行也走续行缩进（上层已画过 ⎿ 摘要行）。</summary>
    private static IEnumerable<string> IndentedLines(string text, bool firstWithPrefix = true)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            yield return i == 0 && firstWithPrefix
                ? $"{ToolResultIndent}{lines[i]}"
                : $"{StreamingContinuation}{lines[i]}";
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
}
