
namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// Search service implementation - provides Glob and Grep search capabilities
/// </summary>
[Register]
public sealed partial class SearchService : ISearchService
{
    [Inject] private readonly ILogger<SearchService>? _logger;
    [Inject] private readonly IFileOperationService _fileOperationService;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IFileSystem _fs;

    /// <inheritdoc />
    public async Task<GlobSearchResult> GlobSearchAsync(
        string pattern,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var span = _telemetryService?.StartSpan("search.glob", TelemetrySpanKind.Server);
            span?.SetTag("pattern", pattern);

            try
            {
                var baseDir = string.IsNullOrEmpty(path)
                    ? _fileOperationService.GetCurrentDirectory()
                    : _fileOperationService.GetFullPath(path);

                if (!_fileOperationService.DirectoryExists(baseDir))
                {
                    RecordSearchMetrics("glob", stopwatch.ElapsedMilliseconds, false);
                    return GlobSearchResult.FailureResult($"Directory does not exist: {baseDir}");
                }

                // 对齐 TS extractGlobBaseDirectory: 从绝对路径模式中提取静态基目录
                // ripgrep 的 --glob 只接受相对模式，Matcher 同理
                var (effectiveBaseDir, relativePattern) = ExtractGlobBaseDirectory(pattern, baseDir);

                if (!_fileOperationService.DirectoryExists(effectiveBaseDir))
                {
                    RecordSearchMetrics("glob", stopwatch.ElapsedMilliseconds, false);
                    return GlobSearchResult.FailureResult($"Directory does not exist: {effectiveBaseDir}");
                }

                // Expand brace patterns {a,b,c} — Matcher 不支持大括号，需手动展开
                var expandedPatterns = ExpandBraces(relativePattern);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var matches = new List<string>();

                // 优化：多个展开模式共享一次文件枚举（对齐 ripgrep 单次扫描行为）
                // 先构建所有 Matcher，再统一枚举一次
                var matchers = new List<Matcher>(expandedPatterns.Count);
                foreach (var pat in expandedPatterns)
                {
                    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                    matcher.AddInclude(pat);

                    // 排除 VCS 目录（对齐 TS: .git/.svn/.hg/.bzr/.jj/.sl）
                    foreach (var vcsDir in VcsDirectoriesToExclude)
                    {
                        matcher.AddExclude($"**/{vcsDir}/**");
                    }

                    matchers.Add(matcher);
                }

                // 一次枚举所有文件，多个 Matcher 共享遍历
                var allFiles = _fileOperationService.EnumerateFiles(effectiveBaseDir, "*", SearchOption.AllDirectories);
                var normalizedBase = effectiveBaseDir.Replace('\\', '/').TrimStart('/');
                foreach (var filePath in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var normalizedFile = filePath.Replace('\\', '/').TrimStart('/');
                    var relativePath = normalizedFile.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase)
                        ? normalizedFile[normalizedBase.Length..].TrimStart('/')
                        : normalizedFile;

                    // 任意 Matcher 匹配即可（OR 语义，对齐 ripgrep 多 --glob 参数）
                    foreach (var matcher in matchers)
                    {
                        if (matcher.Match(relativePath).HasMatches)
                        {
                            if (seen.Add(filePath))
                            {
                                matches.Add(filePath);
                            }
                            break;
                        }
                    }
                }

                // Sort by modification time (oldest first, matching TS --sort=modified)
                matches = matches
                    .Select(f => new { Path = f, Time = _fileOperationService.GetFileLastWriteTime(f) })
                    .OrderBy(x => x.Time)
                    .Select(x => x.Path)
                    .ToList();

                var truncated = matches.Count > WorkflowConstants.Limits.DefaultSearchResultLimit;
                var resultFiles = matches.Take(WorkflowConstants.Limits.DefaultSearchResultLimit).ToList();

                stopwatch.Stop();

                _logger?.LogInformation(
                    "Glob search completed: {Pattern}, found {Count} files, duration {Duration}ms",
                    pattern,
                    resultFiles.Count,
                    stopwatch.ElapsedMilliseconds);

                RecordSearchMetrics("glob", stopwatch.ElapsedMilliseconds, true, resultFiles.Count);
                return GlobSearchResult.SuccessResult(
                    stopwatch.ElapsedMilliseconds,
                    resultFiles,
                    truncated);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Glob search failed: {Pattern}", pattern);
                RecordSearchMetrics("glob", stopwatch.ElapsedMilliseconds, false);
                return GlobSearchResult.FailureResult(ex.Message);
            }
            finally
            {
                span?.Dispose();
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GrepSearchResult> GrepSearchAsync(
        GrepSearchInput input,
        CancellationToken cancellationToken = default)
    {
        var span = _telemetryService?.StartSpan("search.grep", TelemetrySpanKind.Server);
        span?.SetTag("pattern", input.Pattern);
        try
        {
            // 入口处检查取消，对齐 TS ripgrep 超时取消行为
            cancellationToken.ThrowIfCancellationRequested();

            var basePath = string.IsNullOrEmpty(input.Path)
                ? _fileOperationService.GetCurrentDirectory()
                : _fileOperationService.GetFullPath(input.Path);

            if (!_fileOperationService.DirectoryExists(basePath) && !_fileOperationService.FileExists(basePath))
            {
                RecordSearchMetrics("grep", 0, false);
                return GrepSearchResult.FailureResult($"Path does not exist: {basePath}");
            }

            // Compile regex — 使用 Compiled 提升匹配性能（对齐 ripgrep 的高性能正则引擎）
            var regexOptions = RegexOptions.Compiled;
            if (input.CaseInsensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }
            if (input.Multiline)
            {
                regexOptions |= RegexOptions.Singleline;
            }

            Regex regex;
            try
            {
                regex = new Regex(input.Pattern, regexOptions);
            }
            catch (ArgumentException ex)
            {
                RecordSearchMetrics("grep", 0, false);
                return GrepSearchResult.FailureResult($"Invalid regular expression: {ex.Message}");
            }

            var filenames = new List<string>();
            var contentLines = new List<string>();
            var totalMatches = 0;
            var context = input.Context ?? 0;

            // Collect files to search
            var filesToSearch = CollectSearchFiles(basePath, input.Glob, input.FileType, input.DenyPatterns, cancellationToken);

            // Search files in parallel
            var searchTasks = filesToSearch.Select(async filePath =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!readResult.Success)
                    {
                        return null;
                    }
                    var fileContent = readResult.Content;

                    if (input.OutputMode == SearchOutputMode.Count)
                    {
                        var count = regex.Matches(fileContent).Count;
                        if (count > 0)
                        {
                            return new FileSearchResult(filePath, count, null);
                        }
                        return null;
                    }

                    var lines = fileContent.Split(['\n'], StringSplitOptions.None);
                    var matchedLines = new List<int>();

                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            matchedLines.Add(i);
                        }
                    }

                    if (matchedLines.Count == 0)
                    {
                        return null;
                    }

                    var fileContentLines = new List<string>();
                    if (input.OutputMode == SearchOutputMode.Content)
                    {
                        foreach (var index in matchedLines)
                        {
                            var start = Math.Max(0, index - (input.Before ?? context));
                            var end = Math.Min(lines.Length, index + (input.After ?? context) + 1);

                            for (var current = start; current < end; current++)
                            {
                                var lineContent = lines[current];
                                // Truncate long lines (aligned with TS --max-columns 500)
                                if (lineContent.Length > MaxContentLineLength)
                                {
                                    lineContent = string.Concat(lineContent.AsSpan(0, MaxContentLineLength), "...");
                                }
                                var prefix = input.LineNumbers
                                    ? $"{filePath}:{current + 1}:"
                                    : $"{filePath}:";
                                fileContentLines.Add($"{prefix}{lineContent}");
                            }
                        }
                    }

                    return new FileSearchResult(filePath, matchedLines.Count, fileContentLines);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read file: {FilePath}", filePath);
                    return null;
                }
            });

            var searchResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);

            foreach (var result in searchResults.Where(r => r != null))
            {
                filenames.Add(result!.FilePath);
                totalMatches += result.MatchCount;
                contentLines.AddRange(result.ContentLines ?? []);
            }

            // Sort files_with_matches by mtime (newest first, aligned with TS)
            if (input.OutputMode == SearchOutputMode.Files)
            {
                filenames = filenames
                    .Select(f => new { Path = f, Time = _fileOperationService.GetFileLastWriteTime(f) })
                    .OrderByDescending(x => x.Time)
                    .ThenBy(x => x.Path, StringComparer.Ordinal)
                    .Select(x => x.Path)
                    .ToList();
            }

                // Apply limit and offset
                var (filteredFilenames, appliedLimit, appliedOffset) = ApplyLimit(
                    filenames, input.HeadLimit, input.Offset);

                if (input.OutputMode == SearchOutputMode.Content)
                {
                    var (filteredLines, lineLimit, lineOffset) = ApplyLimit(
                        contentLines, input.HeadLimit, input.Offset);

                    RecordSearchMetrics("grep", 0, true, filenames.Count);
                    return GrepSearchResult.SuccessResult(
                        input.OutputMode.ToValue(),
                        filteredFilenames,
                        string.Join("\n", filteredLines),
                        filteredLines.Count,
                        null,
                        lineLimit,
                        lineOffset);
                }

                RecordSearchMetrics("grep", 0, true, filenames.Count);
                return GrepSearchResult.SuccessResult(
                    input.OutputMode.ToValue(),
                    filteredFilenames,
                    null,
                    null,
                    input.OutputMode == SearchOutputMode.Count ? totalMatches : null,
                    appliedLimit,
                    appliedOffset);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Grep search failed: {Pattern}", input.Pattern);
                RecordSearchMetrics("grep", 0, false);
                return GrepSearchResult.FailureResult(ex.Message);
            }
            finally
            {
                span?.Dispose();
            }
    }

    private void RecordSearchMetrics(string kind, long elapsedMs, bool isSuccess, int resultCount = 0)
    {
        _telemetryService?.RecordCount("search.operation.count", new Dictionary<string, string> { ["kind"] = kind, ["success"] = isSuccess.ToString() }, "count", "Search operation count");
        if (isSuccess && elapsedMs > 0)
            _telemetryService?.RecordHistogram("search.operation.duration", elapsedMs, new Dictionary<string, string> { ["kind"] = kind }, "ms", "Search operation duration");
    }

    #region Private Methods

    /// <summary>
    /// 从 glob 模式中提取静态基目录和相对模式
    /// 对齐 TS extractGlobBaseDirectory: 找到第一个 glob 特殊字符（* ? [ {），
    /// 其之前的静态路径部分作为基目录，剩余部分作为相对模式
    /// </summary>
    private static (string BaseDir, string RelativePattern) ExtractGlobBaseDirectory(string pattern, string defaultBaseDir)
    {
        if (!Path.IsPathFullyQualified(pattern))
        {
            return (defaultBaseDir, pattern);
        }

        // 找到第一个 glob 特殊字符的位置
        var globChars = pattern.AsSpan();
        var firstGlobPos = -1;
        for (var i = 0; i < globChars.Length; i++)
        {
            var c = globChars[i];
            if (c is '*' or '?' or '[' or '{')
            {
                firstGlobPos = i;
                break;
            }
        }

        if (firstGlobPos == -1)
        {
            // 无 glob 特殊字符，是精确路径
            return (pattern, "*");
        }

        // 找到第一个 glob 特殊字符之前的最后一个目录分隔符
        var lastSepPos = -1;
        for (var i = firstGlobPos - 1; i >= 0; i--)
        {
            if (globChars[i] is '/' or '\\')
            {
                lastSepPos = i;
                break;
            }
        }

        if (lastSepPos == -1)
        {
            // 无目录分隔符，使用默认基目录
            return (defaultBaseDir, pattern);
        }

        var baseDir = pattern[..lastSepPos];
        var relativePattern = pattern[(lastSepPos + 1)..];

        // 规范化路径分隔符
        relativePattern = relativePattern.Replace('\\', '/');

        return (baseDir, relativePattern);
    }

    private static IReadOnlyList<string> ExpandBraces(string pattern)
    {
        var openIndex = pattern.IndexOf('{');
        if (openIndex == -1)
        {
            return new List<string> { pattern };
        }

        var closeIndex = pattern.IndexOf('}', openIndex);
        if (closeIndex == -1)
        {
            // Unmatched brace, treat as literal
            return new List<string> { pattern };
        }

        var prefix = pattern[..openIndex];
        var suffix = pattern[(closeIndex + 1)..];
        var alternatives = pattern[(openIndex + 1)..closeIndex].Split(',');

        var results = new List<string>();
        foreach (var alt in alternatives)
        {
            var expanded = ExpandBraces($"{prefix}{alt}{suffix}");
            results.AddRange(expanded);
        }

        return results;
    }

    // VCS directories to exclude from searches (aligned with TS GrepTool)
    private static readonly FrozenSet<string> VcsDirectoriesToExclude = FrozenSet.ToFrozenSet(
        [".git", ".svn", ".hg", ".bzr", ".jj", ".sl"],
        StringComparer.OrdinalIgnoreCase);

    // Maximum line length for grep content output (aligned with TS --max-columns 500)
    private const int MaxContentLineLength = 500;

    // 二进制检测缓冲区大小（对齐 ripgrep 的 8KB 采样窗口）
    private const int BinaryDetectionBufferSize = 8192;

    // 已知二进制文件扩展名（对齐 ripgrep 内置类型映射）
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

    /// <summary>
    /// 文件类型到扩展名的映射表，对齐 ripgrep --type 内置映射
    /// ripgrep 通过 --type 选项支持预定义的文件类型过滤
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> FileTypeExtensions = FrozenDictionary.ToFrozenDictionary(
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["js"] = ["*.js", "*.jsx", "*.mjs", "*.cjs"],
            ["ts"] = ["*.ts", "*.tsx", "*.mts", "*.cts"],
            ["py"] = ["*.py", "*.pyi"],
            ["rust"] = ["*.rs"],
            ["go"] = ["*.go"],
            ["java"] = ["*.java"],
            ["c"] = ["*.c", "*.h"],
            ["cpp"] = ["*.cpp", "*.cc", "*.cxx", "*.hpp", "*.hh", "*.hxx"],
            ["csharp"] = ["*.cs"],
            ["ruby"] = ["*.rb", "*.erb"],
            ["swift"] = ["*.swift"],
            ["kotlin"] = ["*.kt", "*.kts"],
            ["scala"] = ["*.scala"],
            ["html"] = ["*.html", "*.htm"],
            ["css"] = ["*.css", "*.scss", "*.sass", "*.less"],
            ["json"] = ["*.json"],
            ["yaml"] = ["*.yaml", "*.yml"],
            ["xml"] = ["*.xml", "*.xsl", "*.xsd"],
            ["markdown"] = ["*.md", "*.mdx"],
            ["sh"] = ["*.sh", "*.bash", "*.zsh"],
            ["powershell"] = ["*.ps1", "*.psm1"],
            ["sql"] = ["*.sql"],
            ["dockerfile"] = ["Dockerfile", "*.dockerfile"],
            ["toml"] = ["*.toml"],
            ["ini"] = ["*.ini", "*.cfg", "*.conf"],
        },
        StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<string> CollectSearchFiles(string basePath, string? globFilter, string? fileType, IReadOnlyList<string>? denyPatterns = null, CancellationToken cancellationToken = default)
    {
        if (_fileOperationService.FileExists(basePath))
        {
            return [basePath];
        }

        if (!_fileOperationService.DirectoryExists(basePath))
        {
            return [];
        }

        // 加载 .gitignore 规则（对齐 ripgrep 默认遵守 .gitignore 行为）
        // 注意：GlobTool 不遵守 .gitignore（对齐 TS CLAUDE_CODE_GLOB_NO_IGNORE=true）
        // 但 GrepTool 需要遵守（对齐 TS ripgrep 默认行为）
        var gitignoreMatcher = LoadGitignoreMatchers(basePath);

        // 使用 Matcher 收集文件，支持完整 glob 模式
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        // 添加 include 模式
        if (!string.IsNullOrEmpty(globFilter))
        {
            // 展开大括号后添加
            foreach (var expanded in ExpandBraces(globFilter.Replace('\\', '/')))
            {
                matcher.AddInclude(expanded);
            }
        }

        if (!string.IsNullOrEmpty(fileType))
        {
            // 对齐 ripgrep --type: 使用预定义的文件类型扩展名映射
            // 当 glob 和 type 同时存在时，ripgrep 是 AND 逻辑
            if (FileTypeExtensions.TryGetValue(fileType, out var extensions))
            {
                foreach (var ext in extensions)
                {
                    matcher.AddInclude($"**/{ext}");
                }
            }
            else
            {
                // 未知类型，回退到简单扩展名匹配
                matcher.AddInclude($"**/*.{fileType}");
            }
        }

        if (string.IsNullOrEmpty(globFilter) && string.IsNullOrEmpty(fileType))
        {
            matcher.AddInclude("**/*");
        }

        // 排除 VCS 目录
        foreach (var vcsDir in VcsDirectoriesToExclude)
        {
            matcher.AddExclude($"**/{vcsDir}/**");
        }

        // 使用 IFileOperationService 枚举文件（支持内存文件系统），然后用 Matcher.Match 过滤
        var allFiles = _fileOperationService.EnumerateFiles(basePath, "*", SearchOption.AllDirectories);
        var files = new List<string>();

        // 如果有 file type 但也有 glob，需要交集过滤
        // Matcher 的多个 AddInclude 是 OR 逻辑，但 ripgrep 的 --glob + --type 是 AND 逻辑
        var needsAndFilter = !string.IsNullOrEmpty(fileType) && !string.IsNullOrEmpty(globFilter);
        var typeExtensions = needsAndFilter
            ? (FileTypeExtensions.TryGetValue(fileType!, out var exts)
                ? exts.Select(e => e.Replace("*", "").TrimStart('.').ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [fileType!])
            : null;

        // 预计算规范化基路径（循环外，避免重复计算）
        var normalizedBase = basePath.Replace('\\', '/').TrimStart('/');

        foreach (var filePath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 统一路径分隔符为 /，确保相对路径计算正确
            var normalizedFile = filePath.Replace('\\', '/').TrimStart('/');
            var relativePath = normalizedFile.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase)
                ? normalizedFile[normalizedBase.Length..].TrimStart('/')
                : normalizedFile;

            if (matcher.Match(relativePath).HasMatches)
            {
                if (needsAndFilter && typeExtensions is not null)
                {
                    var ext = Path.GetExtension(filePath).TrimStart('.');
                    if (!typeExtensions.Contains(ext))
                        continue;
                }

                // 跳过二进制文件（对齐 ripgrep 自动跳过二进制文件行为）
                if (IsBinaryFile(filePath))
                    continue;

                // 跳过 .gitignore 忽略的文件（对齐 ripgrep 默认行为）
                if (IsIgnoredByGitignore(gitignoreMatcher, filePath, basePath))
                    continue;

                // 跳过 Read deny 规则排除的文件（对齐 TS getFileReadIgnorePatterns → ripgrep --glob !pattern）
                if (IsDeniedByPatterns(filePath, basePath, denyPatterns))
                    continue;

                files.Add(filePath);
            }
        }

        return files;
    }

    /// <summary>
    /// 加载搜索路径及其父目录中的 .gitignore 匹配器
    /// 对齐 ripgrep 行为：从搜索目录向上查找到仓库根目录，收集所有 .gitignore
    /// </summary>
    private List<GitignoreMatcher> LoadGitignoreMatchers(string searchPath)
    {
        var matchers = new List<GitignoreMatcher>();

        // 内存文件系统路径（非 Windows 绝对路径）无法使用 File.Exists/Directory.Exists
        if (!Path.IsPathFullyQualified(searchPath))
            return matchers;

        var currentDir = searchPath;

        // 向上查找 .gitignore，最多 20 层（防止无限循环）
        for (var i = 0; i < 20; i++)
        {
            var gitignorePath = Path.Combine(currentDir, ".gitignore");
            var matcher = GitignoreMatcher.FromFile(gitignorePath, _fs);
            if (matcher is not null)
            {
                matchers.Add(matcher);
            }

            // 如果到达 .git 目录，停止向上查找
            if (_fs.DirectoryExists(Path.Combine(currentDir, ".git")))
                break;

            var parent = Path.GetDirectoryName(currentDir);
            if (string.IsNullOrEmpty(parent) || parent == currentDir)
                break;

            currentDir = parent;
        }

        // 反转顺序：根目录的 .gitignore 优先级最低，最靠近文件的优先级最高
        matchers.Reverse();
        return matchers;
    }

    /// <summary>
    /// 检查文件是否被 .gitignore 规则忽略
    /// </summary>
    private static bool IsIgnoredByGitignore(List<GitignoreMatcher> matchers, string filePath, string basePath)
    {
        if (matchers.Count == 0)
            return false;

        // 计算相对路径用于匹配
        var normalizedFile = filePath.Replace('\\', '/');
        var normalizedBase = basePath.Replace('\\', '/').TrimEnd('/');
        var relativePath = normalizedFile.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase)
            ? normalizedFile[(normalizedBase.Length + 1)..]
            : normalizedFile;

        foreach (var matcher in matchers)
        {
            if (matcher.IsIgnored(relativePath))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查文件是否被 Read deny 规则排除 — 对齐 TS getFileReadIgnorePatterns
    /// deny 模式已规范化为相对路径，匹配时将文件路径也转为相对路径
    /// </summary>
    private static bool IsDeniedByPatterns(string filePath, string basePath, IReadOnlyList<string>? denyPatterns)
    {
        if (denyPatterns is null || denyPatterns.Count == 0)
            return false;

        var normalizedFile = filePath.Replace('\\', '/');
        var normalizedBase = basePath.Replace('\\', '/').TrimEnd('/');
        var relativePath = normalizedFile.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase)
            ? normalizedFile[(normalizedBase.Length + 1)..]
            : normalizedFile;

        for (var i = 0; i < denyPatterns.Count; i++)
        {
            var pattern = denyPatterns[i];
            // 对齐 TS: 绝对模式直接匹配路径，相对模式匹配任意深度
            if (relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                normalizedFile.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检测文件是否为二进制文件
    /// 对齐 ripgrep 行为：先检查扩展名白名单，再采样前 8KB 检测 null 字节和非打印字符
    /// </summary>
    private bool IsBinaryFile(string filePath)
    {
        // 快速路径：已知二进制扩展名直接跳过
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext))
        {
            return true;
        }

        // 内存文件系统路径（非 Windows 绝对路径）无法使用 FileStream，跳过内容检测
        if (!Path.IsPathFullyQualified(filePath) || !_fs.FileExists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[BinaryDetectionBufferSize];
            var bytesRead = stream.Read(buffer);

            if (bytesRead == 0) return false;

            var nonPrintableCount = 0;
            for (var i = 0; i < bytesRead; i++)
            {
                var b = buffer[i];
                // Null 字节始终视为二进制（对齐 ripgrep）
                if (b == 0) return true;
                // 统计非打印字符（排除 TAB=9, LF=10, CR=13）
                if (b < 0x20 && b is not (9 or 10 or 13))
                {
                    nonPrintableCount++;
                }
            }

            // 超过 10% 非打印字符视为二进制
            return nonPrintableCount > bytesRead / 10;
        }
        catch
        {
            return true;
        }
    }

    private static (List<T> Items, int? AppliedLimit, int? AppliedOffset) ApplyLimit<T>(
        List<T> items,
        int? headLimit,
        int? offset)
    {
        var offsetValue = offset ?? 0;
        var result = items.Skip(offsetValue).ToList();

        var explicitLimit = headLimit ?? WorkflowConstants.Limits.DefaultGrepResultLimit;
        if (explicitLimit == 0)
        {
            return (result, null, offsetValue > 0 ? offsetValue : null);
        }

        var truncated = result.Count > explicitLimit;
        if (truncated)
        {
            result = result.Take(explicitLimit).ToList();
        }

        return (result, truncated ? explicitLimit : null, offsetValue > 0 ? offsetValue : null);
    }

    #endregion

    /// <summary>
    /// File search result
    /// </summary>
    private sealed record FileSearchResult(string FilePath, int MatchCount, List<string>? ContentLines);
}
