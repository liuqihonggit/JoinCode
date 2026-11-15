using System.Text;
using System.Text.RegularExpressions;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.File;

/// <summary>
/// unified diff 应用（apply_patch）—— 纯字符串与文件读写，不依赖 IFileSystem / 引擎。
/// <para>宿主实现（ApplyPatchLogic）在 <c>modules/cognition/hands_core</c> 里绑定了 IFileSystem 与
/// 编码探测器；这里按 BCL 重写同一套语义，并保留最有价值的那部分：上下文不匹配时打印
/// 「期望行 vs 实际行」对照，让 LLM 知道该改哪一行，而不是只拿到一句 context mismatch。</para>
/// </summary>
internal static partial class FilePatchCommands {
    private const int MaxDiagnosticDiffs = 5;
    private const int TruncateAt = 120;

    public static Task<int> ApplyPatchAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var patch = Required(options, "patch");
        var dryRun = IsTrue(options, "dry_run");
        var workingDirectory = Get(options, "working_dir", string.Empty);

        var hunks = ParsePatch(patch);
        if (hunks.Count == 0) {
            Console.Error.WriteLine("补丁里没有可用的 hunk。正确写法: 提供 `--- a/x` / `+++ b/x` / `@@ -n,m +n,m @@` 组成的 unified diff。");
            return Task.FromResult(2);
        }

        var modified = 0;
        var failures = 0;
        var modifiedPaths = new List<string>();

        foreach (var group in hunks.GroupBy(hunk => hunk.FilePath)) {
            ct.ThrowIfCancellationRequested();
            var filePath = ResolvePath(group.Key, workingDirectory);
            if (!global::System.IO.File.Exists(filePath)) {
                Console.Out.WriteLine($"FAIL {filePath}: file not found");
                failures++;
                continue;
            }
            var (failed, hunkCount, detail) = ApplyToFile(filePath, group.ToList(), dryRun);
            if (failed) {
                Console.Out.WriteLine(detail);
                Console.Out.WriteLine($"SKIP {filePath}: left unchanged (patch did not apply cleanly)");
                failures++;
                continue;
            }
            if (!dryRun) modifiedPaths.Add(filePath);
            Console.Out.WriteLine($"OK {(dryRun ? "Would modify" : "Modified")} {filePath} ({hunkCount} hunk(s))");
            modified++;
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(dryRun
            ? $"预演结束: 将修改 {modified} 个文件，失败 {failures} 个（未写入磁盘）"
            : $"完成: 修改 {modified} 个文件，失败 {failures} 个");
        if (modifiedPaths.Count > 0) Console.Out.WriteLine($"已修改: {string.Join(", ", modifiedPaths)}");
        return Task.FromResult(failures > 0 ? 2 : 0);
    }

    /// <summary>按 hunk 顺序应用到一个文件；失败时返回要打印的逐行诊断。</summary>
    private static (bool Failed, int HunkCount, string Detail) ApplyToFile(
        string filePath, List<PatchHunk> hunks, bool dryRun) {
        var bytes = global::System.IO.File.ReadAllBytes(filePath);
        var (original, encoding, hasBom) = Decode(bytes);
        var lines = SplitLines(original);
        // 行尾风格要跟着原文件走：CRLF 文件被写成 LF，会让整文件在 git 里变成全行修改。
        var newline = UsesCrlf(original) ? "\r\n" : "\n";
        var offset = 0;

        foreach (var hunk in hunks.OrderBy(item => item.StartLine)) {
            var adjustedStart = hunk.StartLine - 1 + offset;
            if (!VerifyContext(lines, adjustedStart, hunk)) {
                return (true, hunks.Count, BuildContextMismatchMessage(filePath, hunk, lines, adjustedStart));
            }
            var (updated, removed, added) = ApplyHunk(lines, adjustedStart, hunk);
            lines = updated;
            offset += added - removed;
        }

        if (dryRun) return (false, hunks.Count, string.Empty);
        var newContent = string.Join(newline, lines);
        global::System.IO.File.WriteAllBytes(filePath, Encode(newContent, encoding, hasBom));
        return (false, hunks.Count, string.Empty);
    }

    /// <summary>判断原文件以 CRLF 为主 — CRLF 行多于裸 LF 才算（混合行尾文件按多数处理）。</summary>
    private static bool UsesCrlf(string content) {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < content.Length; i++) {
            if (content[i] != '\n') continue;
            lf++;
            if (i > 0 && content[i - 1] == '\r') crlf++;
        }
        return crlf > lf - crlf;
    }

    /// <summary>
    /// 解码：识别 UTF-8 BOM / UTF-16 BOM，其余按 UTF-8 处理。
    /// <para>不猜测 GBK 等本地编码 —— 猜错会静默写坏文件；非法字节直接抛解码异常，
    /// 让用户知道「这个文件不是 UTF-8」，而不是给他一个改烂的文件。</para>
    /// </summary>
    private static (string Text, Encoding Encoding, bool HasBom) Decode(byte[] bytes) {
        // 带 BOM 的原样保留：写成 UTF8Encoding(true)，否则 GetPreamble() 为空，改完 BOM 就丢了。
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true), true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, true);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, true);
        return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false), false);
    }

    private static byte[] Encode(string content, Encoding encoding, bool hasBom) {
        var body = encoding.GetBytes(content);
        if (!hasBom) return body;
        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static List<PatchHunk> ParsePatch(string patch) {
        var hunks = new List<PatchHunk>();
        string? currentFile = null;
        PatchHunk? currentHunk = null;

        foreach (var rawLine in patch.Split('\n')) {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 && currentHunk is null) continue;

            if (line.StartsWith("--- ", StringComparison.Ordinal)) continue;
            if (line.StartsWith("+++ ", StringComparison.Ordinal)) {
                var path = line[4..];
                if (path.StartsWith("b/", StringComparison.Ordinal)) path = path[2..];
                currentFile = path.Trim();
                continue;
            }
            if (line.StartsWith("diff --git", StringComparison.Ordinal)
                || line.StartsWith("index ", StringComparison.Ordinal)) continue;

            var match = HunkHeaderRegex().Match(line);
            if (match.Success && currentFile is not null) {
                currentHunk = new PatchHunk {
                    FilePath = currentFile,
                    StartLine = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                };
                hunks.Add(currentHunk);
                continue;
            }
            if (currentHunk is not null
                && (line.StartsWith('+') || line.StartsWith('-') || line.StartsWith(' '))) {
                currentHunk.Lines.Add(line);
            }
        }
        return hunks;
    }

    private static bool VerifyContext(List<string> fileLines, int startIndex, PatchHunk hunk) {
        var index = startIndex;
        foreach (var line in hunk.Lines) {
            if (!line.StartsWith(' ') && !line.StartsWith('-')) continue;
            if (index < 0 || index >= fileLines.Count) return false;
            if (fileLines[index] != line[1..]) return false;
            index++;
        }
        return true;
    }

    /// <summary>上下文不匹配时给出逐行对照 —— 只说 context mismatch 等于让 LLM 去猜。</summary>
    private static string BuildContextMismatchMessage(
        string filePath, PatchHunk hunk, List<string> fileLines, int adjustedStart) {
        var builder = new StringBuilder(256);
        builder.Append("FAIL ").Append(filePath).Append(':').Append(hunk.StartLine).AppendLine(": context mismatch");
        builder.AppendLine("[诊断] 期望的 context 行 vs 文件实际内容:");
        var index = adjustedStart;
        var diffs = 0;
        foreach (var line in hunk.Lines) {
            if (!line.StartsWith(' ') && !line.StartsWith('-')) continue;
            var expected = line[1..];
            var actual = index >= 0 && index < fileLines.Count ? fileLines[index] : "<EOF>";
            builder.Append(expected == actual ? "   " : " ! ").Append("期望: ").AppendLine(Truncate(expected));
            if (expected == actual) {
                index++;
                continue;
            }
            builder.Append("    实际: ").AppendLine(Truncate(actual));
            diffs++;
            if (diffs >= MaxDiagnosticDiffs) {
                builder.AppendLine("  ... (后续差异行省略)");
                break;
            }
            index++;
        }
        return builder.ToString().TrimEnd();
    }

    private static string Truncate(string line) =>
        line.Length <= TruncateAt ? line : string.Concat(line.AsSpan(0, TruncateAt), "...[truncated]");

    private static (List<string> Result, int Removed, int Added) ApplyHunk(
        List<string> lines, int startIndex, PatchHunk hunk) {
        var result = new List<string>(lines.Take(startIndex));
        var removed = 0;
        var added = 0;
        var source = startIndex;
        foreach (var line in hunk.Lines) {
            if (line.StartsWith(' ')) {
                result.Add(lines[source]);
                source++;
            } else if (line.StartsWith('-')) {
                source++;
                removed++;
            } else {
                result.Add(line[1..]);
                added++;
            }
        }
        result.AddRange(lines.Skip(source));
        return (result, removed, added);
    }

    private static List<string> SplitLines(string content) {
        if (string.IsNullOrEmpty(content)) return [];
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].EndsWith('\r')) lines[i] = lines[i][..^1];
        }
        return [.. lines];
    }

    private static string ResolvePath(string path, string workingDirectory) {
        if (path.Length == 0 || Path.IsPathRooted(path) || workingDirectory.Length == 0) return path;
        var separator = workingDirectory.EndsWith('/') || workingDirectory.EndsWith('\\') ? "" : "/";
        return workingDirectory + separator + path;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException(
                $"apply_patch 缺少必填参数 --{key}。正确写法: jcc-tool-file.exe apply_patch --patch \"$(cat fix.patch)\"");

    private static string Get(IReadOnlyDictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool IsTrue(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderRegex();

    private sealed class PatchHunk {
        public required string FilePath { get; init; }
        public required int StartLine { get; init; }
        public List<string> Lines { get; } = [];
    }
}
