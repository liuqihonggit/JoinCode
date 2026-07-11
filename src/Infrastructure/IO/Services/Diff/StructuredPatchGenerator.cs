namespace Infrastructure.IO.Services.Diff;

/// <summary>
/// 结构化 Patch 生成器 — 对齐 TS npm diff 库的 structuredPatch 函数
/// 使用 Myers diff 算法生成 StructuredPatchHunk[]
/// </summary>
public static class StructuredPatchGenerator
{
    /// <summary>
    /// 默认上下文行数 — 对齐 TS CONTEXT_LINES = 3
    /// </summary>
    private const int DefaultContextLines = 3;

    /// <summary>
    /// Diff 超时时间 — 对齐 TS DIFF_TIMEOUT_MS = 5000
    /// </summary>
    private const int DiffTimeoutMs = 5000;

    /// <summary>
    /// &amp; 转义 token — 对齐 TS AMPERSAND_TOKEN
    /// diff 库对 &amp; 字符存在 bug，需先转义再计算 diff
    /// </summary>
    private const string AmpersandToken = "<<:AMPERSAND_TOKEN:>>";

    /// <summary>
    /// $ 转义 token — 对齐 TS DOLLAR_TOKEN
    /// diff 库对 $ 字符存在 bug，需先转义再计算 diff
    /// </summary>
    private const string DollarToken = "<<:DOLLAR_TOKEN:>>";

    /// <summary>
    /// 从旧内容和新内容生成结构化 Patch
    /// 对齐 TS: getPatchFromContents — escapeForDiff → structuredPatch → unescapeFromDiff
    /// </summary>
    /// <param name="filePath">文件路径（同时用作旧/新文件名）</param>
    /// <param name="oldContent">旧文件内容</param>
    /// <param name="newContent">新文件内容</param>
    /// <param name="contextLines">上下文行数，默认3</param>
    /// <param name="cancellationToken">取消令牌（超时 5s，对齐 TS DIFF_TIMEOUT_MS）</param>
    /// <returns>结构化 Patch Hunk 数组；超时或取消时返回空数组</returns>
    public static StructuredPatchHunk[] Generate(
        string filePath,
        string oldContent,
        string newContent,
        int contextLines = DefaultContextLines,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: escapeForDiff — 转义 & 和 $ 字符避免 diff 算法 bug
        var escapedOld = EscapeForDiff(oldContent);
        var escapedNew = EscapeForDiff(newContent);

        var oldLines = SplitLines(escapedOld);
        var newLines = SplitLines(escapedNew);

        // 空文件特殊处理
        if (oldLines.Length == 0 && newLines.Length == 0)
            return [];

        // 对齐 TS: DIFF_TIMEOUT_MS = 5000 — 使用 CancellationToken 实现超时
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DiffTimeoutMs);
        var linkedToken = cts.Token;

        // 计算编辑脚本（Myers diff），带超时检查
        var edits = ComputeEditScript(oldLines, newLines, linkedToken);
        if (edits is null)
            return []; // 超时或取消，返回空数组（对齐 TS: if (!result) return []）

        // 从编辑脚本生成 hunks
        var hunks = BuildHunks(edits, oldLines, newLines, contextLines);

        // 对齐 TS: unescapeFromDiff — 反转义每一行的 Content
        foreach (var hunk in hunks)
        {
            for (var i = 0; i < hunk.Lines.Length; i++)
            {
                hunk.Lines[i] = hunk.Lines[i] with { Content = UnescapeFromDiff(hunk.Lines[i].Content) };
            }
        }

        return hunks;
    }

    /// <summary>
    /// 统计 Patch 中的添加/删除行数 — 对齐 TS countLinesChanged
    /// </summary>
    public static (int Added, int Removed) CountLinesChanged(StructuredPatchHunk[] hunks)
    {
        var added = 0;
        var removed = 0;
        foreach (var hunk in hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.Type == PatchLineType.Added) added++;
                else if (line.Type == PatchLineType.Removed) removed++;
            }
        }
        return (added, removed);
    }

    /// <summary>
    /// 将 StructuredPatchHunk 格式化为统一 diff 文本
    /// </summary>
    public static string FormatUnifiedDiff(string filePath, StructuredPatchHunk[] hunks)
    {
        if (hunks.Length == 0) return string.Empty;

        var sb = new StringBuilder(256);
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        foreach (var hunk in hunks)
        {
            if (!string.IsNullOrEmpty(hunk.Header))
                sb.AppendLine(hunk.Header);
            else
                sb.AppendLine($"@@ -{hunk.OldStart},{hunk.OldLines} +{hunk.NewStart},{hunk.NewLines} @@");

            foreach (var line in hunk.Lines)
            {
                var prefix = line.Type switch
                {
                    PatchLineType.Added => '+',
                    PatchLineType.Removed => '-',
                    _ => ' '
                };
                sb.Append(prefix);
                sb.AppendLine(line.Content);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 分割文本为行数组（保留空行，不保留换行符）
    /// </summary>
    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        // 与 TS 的 content.split('\n') 行为对齐
        return content.Split('\n');
    }

    /// <summary>
    /// 计算 Myers diff 编辑脚本
    /// 使用优化的线性空间 Myers 算法
    /// </summary>
    /// <returns>编辑脚本列表；超时或取消时返回 null</returns>
    private static List<EditOp>? ComputeEditScript(string[] oldLines, string[] newLines, CancellationToken cancellationToken)
    {
        var n = oldLines.Length;
        var m = newLines.Length;
        var max = n + m;

        if (max == 0)
            return [];

        // 特殊情况：一方为空
        if (n == 0)
        {
            var ops = new List<EditOp>(m);
            for (var i = 0; i < m; i++)
                ops.Add(new EditOp(EditType.Insert, -1, i));
            return ops;
        }

        if (m == 0)
        {
            var ops = new List<EditOp>(n);
            for (var i = 0; i < n; i++)
                ops.Add(new EditOp(EditType.Delete, i, -1));
            return ops;
        }

        // Myers diff 算法
        var v = new Dictionary<int, int> { [1] = 0 };
        var trace = new List<Dictionary<int, int>>();

        for (var d = 0; d <= max; d++)
        {
            // 对齐 TS DIFF_TIMEOUT_MS: 每 d 步检查一次取消/超时
            if (d % 100 == 0 && cancellationToken.IsCancellationRequested)
                return null;

            var currentV = new Dictionary<int, int>(v);
            trace.Add(currentV);

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v.GetValueOrDefault(k - 1, 0) < v.GetValueOrDefault(k + 1, 0)))
                {
                    x = v.GetValueOrDefault(k + 1, 0); // 向下移动（插入）
                }
                else
                {
                    x = v.GetValueOrDefault(k - 1, 0) + 1; // 向右移动（删除）
                }

                var y = x - k;

                // 沿对角线移动（相等）
                while (x < n && y < m && oldLines[x] == newLines[y])
                {
                    x++;
                    y++;
                }

                v[k] = x;

                if (x >= n && y >= m)
                {
                    // 回溯编辑脚本
                    return Backtrack(trace, oldLines, newLines, n, m);
                }
            }
        }

        // 不应该到这里
        return [];
    }

    /// <summary>
    /// 回溯编辑脚本
    /// </summary>
    private static List<EditOp> Backtrack(
        List<Dictionary<int, int>> trace,
        string[] oldLines, string[] newLines,
        int n, int m)
    {
        var ops = new List<EditOp>();
        var x = n;
        var y = m;

        for (var d = trace.Count - 1; d > 0; d--)
        {
            var v = trace[d];
            var prevV = trace[d - 1];
            var k = x - y;

            int prevK;
            if (k == -d || (k != d && prevV.GetValueOrDefault(k - 1, 0) < prevV.GetValueOrDefault(k + 1, 0)))
            {
                prevK = k + 1; // 向下移动来的（插入）
            }
            else
            {
                prevK = k - 1; // 向右移动来的（删除）
            }

            var prevX = prevV.GetValueOrDefault(prevK, 0);
            var prevY = prevX - prevK;

            // 沿对角线回溯（相等行）
            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                ops.Add(new EditOp(EditType.Equal, x, y));
            }

            if (d > 0)
            {
                if (x == prevX)
                {
                    // 插入
                    y--;
                    ops.Add(new EditOp(EditType.Insert, -1, y));
                }
                else
                {
                    // 删除
                    x--;
                    ops.Add(new EditOp(EditType.Delete, x, -1));
                }
            }
        }

        // 处理 d=0 的对角线
        while (x > 0 && y > 0)
        {
            x--;
            y--;
            ops.Add(new EditOp(EditType.Equal, x, y));
        }

        // 反转（回溯是从后往前的）
        ops.Reverse();

        return ops;
    }

    /// <summary>
    /// 从编辑脚本构建 Hunk 数组
    /// </summary>
    private static StructuredPatchHunk[] BuildHunks(
        List<EditOp> edits, string[] oldLines, string[] newLines, int contextLines)
    {
        if (edits.Count == 0)
            return [];

        // 找出所有变更区域（包含上下文）
        var changeRanges = FindChangeRanges(edits, oldLines, newLines, contextLines);

        // 合并重叠的区域
        var mergedRanges = MergeRanges(changeRanges, oldLines.Length, newLines.Length);

        // 为每个区域生成 hunk
        var hunks = new List<StructuredPatchHunk>(mergedRanges.Count);
        foreach (var range in mergedRanges)
        {
            var hunk = BuildHunk(range, oldLines, newLines);
            if (hunk.Lines.Length > 0)
                hunks.Add(hunk);
        }

        return hunks.ToArray();
    }

    /// <summary>
    /// 找出所有变更区域（含上下文）
    /// </summary>
    private static List<ChangeRange> FindChangeRanges(
        List<EditOp> edits, string[] oldLines, string[] newLines, int contextLines)
    {
        var ranges = new List<ChangeRange>();
        ChangeRange? current = null;

        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (edit.Type == EditType.Equal)
                continue;

            // 变更行
            var oldStart = Math.Max(0, (edit.OldIndex >= 0 ? edit.OldIndex : 0) - contextLines);
            var newStart = Math.Max(0, (edit.NewIndex >= 0 ? edit.NewIndex : 0) - contextLines);

            // 扩展到包含连续的变更行
            var oldEnd = edit.OldIndex >= 0 ? edit.OldIndex + 1 : 0;
            var newEnd = edit.NewIndex >= 0 ? edit.NewIndex + 1 : 0;

            // 向后扫描连续变更
            for (var j = i + 1; j < edits.Count; j++)
            {
                if (edits[j].Type == EditType.Equal)
                    break;
                if (edits[j].OldIndex >= 0)
                    oldEnd = edits[j].OldIndex + 1;
                if (edits[j].NewIndex >= 0)
                    newEnd = edits[j].NewIndex + 1;
                i = j;
            }

            // 添加上下文
            oldEnd = Math.Min(oldLines.Length, oldEnd + contextLines);
            newEnd = Math.Min(newLines.Length, newEnd + contextLines);
            oldStart = Math.Max(0, oldStart);
            newStart = Math.Max(0, newStart);

            if (current is not null && oldStart <= current.OldEnd)
            {
                // 合并到当前区域
                current = current with
                {
                    OldEnd = Math.Max(current.OldEnd, oldEnd),
                    NewEnd = Math.Max(current.NewEnd, newEnd)
                };
            }
            else
            {
                if (current is not null)
                    ranges.Add(current);
                current = new ChangeRange(oldStart, oldEnd, newStart, newEnd);
            }
        }

        if (current is not null)
            ranges.Add(current);

        return ranges;
    }

    /// <summary>
    /// 合并重叠或相邻的区域
    /// </summary>
    private static List<ChangeRange> MergeRanges(List<ChangeRange> ranges, int oldTotal, int newTotal)
    {
        if (ranges.Count <= 1)
            return ranges;

        var merged = new List<ChangeRange> { ranges[0] };

        for (var i = 1; i < ranges.Count; i++)
        {
            var last = merged[^1];
            var current = ranges[i];

            if (current.OldStart <= last.OldEnd)
            {
                merged[^1] = last with
                {
                    OldEnd = Math.Max(last.OldEnd, current.OldEnd),
                    NewEnd = Math.Max(last.NewEnd, current.NewEnd)
                };
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    /// <summary>
    /// 为单个区域构建 Hunk
    /// </summary>
    private static StructuredPatchHunk BuildHunk(ChangeRange range, string[] oldLines, string[] newLines)
    {
        var lines = new List<PatchLine>();
        var oldLine = range.OldStart;
        var newLine = range.NewStart;

        // 使用双指针遍历 old 和 new 的行
        var oi = range.OldStart;
        var ni = range.NewStart;

        while (oi < range.OldEnd || ni < range.NewEnd)
        {
            // 检查是否是相等的行（对角线移动）
            if (oi < range.OldEnd && ni < range.NewEnd && oldLines[oi] == newLines[ni])
            {
                lines.Add(new PatchLine
                {
                    Type = PatchLineType.Context,
                    Content = oldLines[oi],
                    OldLineNumber = oi + 1,
                    NewLineNumber = ni + 1
                });
                oi++;
                ni++;
                oldLine++;
                newLine++;
            }
            else
            {
                // 先输出删除行
                if (oi < range.OldEnd && (ni >= range.NewEnd || oldLines[oi] != newLines[ni]))
                {
                    // 检查是否是纯删除（old 有但 new 没有）
                    lines.Add(new PatchLine
                    {
                        Type = PatchLineType.Removed,
                        Content = oldLines[oi],
                        OldLineNumber = oi + 1,
                        NewLineNumber = null
                    });
                    oi++;
                    oldLine++;
                }

                // 再输出添加行
                if (ni < range.NewEnd && (oi >= range.OldEnd || (oi < range.OldEnd && oldLines[oi] != newLines[ni])))
                {
                    lines.Add(new PatchLine
                    {
                        Type = PatchLineType.Added,
                        Content = newLines[ni],
                        OldLineNumber = null,
                        NewLineNumber = ni + 1
                    });
                    ni++;
                    newLine++;
                }
            }
        }

        var oldCount = range.OldEnd - range.OldStart;
        var newCount = range.NewEnd - range.NewStart;

        return new StructuredPatchHunk
        {
            OldStart = range.OldStart + 1, // 1-based
            OldLines = oldCount,
            NewStart = range.NewStart + 1, // 1-based
            NewLines = newCount,
            Header = $"@@ -{range.OldStart + 1},{oldCount} +{range.NewStart + 1},{newCount} @@",
            Lines = lines.ToArray()
        };
    }

    private enum EditType : byte
    {
        Equal,
        Insert,
        Delete
    }

    private readonly record struct EditOp(EditType Type, int OldIndex, int NewIndex);

    private sealed record ChangeRange(int OldStart, int OldEnd, int NewStart, int NewEnd);

    /// <summary>
    /// 转义 &amp; 和 $ 字符 — 对齐 TS escapeForDiff
    /// diff 库对这两个字符存在 bug，需先替换为 token 再计算 diff
    /// </summary>
    private static string EscapeForDiff(string s)
    {
        if (!s.Contains('&') && !s.Contains('$'))
            return s;

        return s.Replace("&", AmpersandToken).Replace("$", DollarToken);
    }

    /// <summary>
    /// 反转义 token 为原始字符 — 对齐 TS unescapeFromDiff
    /// </summary>
    private static string UnescapeFromDiff(string s)
    {
        if (!s.Contains(AmpersandToken, StringComparison.Ordinal) && !s.Contains(DollarToken, StringComparison.Ordinal))
            return s;

        return s.Replace(AmpersandToken, "&").Replace(DollarToken, "$");
    }
}
