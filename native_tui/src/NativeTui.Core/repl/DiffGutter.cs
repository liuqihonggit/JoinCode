namespace NativeTui.Core.Repl;

/// <summary>
/// diff 行号 gutter 与语法高亮预计算（TASK066 板块 F 基建 F1）— 行号/语言检测/高亮三项渲染前置服务：
/// ① 行号：DiffLine.OldLineNumber/NewLineNumber 引擎真值优先（Added→New / Removed→Old / Context→New
/// 单列取侧，对齐基准 native-ts/color-diff/index.ts ColorDiff.render entries L877-895 行号流）；
/// 全行字段缺失（旧引擎 JSON）降级 hunk 头 "@@ -a,b +c,d @@" 解析 + 流推进
/// （Added→newLine++ / Removed→oldLine++ / Context→当前 newLine 双侧推进 — 基准同算法）；
/// ② gutter 宽：全 patch 统一 digits = 最大行号位数（基准 StructuredDiff.tsx computeGutterWidth
/// L46-49 per-hunk 口径的多 hunk 对齐化 — 同一工具结果内 gutter 列整齐；全 patch 无行号 → 0 降级无 gutter）；
/// ③ 语言检测：summary 文件路径 token 扩展名 → 语言名（基准 color-diff detectLanguage L422-451
/// 文件名/扩展名路径的渲染层近似 — ToolResult 协议无 filePath 字段，summary 即路径载体）；
/// ④ 高亮：new 侧行（Context+Added 按序 = new 文件连续片段）整块 TextMate 会话高亮
/// （ruleStack 逐行推进，跨行语法正确 — 比基准 hljs 行级独立更强）；Removed 行不参与
/// （基准 marker==='-' 不高亮纯默认色同策略，index.ts L915-918）。
/// 全部 internal — 构造期一次性预计算（渲染帧拍零推导成本），测试经 InternalsVisibleTo 直测。
/// </summary>
internal static class DiffGutter
{
    /// <summary>解析 hunk 头 "@@ -a[,b] +c[,d] @@" — 省略 count 时 = 1（git 单行语义）；
    /// 兼容 "@@ -0,0 +1,1 @@"（纯插入 0 基对齐 jsdiff）与 "@@ -9 +9 @@"；畸形头返回 false。</summary>
    /// <param name="header">hunk 头原文（如 "@@ -1,3 +1,4 @@"）。</param>
    /// <param name="oldStart">旧文件起始行号（1 基；0 = 纯插入）。</param>
    /// <param name="oldLines">旧文件行数（省略时 1）。</param>
    /// <param name="newStart">新文件起始行号。</param>
    /// <param name="newLines">新文件行数（省略时 1）。</param>
    internal static bool TryParseHunkHeader(string header, out int oldStart, out int oldLines, out int newStart, out int newLines)
    {
        oldStart = 0;
        oldLines = 0;
        newStart = 0;
        newLines = 0;
        var span = header.AsSpan().Trim();
        if (span.Length < 7 || !span.StartsWith("@@ ") || !span.EndsWith(" @@"))
        {
            return false;
        }

        span = span[3..^3].TrimStart();
        if (span.IsEmpty || span[0] != '-')
        {
            return false;
        }

        if (!TryParseRange(span[1..], out oldStart, out oldLines, out var consumed))
        {
            return false;
        }

        span = span[(1 + consumed)..].TrimStart();
        if (span.IsEmpty || span[0] != '+')
        {
            return false;
        }

        return TryParseRange(span[1..], out newStart, out newLines, out _);
    }

    /// <summary>解析 "a[,b]" 数字区间（a 起始行、b 行数；省略 b = 1）；consumed = 消费字符数（供调用方继续切剩段）。</summary>
    private static bool TryParseRange(ReadOnlySpan<char> span, out int start, out int count, out int consumed)
    {
        start = 0;
        count = 1;
        consumed = 0;
        var digits = 0;
        while (digits < span.Length && char.IsDigit(span[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(span[..digits], out start))
        {
            return false;
        }

        consumed = digits;
        if (digits < span.Length && span[digits] == ',')
        {
            var countStart = digits + 1;
            var cursor = countStart;
            while (cursor < span.Length && char.IsDigit(span[cursor]))
            {
                cursor++;
            }

            if (cursor == countStart || !int.TryParse(span[countStart..cursor], out count))
            {
                return false;
            }

            consumed = cursor;
        }

        return true;
    }

    /// <summary>hunk 各行单列行号 — 路径一：行号字段全在位（引擎投影贯通）按行种取侧直读；
    /// 路径二（降级）：hunk 头解析 + 流推进（Added→newLine++ / Removed→oldLine++ /
    /// Context→当前 newLine 且双侧推进 — 基准 color-diff entries 算法）；
    /// 畸形 hunk 头全 null（渲染降级无 gutter 列）。返回数组与 hunk.Lines 等长（索引对齐）。</summary>
    internal static int?[] AssignLineNumbers(DiffHunk hunk)
    {
        var fromFields = new int?[hunk.Lines.Length];
        for (var index = 0; index < hunk.Lines.Length; index++)
        {
            fromFields[index] = LineNumberFromField(hunk.Lines[index]);
        }

        if (fromFields.Length > 0 && fromFields.All(static lineNumber => lineNumber is not null))
        {
            return fromFields;
        }

        var result = new int?[hunk.Lines.Length];
        if (!TryParseHunkHeader(hunk.Header, out var oldLine, out _, out var newLine, out _))
        {
            return result;
        }

        for (var index = 0; index < hunk.Lines.Length; index++)
        {
            switch (hunk.Lines[index].Kind)
            {
                case DiffLineKind.Added:
                    result[index] = newLine++;
                    break;
                case DiffLineKind.Removed:
                    result[index] = oldLine++;
                    break;
                default:
                    result[index] = newLine;
                    oldLine++;
                    newLine++;
                    break;
            }
        }

        return result;
    }

    /// <summary>行种 → 行号字段取侧（Added→New / Removed→Old / Context→New — 基准单列取侧同语义）。</summary>
    private static int? LineNumberFromField(DiffLine line) => line.Kind switch
    {
        DiffLineKind.Added => line.NewLineNumber,
        DiffLineKind.Removed => line.OldLineNumber,
        _ => line.NewLineNumber,
    };

    /// <summary>全 patch 统一 gutter 位数 = 最大行号位数 — 取流推进实际最大行号与 hunk 头声明终点
    /// （oldStart+oldLines-1 / newStart+newLines-1，基准 StructuredDiff.tsx computeGutterWidth L46-49 口径）
    /// 的较大者（防御 header 行数与实际行数不自洽的演示数据：流推进可越过 header 终点）；
    /// 全 patch 无任何可推导行号（畸形 hunk 头且行字段全缺）→ 0 = 整块降级无 gutter 列（现形态）。</summary>
    internal static int GutterDigits(DiffHunk[] patch)
    {
        var maximum = 0;
        foreach (var hunk in patch)
        {
            maximum = Math.Max(maximum, HunkMaxLineNumber(hunk));
        }

        return maximum > 0 ? maximum.ToString().Length : 0;
    }

    /// <summary>单 hunk 最大行号（流推进 max + header 声明终点 max）。</summary>
    private static int HunkMaxLineNumber(DiffHunk hunk)
    {
        var maximum = AssignLineNumbers(hunk)
            .Where(static lineNumber => lineNumber is not null)
            .Select(static lineNumber => lineNumber!.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (TryParseHunkHeader(hunk.Header, out var oldStart, out var oldLines, out var newStart, out var newLines))
        {
            maximum = Math.Max(maximum, Math.Max(oldStart + oldLines - 1, newStart + newLines - 1));
        }

        return maximum;
    }

    /// <summary>summary → 语言名（扩展名小写）— 路径 token 首个含点项取最后点后字母段
    /// （"Edited a.cs"→"cs" / "Updated demo/simulated.cs (simulated)"→"cs" / "Read 10 lines"→null）；
    /// 高亮器侧 ResolveScope 别名表消化扩展名（"cs"→csharp 等）。</summary>
    internal static string? DetectLanguage(string summary)
    {
        foreach (var token in summary.Split(' '))
        {
            var path = token.Trim('(', ')');
            var dot = path.LastIndexOf('.');
            if (dot <= 0 || dot >= path.Length - 1)
            {
                continue;
            }

            var extension = path[(dot + 1)..].ToLowerInvariant();
            if (extension.Length > 0 && extension.All(char.IsLetter))
            {
                return extension;
            }
        }

        return null;
    }

    /// <summary>hunk 行级语法高亮 — new 侧行（Context+Added 按序 = new 文件连续片段）整块会话高亮，
    /// spans 映射回渲染行序（Removed 行恒 null — 基准 marker==='-' 不高亮）；
    /// 语言不支持 / 高亮器异常 → 全 null 降级纯 diff 色（不阻塞渲染）。
    /// 返回数组与 hunk.Lines 等长（索引对齐，元素 = 该行 token 色段或 null）。</summary>
    internal static IReadOnlyList<HighlightSpan>?[] HighlightHunkLines(DiffHunk hunk, ICodeHighlighter highlighter, string language)
    {
        var result = new IReadOnlyList<HighlightSpan>?[hunk.Lines.Length];
        var newSideIndices = new List<int>(hunk.Lines.Length);
        var newSideLines = new List<string>(hunk.Lines.Length);
        for (var index = 0; index < hunk.Lines.Length; index++)
        {
            if (hunk.Lines[index].Kind != DiffLineKind.Removed)
            {
                newSideIndices.Add(index);
                newSideLines.Add(hunk.Lines[index].Content);
            }
        }

        try
        {
            var highlights = highlighter.Highlight(language, newSideLines);
            if (highlights is null)
            {
                return result;
            }

            for (var side = 0; side < highlights.Count && side < newSideIndices.Count; side++)
            {
                result[newSideIndices[side]] = highlights[side];
            }
        }
        catch (Exception)
        {
            // 高亮器异常降级纯 diff 色（渲染热路径容错 — 已写 result 保持 null 默认）
        }

        return result;
    }
}
