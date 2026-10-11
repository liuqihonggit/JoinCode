// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.CliCommands;

/// <summary>
/// 高性能 ripgrep 兼容搜索引擎 — mmap + PLINQ 并行 + 零 GC 行遍历。
/// <para>ADR: 0070 — 独立于 ISearchService，直接用 MemoryMappedFile 零拷贝读取大文件，
/// PLINQ 链式组织并行搜索，Regex.IsMatch(ReadOnlySpan&lt;char&gt;) 零分配匹配。</para>
/// </summary>
internal static class RgEngine {
    private const long MmapThresholdBytes = 64 * 1024;
    private const int MaxContentLineLength = 500;

    private static ImmutableHamT<string, GitignoreMatcher?> _gitignoreCache = ImmutableHamT<string, GitignoreMatcher?>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// 执行搜索。PLINQ 链式：收集文件 → 并行搜索 → 过滤 → 排序 → 分页。
    /// </summary>
    public static RgOutcome Search(RgQuery q, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var (regex, compileError) = CompileRegex(q);
        if (regex is null)
            return RgOutcome.Failure($"无效正则表达式: {compileError}");

        var files = CollectFiles(q, ct);
        if (files.Count == 0)
            return RgOutcome.Empty();

        var degree = q.Threads is > 0 ? q.Threads.Value : Environment.ProcessorCount;

        var results = new ConcurrentBag<RgFileResult>();

        files.AsParallel()
            .WithCancellation(ct)
            .WithDegreeOfParallelism(degree)
            .Select(f => SearchFile(f, regex, q, ct))
            .Where(r => r is not null)
            .ForAll(r => results.Add(r!));

        var orderedResults = ApplySort(results, q.Sort);
        var (paged, appliedLimit, appliedOffset) = ApplyPaging(orderedResults, q.HeadLimit, q.Offset);

        return RgOutcome.Ok(paged, appliedLimit, appliedOffset);
    }

    /// <summary>
    /// 编译正则。smart-case: 模式含大写则区分大小写，否则忽略。
    /// </summary>
    private static (Regex? Regex, string? Error) CompileRegex(RgQuery q) {
        return SearchRegexCompiler.Compile(
            q.Pattern, q.CaseInsensitive, q.Multiline, q.FixedStrings, q.WordRegexp, q.SmartCase);
    }

    /// <summary>
    /// 收集搜索文件。遵守 .gitignore（除非 NoIgnore）、排除 VCS、二进制扩展名。
    /// </summary>
    private static IReadOnlyList<string> CollectFiles(RgQuery q, CancellationToken ct) {
        var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in q.Paths) {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(path)) {
                allFiles.Add(Path.GetFullPath(path));
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            foreach (var f in EnumerateFiles(path, q, ct))
                allFiles.Add(f);
        }

        return allFiles.ToList();
    }

    private static IEnumerable<string> EnumerateFiles(string root, RgQuery q, CancellationToken ct) {
        var enumeration = Directory.EnumerateFiles(root, "*", new EnumerationOptions {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = q.Hidden ? 0 : FileAttributes.Hidden,
        });

        foreach (var file in enumeration) {
            ct.ThrowIfCancellationRequested();

            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (VcsDirectoryExclusions.IsVcsPath(rel))
                continue;

            if (!q.NoIgnore && IsGitIgnored(root, rel))
                continue;

            if (!MatchesGlob(rel, q.Globs))
                continue;

            if (!FileTypeExtensionMap.MatchesFileType(file, q.FileType))
                continue;

            if (BinaryFileDetector.IsBinaryByExtension(file))
                continue;

            yield return file;
        }
    }

    private static bool IsGitIgnored(string root, string rel) {
        var snapshot = Volatile.Read(ref _gitignoreCache);
        if (!snapshot.TryGetValue(root, out var matcher)) {
            matcher = LoadGitignore(root);
            ImmutableInterlocked.Update(ref _gitignoreCache, d => d.ContainsKey(root) ? d : d.Add(root, matcher));
            matcher = Volatile.Read(ref _gitignoreCache)[root];
        }

        return matcher is not null && matcher.IsIgnored(rel);
    }

    private static GitignoreMatcher? LoadGitignore(string root) {
        var gitignorePath = Path.Combine(root, ".gitignore");
        if (!File.Exists(gitignorePath))
            return null;
        try {
            return GitignoreMatcher.Parse(File.ReadAllText(gitignorePath));
        } catch (Exception ex) {
            Diag.WriteLine($"[RgEngine.IsGitIgnored] 读取 .gitignore 失败: {ex.Message}");
            return null;
        }
    }

    private static bool MatchesGlob(string rel, IReadOnlyList<string>? globs) {
        if (globs is null || globs.Count == 0)
            return true;

        foreach (var glob in globs) {
            var normalizedGlob = glob.Replace('\\', '/');
            if (normalizedGlob.StartsWith('!')) {
                var exclude = normalizedGlob[1..];
                if (SimpleGlobMatch(rel, exclude))
                    return false;
            } else if (!SimpleGlobMatch(rel, normalizedGlob)) {
                return false;
            }
        }
        return true;
    }

    private static bool SimpleGlobMatch(string path, string pattern) {
        if (pattern == "**/*" || pattern == "*")
            return true;

        if (pattern.Contains("**/")) {
            var suffix = pattern["**/".Length..];
            return path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                   path.Contains($"/{suffix}", StringComparison.OrdinalIgnoreCase);
        }

        return GlobMatcher.IsMatch(path, pattern);
    }

    /// <summary>
    /// 搜索单个文件。大文件用 mmap 零拷贝，小文件用 ReadAllBytes。
    /// 用 ReadOnlySpan&lt;char&gt; 遍历行，Regex.IsMatch(span) 零分配匹配。
    /// </summary>
    private static RgFileResult? SearchFile(string path, Regex regex, RgQuery q, CancellationToken ct) {
        try {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0)
                return null;

            if (q.MaxFilesize is long maxSize && fileInfo.Length > maxSize)
                return null;

            byte[] bytes;
            if (fileInfo.Length >= MmapThresholdBytes) {
                using var reader = MappedFileReader.Open(path);
                bytes = reader.ToArray();
            } else {
                bytes = File.ReadAllBytes(path);
            }

            var bomLen = RgCore.DetectBom(bytes);
            var dataSpan = bytes.AsSpan(bomLen);

            if (RgCore.ContainsNullByte(dataSpan))
                return null;

            if (RgCore.IsAscii(dataSpan)) {
                var charSpan = MemoryMarshal.Cast<byte, char>(dataSpan);
                return SearchContent(path, charSpan, regex, q, ct);
            }

            var content = Encoding.UTF8.GetString(bytes, bomLen, bytes.Length - bomLen);
            return SearchContent(path, content.AsSpan(), regex, q, ct);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }

    private static RgFileResult? SearchContent(string path, ReadOnlySpan<char> contentSpan, Regex regex, RgQuery q, CancellationToken ct) {
        var lineRanges = LineSpanIndexer.BuildLineRanges(contentSpan, ct);

        var matchedLines = new List<int>();
        if (q.Multiline) {
            matchedLines = CollectMultilineMatches(contentSpan, regex, lineRanges, q);
        } else {
            matchedLines = CollectLineMatches(contentSpan, regex, lineRanges, q, ct);
        }

        if (matchedLines.Count == 0)
            return null;

        if (q.OutputMode == SearchOutputMode.Files)
            return new RgFileResult(path, matchedLines.Count, null);

        if (q.OutputMode == SearchOutputMode.Count)
            return new RgFileResult(path, matchedLines.Count, null);

        var contentLines = new List<string>();
        var context = q.Context ?? 0;
        foreach (var index in matchedLines) {
            var start = Math.Max(0, index - (q.Before ?? context));
            var end = Math.Min(lineRanges.Count, index + (q.After ?? context) + 1);

            for (var current = start; current < end; current++) {
                var (ls, ll) = lineRanges[current];
                var lineSpan = contentSpan.Slice(ls, ll);
                string lineContent;
                if (lineSpan.Length > MaxContentLineLength)
                    lineContent = string.Concat(lineSpan.Slice(0, MaxContentLineLength).ToString(), "...");
                else
                    lineContent = lineSpan.ToString();

                string formatted;
                if (q.OnlyMatching) {
                    var match = regex.Match(lineContent);
                    formatted = match.Success ? match.Value : lineContent;
                } else if (q.Replace is not null) {
                    formatted = regex.Replace(lineContent, q.Replace);
                } else {
                    formatted = lineContent;
                }

                var prefix = q.LineNumbers ? $"{path}:{current + 1}:" : $"{path}:";
                contentLines.Add($"{prefix}{formatted}");
            }
        }

        return new RgFileResult(path, matchedLines.Count, contentLines);
    }

    private static List<int> CollectMultilineMatches(
        ReadOnlySpan<char> contentSpan, Regex regex,
        List<(int Start, int Length)> lineRanges, RgQuery q) {
        var seen = new HashSet<int>();
        foreach (var m in regex.EnumerateMatches(contentSpan)) {
            var lineIdx = LineSpanIndexer.FindLineIndex(lineRanges, m.Index);
            if (lineIdx >= 0)
                seen.Add(lineIdx);
        }

        var result = new List<int>();
        if (!q.InvertMatch) {
            foreach (var lineIdx in seen.OrderBy(x => x)) {
                result.Add(lineIdx);
                if (q.MaxCount is int mc && result.Count >= mc)
                    break;
            }
            return result;
        }

        for (var i = 0; i < lineRanges.Count; i++) {
            if (seen.Contains(i) || lineRanges[i].Length == 0)
                continue;
            result.Add(i);
            if (q.MaxCount is int mc && result.Count >= mc)
                break;
        }
        return result;
    }

    private const long LineParallelThresholdBytes = 1024 * 1024;
    private const int LineParallelThresholdLines = 1000;

    private static List<int> CollectLineMatches(
        ReadOnlySpan<char> contentSpan, Regex regex,
        List<(int Start, int Length)> lineRanges, RgQuery q, CancellationToken ct) {
        if (contentSpan.Length >= LineParallelThresholdBytes && lineRanges.Count >= LineParallelThresholdLines)
            return CollectLineMatchesParallel(contentSpan, regex, lineRanges, q, ct);

        var matchedLines = new List<int>();
        var fastFixed = RgCore.CanUseFastFixedString(q.FixedStrings, q.CaseInsensitive, q.WordRegexp, q.SmartCase);
        var patternSpan = fastFixed ? q.Pattern.AsSpan() : default;

        for (var i = 0; i < lineRanges.Count; i++) {
            ct.ThrowIfCancellationRequested();
            var (s, l) = lineRanges[i];
            if (l == 0)
                continue;

            if (RgCore.IsLineMatch(contentSpan.Slice(s, l), regex, patternSpan, fastFixed, q.InvertMatch))
                matchedLines.Add(i);

            if (q.MaxCount is int mc && matchedLines.Count >= mc)
                break;
        }
        return matchedLines;
    }

    private static List<int> CollectLineMatchesParallel(
        ReadOnlySpan<char> contentSpan, Regex regex,
        List<(int Start, int Length)> lineRanges, RgQuery q, CancellationToken ct) {
        var content = contentSpan.ToString();
        var fastFixed = RgCore.CanUseFastFixedString(q.FixedStrings, q.CaseInsensitive, q.WordRegexp, q.SmartCase);
        var pattern = q.Pattern;
        var degree = q.Threads is > 0 ? q.Threads.Value : Environment.ProcessorCount;
        var chunkSize = Math.Max(1000, lineRanges.Count / degree);

        var chunks = new List<(int Start, int End)>();
        for (var start = 0; start < lineRanges.Count; start += chunkSize)
            chunks.Add((start, Math.Min(start + chunkSize, lineRanges.Count)));

        var results = chunks
            .AsParallel()
            .WithCancellation(ct)
            .WithDegreeOfParallelism(degree)
            .Select(chunk => {
                var chunkMatches = new List<int>();
                var span = content.AsSpan();
                var patternSpan = pattern.AsSpan();

                for (var i = chunk.Start; i < chunk.End; i++) {
                    var (s, l) = lineRanges[i];
                    if (l == 0)
                        continue;

                    if (RgCore.IsLineMatch(span.Slice(s, l), regex, patternSpan, fastFixed, q.InvertMatch))
                        chunkMatches.Add(i);
                }

                return (chunk.Start, chunkMatches);
            })
            .OrderBy(r => r.Start)
            .ToList();

        var allMatches = results.SelectMany(r => r.chunkMatches).ToList();

        if (q.MaxCount is int mc && allMatches.Count > mc)
            allMatches = allMatches.Take(mc).ToList();

        return allMatches;
    }

    private static List<RgFileResult> ApplySort(ConcurrentBag<RgFileResult> results, string? sort) {
        var list = results.ToList();
        if (string.IsNullOrEmpty(sort))
            return list;

        return sort switch {
            "path" => list.OrderBy(r => r.FilePath, StringComparer.Ordinal).ToList(),
            "modified" => list.OrderBy(r => File.GetLastWriteTime(r.FilePath)).ToList(),
            "accessed" => list.OrderBy(r => File.GetLastAccessTime(r.FilePath)).ToList(),
            "created" => list.OrderBy(r => File.GetCreationTime(r.FilePath)).ToList(),
            "none" => list,
            _ => list,
        };
    }

    private static (List<RgFileResult> Items, int? AppliedLimit, int? AppliedOffset) ApplyPaging(
        List<RgFileResult> items, int? headLimit, int? offset) {
        var offsetValue = offset ?? 0;
        var result = items.Skip(offsetValue).ToList();

        var limit = headLimit ?? 250;
        if (limit == 0)
            return (result, null, offsetValue > 0 ? offsetValue : null);

        var truncated = result.Count > limit;
        if (truncated)
            result = result.Take(limit).ToList();

        return (result, truncated ? limit : null, offsetValue > 0 ? offsetValue : null);
    }
}

/// <summary>搜索查询参数</summary>
internal sealed record RgQuery(
    string Pattern,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string>? Globs,
    string? FileType,
    bool CaseInsensitive,
    bool SmartCase,
    bool WordRegexp,
    bool OnlyMatching,
    string? Replace,
    bool Multiline,
    bool FixedStrings,
    bool Hidden,
    bool NoIgnore,
    int? Before,
    int? After,
    int? Context,
    bool LineNumbers,
    int? HeadLimit,
    int? Offset,
    SearchOutputMode OutputMode,
    string? Sort,
    bool InvertMatch,
    bool LineRegexp,
    int? MaxCount,
    int? Threads,
    long? MaxFilesize);

/// <summary>单文件搜索结果</summary>
internal sealed record RgFileResult(string FilePath, int MatchCount, IReadOnlyList<string>? ContentLines);

/// <summary>搜索结果</summary>
internal sealed record RgOutcome(
    IReadOnlyList<RgFileResult> Results,
    int TotalMatches,
    int? AppliedLimit,
    int? AppliedOffset,
    bool Success,
    string? Error) {
    /// <summary>构造搜索成功结果，自动汇总所有文件的匹配总数。</summary>
    public static RgOutcome Ok(IReadOnlyList<RgFileResult> results, int? limit, int? offset)
        => new(results, results.Sum(r => r.MatchCount), limit, offset, true, null);

    /// <summary>构造空结果（无匹配文件），表示搜索成功但未命中。</summary>
    public static RgOutcome Empty()
        => new(Array.Empty<RgFileResult>(), 0, null, null, true, null);

    /// <summary>构造搜索失败结果，携带错误信息。</summary>
    public static RgOutcome Failure(string error)
        => new(Array.Empty<RgFileResult>(), 0, null, null, false, error);
}