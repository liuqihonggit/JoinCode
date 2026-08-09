namespace Infrastructure.IO.Services.Diff;

/// <summary>
/// 结构化 Patch 生成器 — 对齐 TS npm diff 库的 structuredPatch 函数
/// 使用 Myers diff 算法生成 StructuredPatchHunk[]
/// </summary>
public static class StructuredPatchGenerator
{
    /// <summary>
    /// 默认上下文行数 — 需求要求 diff 窗口上下各留 4 行上下文
    /// </summary>
    private const int DefaultContextLines = 4;

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
        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromMilliseconds(DiffTimeoutMs));
        var linkedToken = cts.Token;

        // 计算编辑脚本（Myers diff），带超时检查
        var edits = ComputeEditScript(oldLines, newLines, linkedToken);
        if (edits is null)
            return []; // 超时或取消，返回空数组（对齐 TS: if (!result) return []）

        // 从编辑脚本生成 hunks
        var hunks = BuildHunks(edits, oldLines, newLines, contextLines);

        // 对齐 TS: unescapeFromDiff — 反转义每一行的 Content
        for (var i = 0; i < hunks.Length; i++)
        {
            hunks[i] = hunks[i] with { Lines = hunks[i].Lines.Select(line => line with { Content = UnescapeFromDiff(line.Content) }).ToArray() };
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
            // 注意：trace 在每轮 d 开始时快照 v，故 trace[d] 即经典 Myers 的 V[d-1]。
            // 回溯必须基于当前快照 v 判定 prevK/prevX，使用 trace[d-1] 会导致 off-by-one，
            // 使靠近文件末尾的变更被错误吞掉（仅剩首段上下文）。
            var v = trace[d];
            var k = x - y;

            int prevK;
            if (k == -d || (k != d && v.GetValueOrDefault(k - 1, 0) < v.GetValueOrDefault(k + 1, 0)))
            {
                prevK = k + 1; // 向下移动来的（插入）
            }
            else
            {
                prevK = k - 1; // 向右移动来的（删除）
            }

            var prevX = v.GetValueOrDefault(prevK, 0);
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
    /// 基于 EditOp 直接构建，避免因插入/删除导致行错位后按内容比对失效的问题
    /// </summary>
    private static StructuredPatchHunk[] BuildHunks(
        List<EditOp> edits, string[] oldLines, string[] newLines, int contextLines)
    {
        if (edits.Count == 0)
            return [];

        // 找出所有变更区段（连续非 Equal 的 EditOp 区间）
        var changeSpans = FindChangeSpans(edits);

        if (changeSpans.Count == 0)
            return [];

        // 扩展上下文（前后各 contextLines 个 Equal op），合并重叠/相邻
        var ranges = ExpandAndMergeSpans(changeSpans, edits.Count, contextLines);

        // 为每个区间生成 hunk — 直接遍历 EditOp 输出
        var hunks = new List<StructuredPatchHunk>(ranges.Count);
        foreach (var (start, end) in ranges)
        {
            var hunk = BuildHunkFromOps(edits, start, end, oldLines, newLines);
            if (hunk.Lines.Any())
                hunks.Add(hunk);
        }

        return hunks.ToArray();
    }

    /// <summary>
    /// 找出所有变更区段（连续非 Equal 的 EditOp 起止索引）
    /// </summary>
    private static List<(int Start, int End)> FindChangeSpans(List<EditOp> edits)
    {
        var spans = new List<(int Start, int End)>();

        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Type == EditType.Equal)
                continue;

            var start = i;
            while (i + 1 < edits.Count && edits[i + 1].Type != EditType.Equal)
                i++;

            spans.Add((start, i));
        }

        return spans;
    }

    /// <summary>
    /// 扩展上下文并合并重叠/相邻区段
    /// </summary>
    private static List<(int Start, int End)> ExpandAndMergeSpans(
        List<(int Start, int End)> spans, int totalOps, int contextLines)
    {
        var ranges = new List<(int Start, int End)>();

        foreach (var (start, end) in spans)
        {
            var expandedStart = Math.Max(0, start - contextLines);
            var expandedEnd = Math.Min(totalOps - 1, end + contextLines);

            if (ranges.Count > 0 && expandedStart <= ranges[^1].End + 1)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, expandedEnd));
            }
            else
            {
                ranges.Add((expandedStart, expandedEnd));
            }
        }

        return ranges;
    }

    /// <summary>
    /// 根据 EditOp 区间构建单个 Hunk（直接按 op 类型输出，不做内容比对）
    /// </summary>
    private static StructuredPatchHunk BuildHunkFromOps(
        List<EditOp> edits, int start, int end, string[] oldLines, string[] newLines)
    {
        var lines = new List<PatchLine>();

        for (var i = start; i <= end; i++)
        {
            var edit = edits[i];
            switch (edit.Type)
            {
                case EditType.Equal:
                    lines.Add(new PatchLine
                    {
                        Type = PatchLineType.Context,
                        Content = oldLines[edit.OldIndex],
                        OldLineNumber = edit.OldIndex + 1,
                        NewLineNumber = edit.NewIndex + 1
                    });
                    break;
                case EditType.Delete:
                    lines.Add(new PatchLine
                    {
                        Type = PatchLineType.Removed,
                        Content = oldLines[edit.OldIndex],
                        OldLineNumber = edit.OldIndex + 1,
                        NewLineNumber = null
                    });
                    break;
                case EditType.Insert:
                    lines.Add(new PatchLine
                    {
                        Type = PatchLineType.Added,
                        Content = newLines[edit.NewIndex],
                        OldLineNumber = null,
                        NewLineNumber = edit.NewIndex + 1
                    });
                    break;
            }
        }

        // 计算 hunk 头：起始行号 = 区间前已消费的旧/新行数 + 1
        var oldStart = 1;
        var newStart = 1;
        for (var i = 0; i < start; i++)
        {
            if (edits[i].Type != EditType.Insert) oldStart++;
            if (edits[i].Type != EditType.Delete) newStart++;
        }

        var oldCount = lines.Count(l => l.OldLineNumber is not null);
        var newCount = lines.Count(l => l.NewLineNumber is not null);

        return new StructuredPatchHunk
        {
            OldStart = oldStart,
            OldLines = oldCount,
            NewStart = newStart,
            NewLines = newCount,
            Header = $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@",
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
