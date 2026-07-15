
namespace Core.Context.Resolution;

/// <summary>
/// 代码引用解析器实现
/// 集成 ISearchService 进行文件搜索，支持模糊匹配和精确匹配
/// </summary>
[Register(typeof(IReferenceResolver), JoinCode.Abstractions.Attributes.ServiceLifetime.Scoped)]
public sealed partial class ReferenceResolver : IReferenceResolver
{
    private readonly ISearchService _searchService;
    private readonly IFileOperationService _fileOperationService;
    private readonly ICodeIndexer? _codeIndexer;
    [Inject] private readonly ILogger<ReferenceResolver>? _logger;

    // 目录别名映射表
    private static readonly FrozenDictionary<string, string[]> DirectoryAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["工具"] = ["tools", "tool", "ToolHandlers", "Commands"],
        ["工具实现"] = ["tools", "ToolHandlers"],
        ["命令"] = ["commands", "command", "Commands"],
        ["服务"] = ["services", "service", "Services"],
        ["接口"] = ["interfaces", "interface", "Interfaces"],
        ["状态"] = ["state", "states", "StateService"],
        ["任务"] = ["tasks", "task", "Scheduling", "TaskService"],
        ["代理"] = ["agents", "agent", "Agents"],
        ["桥接"] = ["bridge", "Bridge"],
        ["组件"] = ["components", "component", "Components"],
        ["技能"] = ["skills", "skill", "Skills"],
        ["查询"] = ["query", "Query", "QueryEngine"],
        ["权限"] = ["permission", "permissions", "Permission"],
        ["配置"] = ["configuration", "config", "Configuration", "schemas"],
        ["Mcp"] = ["mcp", "Mcp"],
        ["测试"] = ["tests", "test", "Tests"],
        ["模型"] = ["models", "model", "Models"]
    }.ToFrozenDictionary();

    // 文件扩展名映射
    private static readonly FrozenDictionary<string, string[]> ExtensionPatterns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = ["*.cs"],
        [".ts"] = ["*.ts", "*.tsx"],
        [".js"] = ["*.js", "*.jsx"],
        [".py"] = ["*.py"],
        [".java"] = ["*.java"],
        [".go"] = ["*.go"],
        [".rs"] = ["*.rs"],
        [".cpp"] = ["*.cpp", "*.cc", "*.cxx"],
        [".c"] = ["*.c"],
        [".h"] = ["*.h", "*.hpp"],
        [".md"] = ["*.md"],
        [".json"] = ["*.json"],
        [".xml"] = ["*.xml"],
        [".yaml"] = ["*.yaml", "*.yml"]
    }.ToFrozenDictionary();

    public ReferenceResolver(ISearchService searchService, IFileOperationService fileOperationService, ICodeIndexer? codeIndexer = null, ILogger<ReferenceResolver>? logger = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _codeIndexer = codeIndexer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CodeReference> ResolveCodeReferenceAsync(
        string reference,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? ReferenceResolutionOptions.Default;
        var projectRoot = GetProjectRoot(opts);

        _logger?.LogInformation("解析代码引用: {Reference}", reference);

        try
        {
            // 0. 尝试 CodeIndex 符号搜索（优先级最高）
            if (_codeIndexer is not null)
            {
                var codeIndexResult = await TryCodeIndexSearchAsync(reference, opts, cancellationToken).ConfigureAwait(false);
                if (codeIndexResult != null && codeIndexResult.FileMatches.Count > 0)
                {
                    return codeIndexResult;
                }
            }

            // 1. 尝试精确匹配（作为完整路径）
            var exactPath = _fileOperationService.CombinePath(projectRoot, reference.Replace('/', Path.DirectorySeparatorChar));
            if (_fileOperationService.DirectoryExists(exactPath))
            {
                return await ResolveDirectoryReferenceAsync(reference, exactPath, opts, cancellationToken).ConfigureAwait(false);
            }
            if (_fileOperationService.FileExists(exactPath))
            {
                return ResolveFileReference(reference, exactPath, ReferenceMatchType.Exact);
            }

            // 2. 尝试作为 Glob 模式匹配
            var globResult = await TryGlobMatchAsync(reference, projectRoot, opts, cancellationToken).ConfigureAwait(false);
            if (globResult != null && globResult.FileMatches.Count > 0)
            {
                return globResult;
            }

            // 3. 尝试模糊匹配（解析路径各部分）
            if (opts.EnableFuzzyMatching)
            {
                var fuzzyResult = await TryFuzzyMatchAsync(reference, projectRoot, opts, cancellationToken).ConfigureAwait(false);
                if (fuzzyResult != null && fuzzyResult.FileMatches.Count > 0)
                {
                    return fuzzyResult;
                }
            }

            // 4. 尝试部分匹配
            var partialResult = await TryPartialMatchAsync(reference, projectRoot, opts, cancellationToken).ConfigureAwait(false);
            if (partialResult != null && partialResult.FileMatches.Count > 0)
            {
                return partialResult;
            }

            // 未找到匹配
            _logger?.LogWarning("无法解析代码引用: {Reference}", reference);
            return CodeReference.Unresolved(reference);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "解析代码引用时发生错误: {Reference}", reference);
            return CodeReference.Unresolved(reference);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeReference>> FindMatchingFilesAsync(
        string description,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? ReferenceResolutionOptions.Default;
        var projectRoot = GetProjectRoot(opts);
        var results = new List<CodeReference>();

        _logger?.LogInformation("根据描述查找文件: {Description}", description);

        try
        {
            // 0. 尝试 CodeIndex 符号搜索（优先级最高）
            if (_codeIndexer is not null)
            {
                var codeIndexResults = await TryCodeIndexFindMatchingAsync(description, opts, cancellationToken).ConfigureAwait(false);
                if (codeIndexResults.Count > 0)
                {
                    return codeIndexResults;
                }
            }

            // 1. 检查是否是目录别名（并行解析）
            if (DirectoryAliases.TryGetValue(description, out var aliases))
            {
                var aliasTasks = aliases.Select(alias => ResolveCodeReferenceAsync(alias, opts, cancellationToken));
                var aliasResults = await Task.WhenAll(aliasTasks).ConfigureAwait(false);
                results.AddRange(aliasResults.Where(r => r.IsResolved));
            }

            // 2. 尝试 Glob 搜索（并行执行）
            var globPatterns = InferGlobPatterns(description);
            var globTasks = globPatterns.Select(async pattern =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchResult = await _searchService.GlobSearchAsync(
                    pattern,
                    projectRoot,
                    cancellationToken).ConfigureAwait(false);

                if (searchResult.Success && searchResult.Filenames.Count > 0)
                {
                    var fileMatches = searchResult.Filenames
                        .Select(f => FileMatch.Create(f, ReferenceMatchType.Fuzzy, CalculateRelevanceScore(f, description)))
                        .Where(fm => fm.RelevanceScore >= opts.MinRelevanceScore)
                        .Take(opts.MaxResults)
                        .ToList();

                    if (fileMatches.Count > 0)
                    {
                        return new CodeReference
                        {
                            ReferencePath = description,
                            ResolvedPath = projectRoot,
                            MatchType = ReferenceMatchType.Fuzzy,
                            RelevanceScore = fileMatches.Average(fm => fm.RelevanceScore),
                            FileMatches = fileMatches
                        };
                    }
                }
                return null;
            });

            var globResults = await Task.WhenAll(globTasks).ConfigureAwait(false);
            results.AddRange(globResults.Where(r => r != null)!);

            // 3. 尝试 Grep 搜索（在文件内容中查找）
            if (results.Count == 0)
            {
                var grepResult = await TryGrepSearchAsync(description, projectRoot, opts, cancellationToken).ConfigureAwait(false);
                if (grepResult.Count > 0)
                {
                    results.AddRange(grepResult);
                }
            }

            return results.DistinctBy(r => r.ResolvedPath).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "查找匹配文件时发生错误: {Description}", description);
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<ReferenceIndex> BuildReferenceIndexAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var index = new ReferenceIndex(projectRoot);
        var stopwatch = Stopwatch.StartNew();

        _logger?.LogInformation("开始构建代码引用索引: {ProjectRoot}", projectRoot);

        try
        {
            var searchResult = await _searchService.GlobSearchAsync(
                "**/*",
                projectRoot,
                cancellationToken).ConfigureAwait(false);

            if (!searchResult.Success)
            {
                _logger?.LogWarning("构建索引时搜索失败: {Error}", searchResult.ErrorMessage);
                return index;
            }

            foreach (var filePath in searchResult.Filenames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 使用 IFileOperationService 检查文件存在，支持 InMemoryFileSystem
                    if (!_fileOperationService.FileExists(filePath)) continue;

                    var relativePath = Path.GetRelativePath(projectRoot, filePath);
                    var fileType = Path.GetExtension(filePath).TrimStart('.');
                    var keywords = ExtractKeywords(relativePath);

                    var lastWriteTimeUtc = await _fileOperationService
                        .GetLastWriteTimeUtcAsync(filePath, cancellationToken)
                        .ConfigureAwait(false);

                    // 获取文件大小：优先通过 ListDirectoryAsync 获取，回退到默认值 0
                    long fileSize = 0;
                    try
                    {
                        var dirPath = Path.GetDirectoryName(filePath) ?? projectRoot;
                        var fileName = Path.GetFileName(filePath);
                        var dirList = await _fileOperationService
                            .ListDirectoryAsync(dirPath, false, cancellationToken)
                            .ConfigureAwait(false);
                        var entry = dirList.Files.FirstOrDefault(f => f.Name == fileName);
                        fileSize = entry?.Size ?? 0;
                    }
                    catch (Exception ex)
                    {
                        // 无法获取文件大小时使用默认值
                        System.Diagnostics.Trace.WriteLine($"Failed to get file size for reference: {ex.Message}");
                    }

                    var indexedRef = IndexedReference.Create(
                        relativePath,
                        fileType,
                        keywords,
                        lastWriteTimeUtc,
                        fileSize);

                    index.AddReference(indexedRef);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "索引文件失败: {FilePath}", filePath);
                }
            }

            stopwatch.Stop();
            _logger?.LogInformation(
                "代码引用索引构建完成: {Count} 项, 耗时 {Duration}ms",
                index.Count,
                stopwatch.ElapsedMilliseconds);

            return index;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "构建代码引用索引时发生错误");
            return index;
        }
    }

    #region Private Methods

    private async Task<CodeReference?> TryCodeIndexSearchAsync(
        string reference,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var codeIndexer = _codeIndexer;
        if (codeIndexer is null) return null;
        try
        {
            var searchResult = await codeIndexer.Searcher.SearchAsync(reference, cancellationToken).ConfigureAwait(false);
            if (searchResult.Items.Count == 0)
            {
                return null;
            }

            var fileMatches = searchResult.Items
                .Select(s => FileMatch.Create(
                    s.FilePath,
                    ReferenceMatchType.Exact,
                    0.95,
                    $"CodeIndex: {s.Kind} {s.Name}"))
                .Take(opts.MaxResults)
                .ToList();

            return new CodeReference
            {
                ReferencePath = reference,
                ResolvedPath = searchResult.Items[0].FilePath,
                MatchType = ReferenceMatchType.Exact,
                RelevanceScore = 0.95,
                FileMatches = fileMatches
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "CodeIndex 搜索失败，降级到文件搜索");
            return null;
        }
    }

    private async Task<IReadOnlyList<CodeReference>> TryCodeIndexFindMatchingAsync(
        string description,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var codeIndexer = _codeIndexer;
        if (codeIndexer is null) return [];
        try
        {
            var searchResult = await codeIndexer.Searcher.SearchAsync(description, cancellationToken).ConfigureAwait(false);
            if (searchResult.Items.Count == 0)
            {
                return [];
            }

            var grouped = searchResult.Items
                .GroupBy(s => s.FilePath)
                .Take(opts.MaxResults)
                .ToList();

            var results = grouped.Select(group => new CodeReference
            {
                ReferencePath = description,
                ResolvedPath = group.Key,
                MatchType = ReferenceMatchType.Exact,
                RelevanceScore = 0.9,
                FileMatches = group
                    .Select(s => FileMatch.Create(
                        s.FilePath, ReferenceMatchType.Exact, 0.9,
                        $"CodeIndex: {s.Kind} {s.Name}"))
                    .ToList()
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "CodeIndex 搜索失败，降级到文件搜索");
            return [];
        }
    }

    private string GetProjectRoot(ReferenceResolutionOptions opts)
    {
        if (!string.IsNullOrEmpty(opts.ProjectRoot))
        {
            return _fileOperationService.GetFullPath(opts.ProjectRoot);
        }

        return _fileOperationService.GetCurrentDirectory();
    }

    private async Task<CodeReference> ResolveDirectoryReferenceAsync(
        string reference,
        string directoryPath,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var fileMatches = new List<FileMatch>();

        // 获取目录下的文件
        foreach (var pattern in opts.IncludePatterns.Take(5)) // 限制模式数量
        {
            cancellationToken.ThrowIfCancellationRequested();

            var searchResult = await _searchService.GlobSearchAsync(
                pattern,
                directoryPath,
                cancellationToken).ConfigureAwait(false);

            if (searchResult.Success)
            {
                foreach (var file in searchResult.Filenames.Take(opts.MaxResults))
                {
                    fileMatches.Add(FileMatch.Create(
                        file,
                        ReferenceMatchType.Exact,
                        1.0,
                        "目录内文件"));
                }
            }
        }

        return new CodeReference
        {
            ReferencePath = reference,
            ResolvedPath = directoryPath,
            MatchType = ReferenceMatchType.Exact,
            RelevanceScore = 1.0,
            FileMatches = fileMatches.Take(opts.MaxResults).ToList()
        };
    }

    private CodeReference ResolveFileReference(string reference, string filePath, ReferenceMatchType matchType)
    {
        var fileMatch = FileMatch.Create(filePath, matchType, 1.0, "精确匹配");

        return new CodeReference
        {
            ReferencePath = reference,
            ResolvedPath = filePath,
            MatchType = matchType,
            RelevanceScore = 1.0,
            FileMatches = new[] { fileMatch }
        };
    }

    private async Task<CodeReference?> TryGlobMatchAsync(
        string reference,
        string projectRoot,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        // 转换路径分隔符并尝试作为 Glob 模式
        var globPattern = reference.Replace('/', Path.DirectorySeparatorChar);

        // 如果包含通配符，直接使用
        if (globPattern.Contains('*') || globPattern.Contains('?'))
        {
            var searchResult = await _searchService.GlobSearchAsync(
                globPattern,
                projectRoot,
                cancellationToken).ConfigureAwait(false);

            if (searchResult.Success && searchResult.Filenames.Count > 0)
            {
                var fileMatches = searchResult.Filenames
                    .Select(f => FileMatch.Create(f, ReferenceMatchType.Pattern, 0.9, "Glob 模式匹配"))
                    .Take(opts.MaxResults)
                    .ToList();

                return new CodeReference
                {
                    ReferencePath = reference,
                    ResolvedPath = projectRoot,
                    MatchType = ReferenceMatchType.Pattern,
                    RelevanceScore = 0.9,
                    FileMatches = fileMatches
                };
            }
        }

        return null;
    }

    private async Task<CodeReference?> TryFuzzyMatchAsync(
        string reference,
        string projectRoot,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var parts = reference.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var currentPath = projectRoot;
        var matchedParts = new List<string>();
        var allMatches = new List<FileMatch>();

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 检查是否是目录别名
            if (DirectoryAliases.TryGetValue(part, out var aliases))
            {
                var found = false;
                foreach (var alias in aliases)
                {
                    var aliasPath = _fileOperationService.CombinePath(currentPath, alias);
                    if (_fileOperationService.DirectoryExists(aliasPath))
                    {
                        currentPath = aliasPath;
                        matchedParts.Add(alias);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // 尝试直接匹配
                    var directPath = _fileOperationService.CombinePath(currentPath, part);
                    if (_fileOperationService.DirectoryExists(directPath))
                    {
                        currentPath = directPath;
                        matchedParts.Add(part);
                    }
                    else
                    {
                        // 尝试模糊匹配目录名
                        var fuzzyDir = await FindFuzzyDirectoryAsync(currentPath, part, cancellationToken).ConfigureAwait(false);
                        if (fuzzyDir != null)
                        {
                            currentPath = fuzzyDir;
                            matchedParts.Add(Path.GetFileName(fuzzyDir));
                        }
                    }
                }
            }
            else
            {
                // 尝试直接匹配
                var directPath = _fileOperationService.CombinePath(currentPath, part);
                if (_fileOperationService.DirectoryExists(directPath))
                {
                    currentPath = directPath;
                    matchedParts.Add(part);
                }
                else if (_fileOperationService.FileExists(directPath))
                {
                    allMatches.Add(FileMatch.Create(
                        directPath,
                        ReferenceMatchType.Fuzzy,
                        CalculateRelevanceScore(directPath, reference),
                        "模糊匹配"));
                }
                else
                {
                    // 尝试作为 Glob 模式
                    var searchResult = await _searchService.GlobSearchAsync(
                        $"**/{part}",
                        currentPath,
                        cancellationToken).ConfigureAwait(false);

                    if (searchResult.Success)
                    {
                        foreach (var file in searchResult.Filenames)
                        {
                            var score = CalculateRelevanceScore(file, part);
                            if (score >= opts.FuzzyMatchThreshold)
                            {
                                allMatches.Add(FileMatch.Create(
                                    file,
                                    ReferenceMatchType.Fuzzy,
                                    score,
                                    "模糊匹配"));
                            }
                        }
                    }
                }
            }
        }

        // 如果匹配到了目录，获取目录内容
        if (matchedParts.Count > 0 && allMatches.Count == 0)
        {
            return await ResolveDirectoryReferenceAsync(
                reference,
                currentPath,
                opts,
                cancellationToken).ConfigureAwait(false);
        }

        if (allMatches.Count > 0)
        {
            var bestMatches = allMatches
                .OrderByDescending(m => m.RelevanceScore)
                .Take(opts.MaxResults)
                .ToList();

            return new CodeReference
            {
                ReferencePath = reference,
                ResolvedPath = bestMatches[0].FilePath,
                MatchType = ReferenceMatchType.Fuzzy,
                RelevanceScore = bestMatches.Average(m => m.RelevanceScore),
                FileMatches = bestMatches
            };
        }

        return null;
    }

    private async Task<CodeReference?> TryPartialMatchAsync(
        string reference,
        string projectRoot,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(reference);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = reference;
        }

        // 尝试搜索文件名包含该字符串的文件
        var searchResult = await _searchService.GlobSearchAsync(
            $"**/*{fileName}*",
            projectRoot,
            cancellationToken).ConfigureAwait(false);

        if (searchResult.Success && searchResult.Filenames.Count > 0)
        {
            var fileMatches = searchResult.Filenames
                .Select(f => FileMatch.Create(
                    f,
                    ReferenceMatchType.Partial,
                    CalculateRelevanceScore(f, fileName) * 0.8, // 部分匹配降低分数
                    "部分匹配"))
                .Where(fm => fm.RelevanceScore >= opts.MinRelevanceScore)
                .Take(opts.MaxResults)
                .ToList();

            if (fileMatches.Count > 0)
            {
                return new CodeReference
                {
                    ReferencePath = reference,
                    ResolvedPath = fileMatches[0].FilePath,
                    MatchType = ReferenceMatchType.Partial,
                    RelevanceScore = fileMatches.Average(fm => fm.RelevanceScore),
                    FileMatches = fileMatches
                };
            }
        }

        return null;
    }

    private async Task<string?> FindFuzzyDirectoryAsync(
        string parentPath,
        string targetName,
        CancellationToken cancellationToken)
    {
        if (!_fileOperationService.DirectoryExists(parentPath))
        {
            return null;
        }

        var directories = _fileOperationService.GetDirectories(parentPath, "*", SearchOption.TopDirectoryOnly);

        var match = directories
            .Select(dir => (Dir: dir, Score: CalculateSimilarity(Path.GetFileName(dir), targetName)))
            .Where(x => x.Score >= 0.6)
            .MaxBy(x => x.Score);

        return match is { Dir: var bestMatch } ? bestMatch : null;
    }

    private async Task<IReadOnlyList<CodeReference>> TryGrepSearchAsync(
        string description,
        string projectRoot,
        ReferenceResolutionOptions opts,
        CancellationToken cancellationToken)
    {
        var results = new List<CodeReference>();

        try
        {
            var grepResult = await _searchService.GrepSearchAsync(
                new GrepSearchInput
                {
                    Pattern = Regex.Escape(description),
                    Path = projectRoot,
                    OutputMode = SearchOutputMode.Files,
                    CaseInsensitive = true,
                    HeadLimit = opts.MaxResults
                },
                cancellationToken).ConfigureAwait(false);

            if (grepResult.Success && grepResult.Filenames.Count > 0)
            {
                var fileMatches = grepResult.Filenames
                    .Select(f => FileMatch.Create(
                        f,
                        ReferenceMatchType.Fuzzy,
                        0.6,
                        "内容匹配"))
                    .ToList();

                results.Add(new CodeReference
                {
                    ReferencePath = description,
                    ResolvedPath = projectRoot,
                    MatchType = ReferenceMatchType.Fuzzy,
                    RelevanceScore = 0.6,
                    FileMatches = fileMatches
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Grep 搜索失败");
        }

        return results;
    }

    private static List<string> InferGlobPatterns(string description)
    {
        var patterns = new List<string>();

        // 检查是否有扩展名
        var extension = Path.GetExtension(description);
        if (!string.IsNullOrEmpty(extension) && ExtensionPatterns.TryGetValue(extension, out var extPatterns))
        {
            patterns.AddRange(extPatterns);
        }
        else
        {
            // 默认搜索常见代码文件
            patterns.Add($"**/*{description}*.cs");
            patterns.Add($"**/*{description}*.ts");
            patterns.Add($"**/*{description}*.js");
            patterns.Add($"**/*{description}*.py");
        }

        return patterns;
    }

    private static List<string> ExtractKeywords(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);

        return parts
            .SelectMany(part => new[] { part }.Concat(SplitCamelCase(part)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitCamelCase(string input)
    {
        var words = new List<string>();
        var currentWord = new System.Text.StringBuilder();

        foreach (char c in input)
        {
            if (char.IsUpper(c) && currentWord.Length > 0)
            {
                // 使用 Span 避免 ToLowerInvariant 分配
                words.Add(currentWord.ToString());
                currentWord.Clear();
            }
            currentWord.Append(c);
        }

        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }

        return words;
    }

    private static double CalculateRelevanceScore(string filePath, string query)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;

        // 使用 OrdinalIgnoreCase 比较避免 ToLowerInvariant 分配
        if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        // 文件名包含查询 - 使用 Span 和 OrdinalIgnoreCase
        if (ContainsOrdinalIgnoreCase(fileName.AsSpan(), query.AsSpan()))
        {
            return 0.9;
        }

        // 查询包含文件名
        if (ContainsOrdinalIgnoreCase(query.AsSpan(), fileName.AsSpan()))
        {
            return 0.8;
        }

        // 计算相似度 - 需要小写版本
        var fileNameLower = fileName.ToLowerInvariant();
        var queryLower = query.ToLowerInvariant();
        return CalculateSimilarity(fileNameLower, queryLower);
    }

    /// <summary>
    /// 使用 OrdinalIgnoreCase 检查 Span 是否包含子串
    /// </summary>
    private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return true;
        if (source.IsEmpty) return false;

        for (int i = 0; i <= source.Length - value.Length; i++)
        {
            if (source.Slice(i, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static double CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
        {
            return 0.0;
        }

        var longer = s1.Length > s2.Length ? s1 : s2;
        var shorter = s1.Length > s2.Length ? s2 : s1;

        if (longer.Length == 0)
        {
            return 1.0;
        }

        var distance = CalculateLevenshteinDistance(longer, shorter);
        return (longer.Length - distance) / (double)longer.Length;
    }

    private static int CalculateLevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (var i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    #endregion
}
