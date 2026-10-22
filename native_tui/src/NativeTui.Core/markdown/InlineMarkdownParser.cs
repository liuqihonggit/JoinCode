namespace NativeTui.Core.Markdown;

/// <summary>
/// 行内 Markdown 轻量解析器（TASK064/TASK065 P3 — CoT 展开态渲染专用）：
/// Core 零依赖铁律下不引 Markdig 卫星，手写行级扫描 **bold** / *italic* / `code` 三件套，
/// 产出 Core 段模型（MarkdownSegment — 渲染层经 MapMarkdownStyle 映射后叠加 dim 基调）。
/// 平级扫描不递归（粗体内嵌套标记字面保留）；_italic_ 不支持（snake_case 误判防护）；
/// 未闭合标记字面降级。契约：空列表 = 无行内语法（整行纯文本降级渲染）；
/// 非空 = 至少含一个带样式段（plain 段夹于其间，扫描顺序天然连续无需合并）。
/// </summary>
public static class InlineMarkdownParser
{
    /// <summary>解析单行 → 行内段序列（空列表 = 纯文本无行内语法 — 渲染降级信号）。</summary>
    public static IReadOnlyList<MarkdownSegment> ParseLine(ReadOnlySpan<char> line)
    {
        var segments = new List<MarkdownSegment>();
        var plainStart = 0;
        var index = 0;
        while (index < line.Length)
        {
            var marker = ScanMarker(line, index);
            if (marker is null)
            {
                index++;
                continue;
            }

            AppendPlain(segments, line[plainStart..index]);
            segments.Add(new MarkdownSegment(line[(index + marker.Value.MarkerLength)..marker.Value.ContentEnd].ToString(), marker.Value.Style));
            index = marker.Value.ContentEnd + marker.Value.MarkerLength;
            plainStart = index;
        }

        // 有带样式段才追加尾部 plain（无样式整行 → 空列表降级契约）
        if (segments.Count > 0)
        {
            AppendPlain(segments, line[plainStart..]);
        }

        return segments;
    }

    /// <summary>行首标记扫描分派 — 反引号走 code、星号走强调（无标记字符返回 null）。</summary>
    private static MarkerMatch? ScanMarker(ReadOnlySpan<char> line, int index) => line[index] switch
    {
        '`' => ScanDelimited(line, index, "`", MarkdownStyle.Code),
        '*' => ScanAsterisk(line, index),
        _ => null,
    };

    /// <summary>星号强调扫描 — *** 三连优先（Bold|Italic），未闭合退化为 ** 粗体，最后单 * 斜体。</summary>
    private static MarkerMatch? ScanAsterisk(ReadOnlySpan<char> line, int index)
    {
        var triple = HasRun(line, index, 3)
            ? ScanDelimited(line, index, "***", MarkdownStyle.Bold | MarkdownStyle.Italic)
            : null;
        if (triple is not null)
        {
            return triple;
        }

        return HasRun(line, index, 2)
            ? ScanDelimited(line, index, "**", MarkdownStyle.Bold)
            : ScanDelimited(line, index, "*", MarkdownStyle.Italic);
    }

    /// <summary>定界符闭合扫描 — 内容起点后找闭合定界符（未闭合或空内容返回 null — 字面降级）。</summary>
    private static MarkerMatch? ScanDelimited(ReadOnlySpan<char> line, int index, string delimiter, MarkdownStyle style)
    {
        var contentStart = index + delimiter.Length;
        var closing = line[contentStart..].IndexOf(delimiter.AsSpan(), StringComparison.Ordinal);
        return closing > 0
            ? new MarkerMatch(style, delimiter.Length, contentStart + closing)
            : null;
    }

    /// <summary>位置起连续 markerLength 个星号检测。</summary>
    private static bool HasRun(ReadOnlySpan<char> line, int index, int markerLength)
    {
        if (index + markerLength > line.Length)
        {
            return false;
        }

        for (var offset = 0; offset < markerLength; offset++)
        {
            if (line[index + offset] != '*')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>plain 文本段追加（空文本跳过 — 空段不产出）。</summary>
    private static void AppendPlain(List<MarkdownSegment> segments, ReadOnlySpan<char> text)
    {
        if (text.Length > 0)
        {
            segments.Add(new MarkdownSegment(text.ToString(), MarkdownStyle.None));
        }
    }

    /// <summary>行内标记匹配结果 — 样式 + 定界符长度 + 内容结束位置（exclusive）。</summary>
    private readonly record struct MarkerMatch(MarkdownStyle Style, int MarkerLength, int ContentEnd);
}
