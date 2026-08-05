namespace Tools.Handlers;

[Register]
public sealed partial class ApplyPatchLogic : ServiceEntity
{
    [Inject] private readonly IFileSystem _fs;

    public async Task<ApplyPatchResult> ApplyAsync(
        string patch,
        bool dryRun,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var hunks = ParsePatch(patch);
        if (hunks.Count == 0)
            return ApplyPatchResult.FailureResult("No valid hunks found in patch");

        var details = new List<string>();
        var filesModified = 0;
        var failures = 0;

        var fileHunks = hunks.GroupBy(h => h.FilePath);

        foreach (var group in fileHunks)
        {
            var filePath = ResolvePath(group.Key, workingDirectory);

            if (!_fs.FileExists(filePath))
            {
                details.Add($"FAIL {filePath}: file not found");
                failures++;
                continue;
            }

            var originalContent = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var originalLines = SplitLines(originalContent);
            var modifiedLines = new List<string>(originalLines);
            var offset = 0;
            var hunkFailed = false;

            foreach (var hunk in group.OrderBy(h => h.StartLine))
            {
                var adjustedStart = hunk.StartLine - 1 + offset;

                if (!VerifyContext(modifiedLines, adjustedStart, hunk))
                {
                    details.Add($"FAIL {filePath}:{hunk.StartLine}: context mismatch");
                    hunkFailed = true;
                    break;
                }

                var (newLines, linesRemoved, linesAdded) = ApplyHunk(modifiedLines, adjustedStart, hunk);
                modifiedLines = newLines;
                offset += linesAdded - linesRemoved;
            }

            if (hunkFailed)
            {
                details.Add($"SKIP {filePath}: left unchanged (patch did not apply cleanly)");
                failures++;
                continue;
            }

            if (!dryRun)
            {
                var newContent = string.Join("\n", modifiedLines);
                await _fs.WriteAllTextAsync(filePath, newContent, cancellationToken).ConfigureAwait(false);
            }

            var verb = dryRun ? "Would modify" : "Modified";
            details.Add($"OK {verb} {filePath} ({group.Count()} hunk(s))");
            filesModified++;
        }

        if (failures > 0)
            return ApplyPatchResult.PartialResult(filesModified, failures, details, dryRun);

        return ApplyPatchResult.SuccessResult(filesModified, details, dryRun);
    }

    internal static List<PatchHunk> ParsePatch(string patch)
    {
        var hunks = new List<PatchHunk>();
        var lines = patch.Split('\n');
        string? currentFile = null;
        PatchHunk? currentHunk = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("--- "))
                continue;

            if (line.StartsWith("+++ "))
            {
                var path = line[4..];
                if (path.StartsWith("b/"))
                    path = path[2..];
                currentFile = path.Trim();
                continue;
            }

            if (line.StartsWith("diff --git") || line.StartsWith("index "))
                continue;

            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success && currentFile is not null)
            {
                currentHunk = new PatchHunk
                {
                    FilePath = currentFile,
                    StartLine = int.Parse(hunkMatch.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
                };
                hunks.Add(currentHunk);
                continue;
            }

            if (currentHunk is not null &&
                (line.StartsWith('+') || line.StartsWith('-') || line.StartsWith(' ')))
            {
                currentHunk.Lines.Add(line);
            }
        }

        return hunks;
    }

    private static bool VerifyContext(List<string> fileLines, int startIndex, PatchHunk hunk)
    {
        var fileIdx = startIndex;
        foreach (var line in hunk.Lines)
        {
            if (line.StartsWith(' ') || line.StartsWith('-'))
            {
                if (fileIdx >= fileLines.Count) return false;
                var expected = line[1..];
                if (fileLines[fileIdx] != expected) return false;
                fileIdx++;
            }
        }
        return true;
    }

    private static (List<string> Result, int Removed, int Added) ApplyHunk(
        List<string> lines, int startIndex, PatchHunk hunk)
    {
        var result = new List<string>(lines[..startIndex]);
        var removed = 0;
        var added = 0;
        var sourceIdx = startIndex;

        foreach (var line in hunk.Lines)
        {
            if (line.StartsWith(' '))
            {
                result.Add(lines[sourceIdx]);
                sourceIdx++;
            }
            else if (line.StartsWith('-'))
            {
                sourceIdx++;
                removed++;
            }
            else if (line.StartsWith('+'))
            {
                result.Add(line[1..]);
                added++;
            }
        }

        result.AddRange(lines[sourceIdx..]);
        return (result, removed, added);
    }

    private static string ResolvePath(string path, string? workingDirectory)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        if (Path.IsPathRooted(path))
            return path;

        if (string.IsNullOrEmpty(workingDirectory))
            return path;

        var separator = workingDirectory.EndsWith('/') || workingDirectory.EndsWith('\\') ? "" : "/";
        return workingDirectory + separator + path;
    }

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        return content.Split('\n');
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderRegex();

    internal sealed class PatchHunk
    {
        public required string FilePath { get; init; }
        public required int StartLine { get; init; }
        public List<string> Lines { get; } = [];
    }
}
