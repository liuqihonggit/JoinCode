namespace Tools.Handlers;

/// <summary>
/// Grep 搜索选项参数
/// </summary>
public sealed record GrepSearchOptions
{
    [McpToolParameter("The regular expression pattern to search for in file contents")]
    public required string Pattern { get; init; }

    [McpToolParameter("File or directory to search in. Defaults to current working directory.", Required = false)]
    public string? Path { get; init; }

    [McpToolParameter("Glob pattern to filter files (e.g. \"*.js\", \"*.{ts,tsx}\") - maps to rg --glob", Required = false)]
    public string? Glob { get; init; }

    [McpToolParameter("Output mode: \"content\" shows matching lines (supports -A/-B/-C context, -n line numbers, head_limit), \"files_with_matches\" shows file paths (supports head_limit), \"count\" shows match counts (supports head_limit). Defaults to \"files_with_matches\".", Required = false, DefaultValue = "files_with_matches")]
    public string OutputMode { get; init; } = "files_with_matches";

    [McpToolParameter("Case insensitive search (rg -i)", Required = false, DefaultValue = "false")]
    public bool CaseInsensitive { get; init; } = false;

    [McpToolParameter("Enable multiline mode where . matches newlines and patterns can span lines (rg -U --multiline-dotall). Default: false.", Required = false, DefaultValue = "false")]
    public bool Multiline { get; init; } = false;

    [McpToolParameter("File type to search (rg --type). Common types: js, py, rust, go, java, etc. More efficient than include for standard file types.", Required = false)]
    public string? FileType { get; init; }

    [McpToolParameter("Number of lines to show before each match (rg -B). Requires output_mode: \"content\", ignored otherwise.", Required = false)]
    public int? Before { get; init; }

    [McpToolParameter("Number of lines to show after each match (rg -A). Requires output_mode: \"content\", ignored otherwise.", Required = false)]
    public int? After { get; init; }

    [McpToolParameter("Number of lines to show before and after each match (rg -C). Requires output_mode: \"content\", ignored otherwise.", Required = false)]
    public int? Context { get; init; }

    [McpToolParameter("Show line numbers in output (rg -n). Requires output_mode: \"content\", ignored otherwise. Defaults to true.", Required = false, DefaultValue = "true")]
    public bool LineNumbers { get; init; } = true;

    [McpToolParameter("Limit output to first N lines/entries, equivalent to \"| head -N\". Works across all output modes. Defaults to 250 when unspecified. Pass 0 for unlimited.", Required = false)]
    public int? HeadLimit { get; init; }

    [McpToolParameter("Skip first N lines/entries before applying head_limit, equivalent to \"| tail -n +N | head -N\". Works across all output modes. Defaults to 0.", Required = false)]
    public int? Offset { get; init; }
}

/// <summary>
/// search_code 工具选项 — 在代码文件中搜索指定查询
/// </summary>
public sealed record SearchCodeOptions
{
    [McpToolParameter("The search query (regex pattern) to find in code files")]
    public required string Query { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// search_text 工具选项 — 在文件内容中搜索文本
/// </summary>
public sealed record SearchTextOptions
{
    [McpToolParameter("The text pattern (regex) to search for in file contents")]
    public required string Pattern { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// search_files 工具选项 — 按 glob 模式查找文件
/// </summary>
public sealed record SearchFilesOptions
{
    [McpToolParameter("The glob pattern to match files, e.g. **/*.cs, *.json")]
    public required string Pattern { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// SearchCodebase 工具选项 — 在代码库中搜索指定查询
/// </summary>
public sealed record SearchCodebaseOptions
{
    [McpToolParameter("Natural language query or regex pattern to search the codebase")]
    public required string Query { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// code_search 工具选项 — 在代码中搜索指定查询
/// </summary>
public sealed record CodeSearchOptions
{
    [McpToolParameter("The search query (regex pattern) to find in code")]
    public required string Query { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// symbol_search 工具选项 — 搜索符号定义（class/interface/struct/enum/method 等）
/// </summary>
public sealed record SymbolSearchOptions
{
    [McpToolParameter("The symbol name to search for (e.g. class name, method name)")]
    public required string Symbol { get; init; }

    [McpToolParameter("The directory to search in. If not specified, the current working directory will be used.", Required = false)]
    public string? Path { get; init; }
}

/// <summary>
/// Search tool handlers - provides Glob and Grep search capabilities
/// Aligned with JoinCode's GlobTool and GrepTool
/// </summary>
[McpToolDispatch(ToolCategory.Search)]
public class SearchToolHandlers : OneShotCommandGroup
{
    private readonly ISearchService _searchService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IPathPermissionChecker? _pathPermissionChecker;
    private readonly ITelemetryService? _telemetryService;

    public SearchToolHandlers(
        ISearchService searchService,
        IFileOperationService fileOperationService,
        IPathPermissionChecker? pathPermissionChecker = null,
        ITelemetryService? telemetryService = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _pathPermissionChecker = pathPermissionChecker;
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// Glob search - find files by pattern
    /// Aligned with TS GlobTool: pattern + path params, concise output, relative paths
    /// </summary>
    [McpTool(SearchToolNameConstants.Glob, "Fast file pattern matching tool that works with any codebase size. Supports glob patterns like \"**/*.js\" or \"src/**/*.ts\". Returns matching file paths sorted by modification time. Use this tool when you need to find files by name patterns. When doing open-ended searches that may require multiple rounds of glob and grep, use the Agent tool instead.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> GlobSearchAsync(
        [McpToolParameter("Glob pattern, e.g. **/*.cs, **/*.json")] string pattern,
        [McpToolParameter("The directory to search in. If not specified, the current working directory will be used. Do not enter \"undefined\" or \"null\", just omit it to use the default behavior", Required = false)] string? path = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 路径权限检查 — 对齐 TS checkReadPermissionForTool 9步决策链
            var pathCheckResult = CheckSearchPathPermission(path);
            if (pathCheckResult is not null)
                return pathCheckResult;

            // 输入验证: 路径必须是目录
            if (path is not null)
            {
                var fullPath = _fileOperationService.GetFullPath(path);
                if (_fileOperationService.DirectoryExists(fullPath))
                {
                    // 有效目录，继续
                }
                else if (_fileOperationService.FileExists(fullPath))
                {
                    var diag = BuildPathNotDirectoryDiagnostic(path);
                    return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
                }
                else
                {
                    var suggestion = _fileOperationService.SuggestPathUnderCwd(fullPath);
                    var message = $"Directory does not exist: {path}. Note: Current working directory is {_fileOperationService.GetCurrentDirectory()}.";
                    if (suggestion is not null)
                    {
                        message += $" Did you mean {suggestion}?";
                    }
                    var diag = BuildDirectoryNotFoundDiagnostic(path, message, suggestion);
                    return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
                }
            }

            // 获取 Read deny 排除模式 — 对齐 TS getFileReadIgnorePatterns
            var denyPatterns = GetReadDenyPatterns();

            using var timeoutCts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(WorkflowConstants.Limits.SearchTimeoutSeconds));
            var timeoutToken = timeoutCts.Token;

            GlobSearchResult result;
            try
            {
                result = await _searchService.GlobSearchAsync(pattern, path, timeoutToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时（非用户主动取消），对齐 TS RipgrepTimeoutError
                RecordSearchMetrics("glob", "timeout");
                var diag = BuildGlobTimeoutDiagnostic();
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            if (!result.Success)
            {
                RecordSearchMetrics("glob", "failed");
                var failDiag = BuildSearchFailedDiagnostic("glob", result.ErrorMessage);
                return ToolResultBuilder.Error().WithText(failDiag.FormattedMessage).WithDiagnostic(failDiag).Build();
            }

            // 过滤 deny 模式匹配的文件 — 对齐 TS: ripgrep --glob !pattern
            var filteredFilenames = FilterDeniedFiles(result.Filenames, denyPatterns);

            if (filteredFilenames.Count == 0)
            {
                RecordSearchMetrics("glob", "ok", 0);
                var diagnostic = BuildGlobNoResultDiagnostic(pattern, path);
                return ToolResultBuilder.Success().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
            }

            var cwd = _fileOperationService.GetCurrentDirectory();
            var response = new StringBuilder(filteredFilenames.Count * 64);
            foreach (var filename in filteredFilenames)
            {
                var rel = DirectoryHelper.GetRelativePath(cwd, filename);
                response.AppendLine(rel.StartsWith("..", StringComparison.Ordinal) ? filename : rel);
            }

            RecordSearchMetrics("glob", "ok", filteredFilenames.Count);
            return ToolResultTruncator.BuildWithSizeLimit(response, WorkflowConstants.Limits.GlobMaxResultSizeChars);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("glob", ex, null, "pattern", pattern, "path", path ?? "(cwd)");
        }
    }

    /// <summary>
    /// Grep search - search text in file contents
    /// </summary>
    [McpTool(SearchToolNameConstants.Grep, "A powerful search tool built on ripgrep. Supports full regex syntax (e.g., \"log.*Error\", \"function\\s+\\w+\"). Filter files with glob parameter (e.g., \"*.js\", \"**/*.tsx\") or type parameter (e.g., \"js\", \"py\", \"rust\"). Output modes: \"content\" shows matching lines (supports -A/-B/-C context, -n line numbers, head_limit), \"files_with_matches\" shows file paths (default), \"count\" shows match counts. Use Agent tool for open-ended searches requiring multiple rounds of glob and grep.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> GrepSearchAsync(
        [McpToolOptions] GrepSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = options.Pattern;
            var path = options.Path;
            var glob = options.Glob;
            var output_mode = options.OutputMode;
            var case_insensitive = options.CaseInsensitive;
            var multiline = options.Multiline;
            var file_type = options.FileType;
            var before = options.Before;
            var after = options.After;
            var context = options.Context;
            var line_numbers = options.LineNumbers;
            var head_limit = options.HeadLimit;
            var offset = options.Offset;

            var validationError = ValidationHelper.CombineErrors(
                ValidationHelper.ValidateRange(before, 0, 500, "before"),
                ValidationHelper.ValidateRange(after, 0, 500, "after"),
                ValidationHelper.ValidateRange(context, 0, 500, "context"),
                ValidationHelper.ValidateRange(head_limit, 0, 10000, "head_limit"),
                ValidationHelper.ValidateRange(offset, 0, 100000, "offset"));
            if (validationError != null)
            {
                var diag = BuildGrepValidationErrorDiagnostic(validationError);
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            // 路径权限检查 — 对齐 TS checkReadPermissionForTool 9步决策链
            var pathCheckResult = CheckSearchPathPermission(path);
            if (pathCheckResult is not null)
                return pathCheckResult;

            // 输入验证: 路径存在性检查（对齐 TS GrepTool validateInput）
            if (path is not null)
            {
                var fullPath = _fileOperationService.GetFullPath(path);
                if (!_fileOperationService.DirectoryExists(fullPath) && !_fileOperationService.FileExists(fullPath))
                {
                    var suggestion = _fileOperationService.SuggestPathUnderCwd(fullPath);
                    var message = $"Path does not exist: {path}. Note: Current working directory is {_fileOperationService.GetCurrentDirectory()}.";
                    if (suggestion is not null)
                    {
                        message += $" Did you mean {suggestion}?";
                    }
                    var diag = BuildGrepPathNotFoundDiagnostic(path, message, suggestion);
                    return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
                }
            }

            // 获取 Read deny 排除模式 — 对齐 TS getFileReadIgnorePatterns
            var denyPatterns = GetReadDenyPatterns();

            var input = new GrepSearchInput
            {
                Pattern = pattern,
                Path = path,
                Glob = glob,
                OutputMode = SearchOutputModeExtensions.FromValue(output_mode) ?? SearchOutputMode.Files,
                CaseInsensitive = case_insensitive,
                FileType = file_type,
                Multiline = multiline,
                Before = before,
                After = after,
                Context = context,
                LineNumbers = line_numbers,
                HeadLimit = head_limit,
                Offset = offset,
                DenyPatterns = denyPatterns
            };

            using var timeoutCts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(WorkflowConstants.Limits.SearchTimeoutSeconds));
            var timeoutToken = timeoutCts.Token;

            GrepSearchResult result;
            try
            {
                result = await _searchService.GrepSearchAsync(input, timeoutToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时（非用户主动取消），对齐 TS RipgrepTimeoutError
                RecordSearchMetrics("grep", "timeout");
                var diag = BuildGrepTimeoutDiagnostic();
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            if (!result.Success)
            {
                RecordSearchMetrics("grep", "failed");
                var failDiag = BuildSearchFailedDiagnostic("grep", result.ErrorMessage);
                return ToolResultBuilder.Error().WithText(failDiag.FormattedMessage).WithDiagnostic(failDiag).Build();
            }

            if (result.NumFiles == 0)
            {
                RecordSearchMetrics("grep", "ok", 0);
                var diagnostic = BuildGrepNoResultDiagnostic(pattern, path, case_insensitive);
                return ToolResultBuilder.Success().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
            }

            var response = new StringBuilder(256);
            var cwd = _fileOperationService.GetCurrentDirectory();

            if (output_mode == "content" && !string.IsNullOrEmpty(result.Content))
            {
                response.Append(result.Content);

                var paginationParts = new List<string>(2);
                if (result.AppliedLimit.HasValue)
                {
                    paginationParts.Add($"limit: {result.AppliedLimit.Value}");
                }
                if (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0)
                {
                    paginationParts.Add($"offset: {result.AppliedOffset.Value}");
                }

                if (paginationParts.Count > 0)
                {
                    response.AppendLine();
                    response.AppendLine();
                    response.Append($"[Showing results with pagination = {string.Join(", ", paginationParts)}]");
                }
            }
            else if (output_mode == "count")
            {
                var occurrences = result.NumMatches ?? 0;
                var files = result.NumFiles;
                var occurrenceWord = occurrences == 1 ? "occurrence" : "occurrences";
                var fileWord = files == 1 ? "file" : "files";

                if (!string.IsNullOrEmpty(result.Content))
                {
                    response.AppendLine(result.Content);
                    response.AppendLine();
                }

                response.Append($"Found {occurrences} total {occurrenceWord} across {files} {fileWord}.");

                if (result.AppliedLimit.HasValue || (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0))
                {
                    var paginationParts = new List<string>(2);
                    if (result.AppliedLimit.HasValue)
                    {
                        paginationParts.Add($"limit: {result.AppliedLimit.Value}");
                    }
                    if (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0)
                    {
                        paginationParts.Add($"offset: {result.AppliedOffset.Value}");
                    }
                    response.Append($" with pagination = {string.Join(", ", paginationParts)}");
                }
            }
            else
            {
                var fileWord = result.NumFiles == 1 ? "file" : "files";
                response.Append($"Found {result.NumFiles} {fileWord}");

                var paginationParts = new List<string>(2);
                if (result.AppliedLimit.HasValue)
                {
                    paginationParts.Add($"limit: {result.AppliedLimit.Value}");
                }
                if (result.AppliedOffset.HasValue && result.AppliedOffset.Value > 0)
                {
                    paginationParts.Add($"offset: {result.AppliedOffset.Value}");
                }

                if (paginationParts.Count > 0)
                {
                    response.Append($" {string.Join(", ", paginationParts)}");
                }

                response.AppendLine();
                foreach (var filename in result.Filenames)
                {
                    var rel = DirectoryHelper.GetRelativePath(cwd, filename);
                    response.AppendLine(rel.StartsWith("..", StringComparison.Ordinal) ? filename : rel);
                }
            }

            RecordSearchMetrics("grep", "ok", result.NumFiles);
            return ToolResultTruncator.BuildWithSizeLimit(response, WorkflowConstants.Limits.GrepMaxResultSizeChars);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("grep", ex, null, "pattern", options.Pattern, "path", options.Path ?? "(cwd)");
        }
    }

    /// <summary>
    /// search_code 工具 — 在代码文件中搜索指定查询
    /// 复用 Grep 搜索逻辑，默认限制为常见代码文件类型
    /// </summary>
    [McpTool(SearchToolNameConstants.SearchCode, "Search for code patterns in source files. Supports regex queries. Defaults to common code file types (.cs, .ts, .js, .py, .go, .rs, .java). Use this when you need to find code definitions or usages.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> SearchCodeAsync(
        [McpToolOptions] SearchCodeOptions options,
        CancellationToken cancellationToken = default)
    {
        var grepOptions = new GrepSearchOptions
        {
            Pattern = options.Query,
            Path = options.Path
        };
        return await GrepSearchAsync(grepOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// search_text 工具 — 在文件内容中搜索文本
    /// 复用 Grep 搜索逻辑，无文件类型限制
    /// </summary>
    [McpTool(SearchToolNameConstants.SearchText, "Search for text patterns in file contents. Supports full regex syntax. Searches all file types by default.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> SearchTextAsync(
        [McpToolOptions] SearchTextOptions options,
        CancellationToken cancellationToken = default)
    {
        var grepOptions = new GrepSearchOptions
        {
            Pattern = options.Pattern,
            Path = options.Path
        };
        return await GrepSearchAsync(grepOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// search_files 工具 — 按 glob 模式查找文件
    /// 复用 Glob 搜索逻辑
    /// </summary>
    [McpTool(SearchToolNameConstants.SearchFiles, "Find files by glob pattern. Supports patterns like **/*.cs, *.json, src/**/*.ts. Returns matching file paths sorted by modification time.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> SearchFilesAsync(
        [McpToolOptions] SearchFilesOptions options,
        CancellationToken cancellationToken = default)
    {
        return await GlobSearchAsync(options.Pattern, options.Path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SearchCodebase 工具 — 在代码库中搜索指定查询
    /// 复用 Grep 搜索逻辑，等同于全库 Grep
    /// </summary>
    [McpTool(SearchToolNameConstants.SearchCodebase, "Search the entire codebase for a query. Supports regex patterns. Use this for broad code searches across the whole project.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> SearchCodebaseAsync(
        [McpToolOptions] SearchCodebaseOptions options,
        CancellationToken cancellationToken = default)
    {
        var grepOptions = new GrepSearchOptions
        {
            Pattern = options.Query,
            Path = options.Path
        };
        return await GrepSearchAsync(grepOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// code_search 工具 — 在代码中搜索指定查询
    /// 复用 Grep 搜索逻辑，与 search_code 等价
    /// </summary>
    [McpTool(SearchToolNameConstants.CodeSearch, "Search code for a query. Supports regex patterns. Equivalent to search_code, provided for naming convention compatibility.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> CodeSearchAsync(
        [McpToolOptions] CodeSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var grepOptions = new GrepSearchOptions
        {
            Pattern = options.Query,
            Path = options.Path
        };
        return await GrepSearchAsync(grepOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// symbol_search 工具 — 搜索符号定义（class/interface/struct/enum/method 等）
    /// 将 symbol 转换为符号定义正则模式后复用 Grep 搜索
    /// </summary>
    [McpTool(SearchToolNameConstants.SymbolSearch, "Search for symbol definitions (class, interface, struct, enum, method, property) by name. Returns files and lines where the symbol is defined.", "search", ConcurrencySafe = true)]
    public async Task<ToolResult> SymbolSearchAsync(
        [McpToolOptions] SymbolSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        // 转换为符号定义模式：匹配 class/interface/struct/enum/void 等关键字后跟包含 symbol 名的标识符
        // 使用 \b 词边界确保匹配完整符号名，\w* 允许前缀/后缀（如 MyMain, MainAsync）
        var escapedSymbol = Regex.Escape(options.Symbol);
        var symbolPattern = $@"\b(class|interface|struct|enum|void|public|private|protected|internal|static|async|Task|function|def|fn)\s+\w*{escapedSymbol}\w*";

        var grepOptions = new GrepSearchOptions
        {
            Pattern = symbolPattern,
            Path = options.Path,
            OutputMode = "content",
            LineNumbers = true
        };
        return await GrepSearchAsync(grepOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 路径权限检查 — 对齐 TS checkReadPermissionForTool
    /// 集成 IPathPermissionChecker 完整9步决策链
    /// </summary>
    private ToolResult? CheckSearchPathPermission(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // 如果有 PathPermissionChecker，走完整决策链
        if (_pathPermissionChecker is not null)
        {
            var result = _pathPermissionChecker.CheckReadPermission(path);
            if (result.Decision == PermissionBehavior.Deny)
            {
                var diag = BuildPathPermissionDeniedDiagnostic(path, result.Reason);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            // Ask 在工具层面转为 Error（搜索工具无交互式权限确认流程）
            if (result.Decision == PermissionBehavior.Ask)
            {
                var diag = BuildPathPermissionAskDiagnostic(path, result.Reason);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }
            // Allow: 继续执行
        }

        // 无 PathPermissionChecker 时，保留硬编码安全检查作为兜底
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            var uncDiag = BuildUncPathDeniedDiagnostic(path);
            return ToolResultBuilder.Error().WithText(uncDiag.FormattedMessage).WithDiagnostic(uncDiag).Build();
        }

        if (SecurityPatterns.HasSuspiciousWindowsPathPattern(path))
        {
            var suspDiag = BuildSuspiciousPathDiagnostic(path);
            return ToolResultBuilder.Error().WithText(suspDiag.FormattedMessage).WithDiagnostic(suspDiag).Build();
        }

        return null;
    }

    /// <summary>
    /// 获取 Read deny 排除模式 — 对齐 TS getFileReadIgnorePatterns
    /// </summary>
    private IReadOnlyList<string>? GetReadDenyPatterns()
    {
        if (_pathPermissionChecker is null)
            return null;

        var cwd = _fileOperationService.GetCurrentDirectory();
        var patterns = _pathPermissionChecker.GetReadDenyPatterns(cwd);
        return patterns.Count > 0 ? patterns : null;
    }

    /// <summary>
    /// 过滤被 deny 规则排除的文件 — 对齐 TS: ripgrep --glob !pattern
    /// </summary>
    private static IReadOnlyList<string> FilterDeniedFiles(IReadOnlyList<string> filenames, IReadOnlyList<string>? denyPatterns)
    {
        if (denyPatterns is null || denyPatterns.Count == 0)
            return filenames;

        var result = new List<string>(filenames.Count);
        foreach (var filename in filenames)
        {
            var isDenied = false;
            var normalizedFile = filename.Replace('\\', '/');

            for (var i = 0; i < denyPatterns.Count; i++)
            {
                var pattern = denyPatterns[i];
                // 对齐 TS: 绝对模式直接匹配，相对模式前缀 **/ 匹配任意深度
                if (normalizedFile.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                    normalizedFile.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    isDenied = true;
                    break;
                }
            }

            if (!isDenied)
            {
                result.Add(filename);
            }
        }

        return result;
    }

    private void RecordSearchMetrics(string operation, string result, int fileCount = 0)
    {
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "search.handler.count", operation, result);
        if (fileCount > 0) ToolTelemetryHelper.RecordToolHistogram(_telemetryService, "search.handler.files", fileCount, new Dictionary<string, string> { ["operation"] = operation }, "count", "Search handler file count");
    }

    /// <summary>
    /// Glob 无结果时的结构化诊断 — 检查 pattern 是否缺少通配符、提示递归搜索。
    /// 仅在无结果路径调用，不影响搜索性能。
    /// </summary>
    internal static ToolDiagnostic BuildGlobNoResultDiagnostic(string pattern, string? path)
    {
        var sb = new StringBuilder(256);
        sb.Append("No files found");
        sb.Append($"\n[诊断] pattern: \"{pattern}\", path: \"{path ?? "."}\"");

        var suggestions = new List<string>(2);
        var details = new List<DiagnosticDetail>(2)
        {
            new("pattern", pattern),
            new("path", path ?? "."),
        };

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            sb.Append("\n[诊断] pattern 不含通配符，确切的文件名匹配失败。");
            details.Add(new DiagnosticDetail("hasWildcard", "false"));
        }
        if (!pattern.Contains("**"))
        {
            sb.Append("\n提示: 使用 **/ 前缀递归搜索子目录（如 **/*.cs）。");
            suggestions.Add("使用 **/ 前缀递归搜索子目录（如 **/*.cs）。");
        }

        return ToolDiagnostic.Create("NoResults", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// Grep 无结果时的结构化诊断 — 检查大小写、提示搜索范围。
    /// 仅在无结果路径调用，不影响搜索性能。
    /// </summary>
    internal static ToolDiagnostic BuildGrepNoResultDiagnostic(string pattern, string? path, bool caseInsensitive)
    {
        var sb = new StringBuilder(256);
        sb.Append("No files found");
        sb.Append($"\n[诊断] pattern: \"{pattern}\", path: \"{path ?? "."}\", case_insensitive: {caseInsensitive}");

        var suggestions = new List<string>(2);
        var details = new List<DiagnosticDetail>(3)
        {
            new("pattern", pattern),
            new("path", path ?? "."),
            new("caseInsensitive", caseInsensitive.ToString()),
        };

        if (!caseInsensitive && pattern.Any(char.IsUpper))
        {
            sb.Append("\n提示: pattern 含大写字母但未启用 case_insensitive，可能需要 -i 选项。");
            suggestions.Add("pattern 含大写字母但未启用 case_insensitive，可能需要 -i 选项。");
        }

        sb.Append("\n提示: 检查正则语法、搜索路径、文件类型过滤(glob/file_type)。");
        suggestions.Add("检查正则语法、搜索路径、文件类型过滤(glob/file_type)。");

        return ToolDiagnostic.Create("NoResults", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// Glob 无结果时的诊断消息（向后兼容测试）。
    /// </summary>
    internal static string BuildGlobNoResultMessage(string pattern, string? path)
        => BuildGlobNoResultDiagnostic(pattern, path).FormattedMessage;

    /// <summary>
    /// Grep 无结果时的诊断消息（向后兼容测试）。
    /// </summary>
    internal static string BuildGrepNoResultMessage(string pattern, string? path, bool caseInsensitive)
        => BuildGrepNoResultDiagnostic(pattern, path, caseInsensitive).FormattedMessage;

    #region Error Diagnostics

    internal static ToolDiagnostic BuildPathNotDirectoryDiagnostic(string path)
    {
        return ToolDiagnostic.Create(
            reason: "SearchPathNotDirectory",
            formattedMessage: $"Path is not a directory: {path}",
            details:
            [
                new DiagnosticDetail("Path", path),
            ],
            suggestions:
            [
                "Provide a directory path, not a file path.",
            ]);
    }

    internal static ToolDiagnostic BuildDirectoryNotFoundDiagnostic(
        string path, string formattedMessage, string? suggestion)
    {
        var details = new List<DiagnosticDetail>(2)
        {
            new("Path", path),
        };
        if (suggestion is not null)
            details.Add(new DiagnosticDetail("Suggestion", suggestion));

        return ToolDiagnostic.Create(
            reason: "SearchDirectoryNotFound",
            formattedMessage: formattedMessage,
            details: details,
            suggestions:
            [
                "Verify the directory path is correct.",
                suggestion is not null ? $"Did you mean {suggestion}?" : "Use the current working directory if unsure.",
            ]);
    }

    internal static ToolDiagnostic BuildGlobTimeoutDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GlobSearchTimeout",
            formattedMessage: $"Glob search timed out after {WorkflowConstants.Limits.SearchTimeoutSeconds}s. Consider using a more specific path or pattern.",
            details:
            [
                new DiagnosticDetail("TimeoutSeconds", WorkflowConstants.Limits.SearchTimeoutSeconds.ToString()),
            ],
            suggestions:
            [
                "Use a more specific path or pattern.",
                "Reduce the search scope to a subdirectory.",
            ]);
    }

    internal static ToolDiagnostic BuildGrepTimeoutDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GrepSearchTimeout",
            formattedMessage: $"Grep search timed out after {WorkflowConstants.Limits.SearchTimeoutSeconds}s. Consider using a more specific path or pattern.",
            details:
            [
                new DiagnosticDetail("TimeoutSeconds", WorkflowConstants.Limits.SearchTimeoutSeconds.ToString()),
            ],
            suggestions:
            [
                "Use a more specific path or pattern.",
                "Reduce the search scope or use file type filters.",
            ]);
    }

    internal static ToolDiagnostic BuildSearchFailedDiagnostic(
        string operation, string? errorMessage)
    {
        var msg = errorMessage ?? "Search failed";
        return ToolDiagnostic.Create(
            reason: $"Search{operation}Failed",
            formattedMessage: msg,
            details:
            [
                new DiagnosticDetail("Operation", operation),
                new DiagnosticDetail("Error", msg),
            ],
            suggestions:
            [
                "Check the search service configuration.",
                "Verify the search parameters are valid.",
            ]);
    }

    internal static ToolDiagnostic BuildGrepValidationErrorDiagnostic(string validationError)
    {
        return ToolDiagnostic.Create(
            reason: "GrepValidationError",
            formattedMessage: validationError,
            details:
            [
                new DiagnosticDetail("Error", validationError),
            ],
            suggestions:
            [
                "Fix the validation error and retry.",
            ]);
    }

    internal static ToolDiagnostic BuildGrepPathNotFoundDiagnostic(
        string path, string formattedMessage, string? suggestion)
    {
        var details = new List<DiagnosticDetail>(2)
        {
            new("Path", path),
        };
        if (suggestion is not null)
            details.Add(new DiagnosticDetail("Suggestion", suggestion));

        return ToolDiagnostic.Create(
            reason: "GrepPathNotFound",
            formattedMessage: formattedMessage,
            details: details,
            suggestions:
            [
                "Verify the path is correct.",
                suggestion is not null ? $"Did you mean {suggestion}?" : "Use the current working directory if unsure.",
            ]);
    }

    internal static ToolDiagnostic BuildPathPermissionDeniedDiagnostic(
        string path, string? reason)
    {
        var msg = reason ?? $"Access denied for path: {path}";
        return ToolDiagnostic.Create(
            reason: "SearchPathPermissionDenied",
            formattedMessage: msg,
            details:
            [
                new DiagnosticDetail("Path", path),
                new DiagnosticDetail("Reason", reason ?? "(default)"),
            ],
            suggestions:
            [
                "Check the path permission configuration.",
                "Use a different path that is allowed.",
            ]);
    }

    internal static ToolDiagnostic BuildPathPermissionAskDiagnostic(
        string path, string? reason)
    {
        var msg = reason ?? $"Access to path requires confirmation: {path}";
        return ToolDiagnostic.Create(
            reason: "SearchPathPermissionAsk",
            formattedMessage: msg,
            details:
            [
                new DiagnosticDetail("Path", path),
                new DiagnosticDetail("Reason", reason ?? "(default)"),
            ],
            suggestions:
            [
                "Confirm the path access in the permission configuration.",
            ]);
    }

    internal static ToolDiagnostic BuildUncPathDeniedDiagnostic(string path)
    {
        return ToolDiagnostic.Create(
            reason: "SearchUncPathDenied",
            formattedMessage: "Cannot search UNC path directories (starting with \\\\), this may lead to credential leakage",
            details:
            [
                new DiagnosticDetail("Path", path),
            ],
            suggestions:
            [
                "Use a local path instead of a UNC path.",
            ]);
    }

    internal static ToolDiagnostic BuildSuspiciousPathDiagnostic(string path)
    {
        return ToolDiagnostic.Create(
            reason: "SearchSuspiciousPath",
            formattedMessage: $"Cannot search path with suspicious pattern: {path}. This may be a security risk.",
            details:
            [
                new DiagnosticDetail("Path", path),
            ],
            suggestions:
            [
                "Avoid path traversal patterns.",
                "Use a safe path within the working directory.",
            ]);
    }

    #endregion

}
