namespace JoinCode.Cli;

/// <summary>
/// Diff 解析器 — 解析统一 diff 输出
/// </summary>
public sealed class DiffParser
{
    /// <summary>
    /// 解析统一 diff 输出为纯文本行列表 — 对齐 TS parseGitDiff 简化版
    /// </summary>
    public static string Parse(string diffOutput)
    {
        if (string.IsNullOrWhiteSpace(diffOutput)) return string.Empty;

        var sb = new StringBuilder();
        var lines = diffOutput.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git") || line.StartsWith("index ") || line.StartsWith("--- a/") || line.StartsWith("+++ b/"))
            {
                continue;
            }

            if (line.StartsWith("@@"))
            {
                sb.AppendLine($"{TerminalColors.Primary}{line}{AnsiStyleConstants.Reset}");
                continue;
            }

            if (line.StartsWith('+'))
            {
                sb.AppendLine($"{TerminalColors.Success}{line}{AnsiStyleConstants.Reset}");
            }
            else if (line.StartsWith('-'))
            {
                sb.AppendLine($"{TerminalColors.Error}{line}{AnsiStyleConstants.Reset}");
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析统一 diff 输出为按文件分组的结构化 hunk — 对齐 TS parseGitDiff
    /// </summary>
    public static Dictionary<string, StructuredHunk[]> ParseStructured(string diffOutput)
    {
        if (string.IsNullOrWhiteSpace(diffOutput)) return [];

        var result = new Dictionary<string, List<StructuredHunk>>();
        var lines = diffOutput.Split('\n');

        string? currentFile = null;
        var currentHunks = new List<StructuredHunk>();
        var hunkLines = new List<DiffLine>();
        var inHunk = false;
        var hunkOldStart = 0;
        var hunkOldLines = 0;
        var hunkNewStart = 0;
        var hunkNewLines = 0;
        var hunkHeader = string.Empty;
        var oldLine = 0;
        var newLine = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("--- a/") || line.StartsWith("--- /dev/null"))
            {
                continue;
            }

            if (line.StartsWith("+++ b/"))
            {
                if (currentFile is not null)
                {
                    FlushHunk(currentHunks, hunkLines, hunkOldStart, hunkOldLines, hunkNewStart, hunkNewLines, hunkHeader);
                    result[currentFile] = currentHunks;
                    currentHunks = [];
                }

                currentFile = line[6..];
                inHunk = false;
                hunkLines = [];
                continue;
            }

            if (line.StartsWith("+++ /dev/null"))
            {
                if (currentFile is not null)
                {
                    FlushHunk(currentHunks, hunkLines, hunkOldStart, hunkOldLines, hunkNewStart, hunkNewLines, hunkHeader);
                    result[currentFile] = currentHunks;
                    currentHunks = [];
                }

                currentFile = null;
                inHunk = false;
                hunkLines = [];
                continue;
            }

            if (line.StartsWith("@@"))
            {
                if (inHunk)
                {
                    FlushHunk(currentHunks, hunkLines, hunkOldStart, hunkOldLines, hunkNewStart, hunkNewLines, hunkHeader);
                    hunkLines = [];
                }

                inHunk = true;
                var header = ParseFullHunkHeader(line);
                hunkOldStart = header.OldStart;
                hunkOldLines = header.OldLines;
                hunkNewStart = header.NewStart;
                hunkNewLines = header.NewLines;
                hunkHeader = line;
                oldLine = hunkOldStart;
                newLine = hunkNewStart;
                continue;
            }

            if (!inHunk) continue;

            if (line.StartsWith('+'))
            {
                hunkLines.Add(new DiffLine(PatchLineType.Added, line[1..], null, newLine));
                newLine++;
                hunkNewLines = Math.Max(hunkNewLines, newLine - hunkNewStart);
            }
            else if (line.StartsWith('-'))
            {
                hunkLines.Add(new DiffLine(PatchLineType.Removed, line[1..], oldLine, null));
                oldLine++;
                hunkOldLines = Math.Max(hunkOldLines, oldLine - hunkOldStart);
            }
            else if (line.StartsWith(' '))
            {
                hunkLines.Add(new DiffLine(PatchLineType.Context, line[1..], oldLine, newLine));
                oldLine++;
                newLine++;
            }
            else
            {
                hunkLines.Add(new DiffLine(PatchLineType.Context, line, null, null));
            }
        }

        if (inHunk && currentFile is not null)
        {
            FlushHunk(currentHunks, hunkLines, hunkOldStart, hunkOldLines, hunkNewStart, hunkNewLines, hunkHeader);
            result[currentFile] = currentHunks;
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
    }

    private static void FlushHunk(List<StructuredHunk> hunks, List<DiffLine> lines,
        int oldStart, int oldLines, int newStart, int newLines, string header)
    {
        if (lines.Count == 0) return;
        hunks.Add(new StructuredHunk(oldStart, oldLines, newStart, newLines, header, lines.ToArray()));
        lines.Clear();
    }

    private static (int OldStart, int OldLines, int NewStart, int NewLines) ParseFullHunkHeader(string line)
    {
        var span = line.AsSpan();
        var i = 0;

        while (i < span.Length && span[i] != '@') i++;
        i += 2;

        while (i < span.Length && span[i] != '-') i++;
        if (i >= span.Length) return (1, 0, 1, 0);
        i++;

        var oldStart = ParseNumber(span, ref i);
        var oldLines = 0;
        if (i < span.Length && span[i] == ',')
        {
            i++;
            oldLines = ParseNumber(span, ref i);
        }

        while (i < span.Length && span[i] == ' ') i++;

        while (i < span.Length && span[i] != '+') i++;
        if (i >= span.Length) return (oldStart, oldLines, 1, 0);
        i++;

        var newStart = ParseNumber(span, ref i);
        var newLines = 0;
        if (i < span.Length && span[i] == ',')
        {
            i++;
            newLines = ParseNumber(span, ref i);
        }

        return (oldStart, oldLines, newStart, newLines);
    }

    private static int ParseNumber(ReadOnlySpan<char> span, ref int i)
    {
        var start = i;
        while (i < span.Length && char.IsDigit(span[i])) i++;
        return i > start && int.TryParse(span[start..i], out var num) ? num : 0;
    }
}
