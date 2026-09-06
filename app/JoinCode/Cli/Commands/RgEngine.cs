namespace JoinCode.CliCommands;

/// <summary>
/// 高性能 ripgrep 兼容搜索引擎 — mmap + PLINQ 并行 + 零 GC 行遍历。
/// <para>ADR: 0070 — 独立于 ISearchService，直接用 MemoryMappedFile 零拷贝读取大文件，
/// PLINQ 链式组织并行搜索，Regex.IsMatch(ReadOnlySpan&lt;char&gt;) 零分配匹配。</para>
/// </summary>
internal static class RgEngine
{
    private const long MmapThresholdBytes = 64 * 1024;
    private const int MaxContentLineLength = 500;
    private const int BinaryDetectionBufferSize = 8192;

    private static readonly FrozenSet<string> VcsDirectories = FrozenSet.ToFrozenSet(
        [".git", ".svn", ".hg", ".bzr", ".jj", ".sl"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> BinaryExtensions = FrozenSet.ToFrozenSet(
        [
            ".exe", ".dll", ".so", ".dylib", ".a", ".lib", ".o", ".obj",
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
            ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv", ".flv", ".wmv",
            ".zip", ".tar", ".gz", ".bz2", ".xz", ".7z", ".rar", ".cab",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".class", ".jar", ".war", ".ear", ".dex", ".apk", ".ipa",
            ".woff", ".woff2", ".ttf", ".otf", ".eot",
            ".pyc", ".pyd", ".pyo",
            ".nupkg", ".snupkg", ".pdb", ".mdb",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string[]> FileTypeExtensions = FrozenDictionary.ToFrozenDictionary(
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["js"] = [".js", ".jsx", ".mjs", ".cjs"],
            ["ts"] = [".ts", ".tsx", ".mts", ".cts"],
            ["py"] = [".py", ".pyi"],
            ["rust"] = [".rs"],
            ["go"] = [".go"],
            ["java"] = [".java"],
            ["c"] = [".c", ".h"],
            ["cpp"] = [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx"],
            ["csharp"] = [".cs"],
            ["cs"] = [".cs"],
            ["ruby"] = [".rb", ".erb"],
            ["swift"] = [".swift"],
            ["kotlin"] = [".kt", ".kts"],
            ["scala"] = [".scala"],
            ["html"] = [".html", ".htm"],
            ["css"] = [".css", ".scss", ".sass", ".less"],
            ["json"] = [".json"],
            ["yaml"] = [".yaml", ".yml"],
            ["xml"] = [".xml", ".xsl", ".xsd"],
            ["markdown"] = [".md", ".mdx"],
            ["md"] = [".md", ".mdx"],
            ["sh"] = [".sh", ".bash", ".zsh"],
            ["powershell"] = [".ps1", ".psm1"],
            ["sql"] = [".sql"],
            ["toml"] = [".toml"],
            ["ini"] = [".ini", ".cfg", ".conf"],
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 执行搜索。PLINQ 链式：收集文件 → 并行搜索 → 过滤 → 排序 → 分页。
    /// </summary>
    public static RgOutcome Search(RgQuery q, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var regex = CompileRegex(q);
        if (regex is null)
            return RgOutcome.Failure("无效正则表达式");

        var files = CollectFiles(q, ct);
        if (files.Count == 0)
            return RgOutcome.Empty();

        var parallelOpts = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        var results = new ConcurrentBag<RgFileResult>();

        files.AsParallel()
            .WithCancellation(ct)
            .WithDegreeOfParallelism(Environment.ProcessorCount)
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
    private static Regex? CompileRegex(RgQuery q)
    {
        var pattern = q.Pattern;
        if (q.FixedStrings)
            pattern = Regex.Escape(pattern);
        if (q.WordRegexp)
            pattern = $@"\b(?:{pattern})\b";

        var options = RegexOptions.Compiled;
        if (q.Multiline)
            options |= RegexOptions.Singleline;

        var ignoreCase = q.CaseInsensitive;
        if (q.SmartCase && !pattern.Any(char.IsUpper))
            ignoreCase = true;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;

        try
        {
            return new Regex(pattern, options);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 收集搜索文件。遵守 .gitignore（除非 NoIgnore）、排除 VCS、二进制扩展名。
    /// </summary>
    private static IReadOnlyList<string> CollectFiles(RgQuery q, CancellationToken ct)
    {
        var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in q.Paths)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
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

    private static IEnumerable<string> EnumerateFiles(string root, RgQuery q, CancellationToken ct)
    {
        var enumeration = Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = q.Hidden ? 0 : FileAttributes.Hidden,
        });

        foreach (var file in enumeration)
        {
            ct.ThrowIfCancellationRequested();

            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (IsVcsPath(rel))
                continue;

            if (!q.NoIgnore && IsGitIgnored(root, rel))
                continue;

            if (!MatchesGlob(rel, q.Glob))
                continue;

            if (!MatchesFileType(file, q.FileType))
                continue;

            if (IsBinaryByExtension(file))
                continue;

            yield return file;
        }
    }

    private static bool IsVcsPath(string rel)
    {
        foreach (var vcs in VcsDirectories)
            if (rel.Contains($"/{vcs}/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith($"{vcs}/", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsGitIgnored(string root, string rel)
    {
        var gitPath = Path.Combine(root, ".git");
        if (!Directory.Exists(gitPath))
            return false;

        var gitignorePath = Path.Combine(root, ".gitignore");
        if (!File.Exists(gitignorePath))
            return false;

        try
        {
            var lines = File.ReadAllLines(gitignorePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;
                if (trimmed.StartsWith('!'))
                    continue;

                var pattern = trimmed.Replace('/', '\\');
                if (rel.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Diag.WriteLine($"[RgEngine.IsGitIgnored] 读取 .gitignore 失败: {ex.Message}");
        }
        return false;
    }

    private static bool MatchesGlob(string rel, string? glob)
    {
        if (string.IsNullOrEmpty(glob))
            return true;

        var normalizedGlob = glob.Replace('\\', '/');
        if (normalizedGlob.StartsWith('!'))
        {
            var exclude = normalizedGlob[1..];
            return !SimpleGlobMatch(rel, exclude);
        }
        return SimpleGlobMatch(rel, normalizedGlob);
    }

    private static bool SimpleGlobMatch(string path, string pattern)
    {
        if (pattern == "**/*" || pattern == "*")
            return true;

        if (pattern.Contains("**/"))
        {
            var suffix = pattern["**/".Length..];
            return path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                   path.Contains($"/{suffix}", StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return path.Contains(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return path.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFileType(string file, string? fileType)
    {
        if (string.IsNullOrEmpty(fileType))
            return true;

        var ext = Path.GetExtension(file);
        if (FileTypeExtensions.TryGetValue(fileType, out var exts))
            return Array.IndexOf(exts, ext) >= 0;

        return string.Equals(ext, $".{fileType}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBinaryByExtension(string file)
    {
        var ext = Path.GetExtension(file);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    /// <summary>
    /// 搜索单个文件。大文件用 mmap 零拷贝，小文件用 ReadAllBytes。
    /// 用 ReadOnlySpan&lt;char&gt; 遍历行，Regex.IsMatch(span) 零分配匹配。
    /// </summary>
    private static RgFileResult? SearchFile(string path, Regex regex, RgQuery q, CancellationToken ct)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0)
                return null;

            string content;
            if (fileInfo.Length >= MmapThresholdBytes)
            {
                content = ReadViaMmap(path);
            }
            else
            {
                content = File.ReadAllText(path);
            }

            if (ContainsNullByte(content))
                return null;

            return SearchContent(path, content, regex, q, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// mmap 零拷贝读取文件内容。
    /// </summary>
    private static string ReadViaMmap(string path)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        var length = (int)accessor.Capacity;
        var bytes = new byte[length];
        accessor.ReadArray(0, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool ContainsNullByte(string content)
    {
        var sampleLen = Math.Min(content.Length, BinaryDetectionBufferSize);
        for (var i = 0; i < sampleLen; i++)
        {
            if (content[i] == '\0')
                return true;
        }
        return false;
    }

    /// <summary>
    /// 搜索文件内容。Span 零分配遍历行，Regex.IsMatch(span) 匹配。
    /// </summary>
    private static RgFileResult? SearchContent(string path, string content, Regex regex, RgQuery q, CancellationToken ct)
    {
        var contentSpan = content.AsSpan();
        var lineRanges = new List<(int Start, int Length)>();
        var pos = 0;
        while (pos <= contentSpan.Length)
        {
            ct.ThrowIfCancellationRequested();

            var nlIdx = pos < contentSpan.Length
                ? contentSpan.Slice(pos).IndexOf('\n')
                : -1;
            if (nlIdx < 0)
            {
                lineRanges.Add((pos, contentSpan.Length - pos));
                break;
            }
            var lineStart = pos;
            var lineLen = nlIdx;
            if (lineLen > 0 && contentSpan[lineStart + lineLen - 1] == '\r')
                lineLen--;
            lineRanges.Add((lineStart, lineLen));
            pos += nlIdx + 1;
        }

        var matchedLines = new List<int>();
        for (var i = 0; i < lineRanges.Count; i++)
        {
            var (s, l) = lineRanges[i];
            if (l == 0)
                continue;
            if (regex.IsMatch(contentSpan.Slice(s, l)))
                matchedLines.Add(i);
        }

        if (matchedLines.Count == 0)
            return null;

        if (q.OutputMode == SearchOutputMode.Files)
            return new RgFileResult(path, matchedLines.Count, null);

        if (q.OutputMode == SearchOutputMode.Count)
            return new RgFileResult(path, matchedLines.Count, null);

        var contentLines = new List<string>();
        var context = q.Context ?? 0;
        foreach (var index in matchedLines)
        {
            var start = Math.Max(0, index - (q.Before ?? context));
            var end = Math.Min(lineRanges.Count, index + (q.After ?? context) + 1);

            for (var current = start; current < end; current++)
            {
                var (ls, ll) = lineRanges[current];
                var lineSpan = contentSpan.Slice(ls, ll);
                string lineContent;
                if (lineSpan.Length > MaxContentLineLength)
                    lineContent = string.Concat(lineSpan.Slice(0, MaxContentLineLength).ToString(), "...");
                else
                    lineContent = lineSpan.ToString();

                string formatted;
                if (q.OnlyMatching)
                {
                    var match = regex.Match(lineContent);
                    formatted = match.Success ? match.Value : lineContent;
                }
                else if (q.Replace is not null)
                {
                    formatted = regex.Replace(lineContent, q.Replace);
                }
                else
                {
                    formatted = lineContent;
                }

                var prefix = q.LineNumbers ? $"{path}:{current + 1}:" : $"{path}:";
                contentLines.Add($"{prefix}{formatted}");
            }
        }

        return new RgFileResult(path, matchedLines.Count, contentLines);
    }

    private static List<RgFileResult> ApplySort(ConcurrentBag<RgFileResult> results, string? sort)
    {
        var list = results.ToList();
        if (string.IsNullOrEmpty(sort))
            return list;

        return sort switch
        {
            "path" => list.OrderBy(r => r.FilePath, StringComparer.Ordinal).ToList(),
            "modified" => list.OrderBy(r => File.GetLastWriteTime(r.FilePath)).ToList(),
            "accessed" => list.OrderBy(r => File.GetLastAccessTime(r.FilePath)).ToList(),
            "created" => list.OrderBy(r => File.GetCreationTime(r.FilePath)).ToList(),
            "none" => list,
            _ => list,
        };
    }

    private static (List<RgFileResult> Items, int? AppliedLimit, int? AppliedOffset) ApplyPaging(
        List<RgFileResult> items, int? headLimit, int? offset)
    {
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
    string? Glob,
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
    string? Sort);

/// <summary>单文件搜索结果</summary>
internal sealed record RgFileResult(string FilePath, int MatchCount, IReadOnlyList<string>? ContentLines);

/// <summary>搜索结果</summary>
internal sealed record RgOutcome(
    IReadOnlyList<RgFileResult> Results,
    int TotalMatches,
    int? AppliedLimit,
    int? AppliedOffset,
    bool Success,
    string? Error)
{
    public static RgOutcome Ok(IReadOnlyList<RgFileResult> results, int? limit, int? offset)
        => new(results, results.Sum(r => r.MatchCount), limit, offset, true, null);

    public static RgOutcome Empty()
        => new(Array.Empty<RgFileResult>(), 0, null, null, true, null);

    public static RgOutcome Failure(string error)
        => new(Array.Empty<RgFileResult>(), 0, null, null, false, error);
}
