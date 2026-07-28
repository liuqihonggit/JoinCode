
namespace Tools.Handlers;

/// <summary>
/// 内置技能工具处理器 - 提供常用代码操作技能
/// </summary>
[McpToolHandler(ToolCategory.Skill)]
public partial class BundledSkillToolHandlers
{
    private readonly IShellExecutionService _shellExecutionService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<BundledSkillToolHandlers>? _logger;

    public BundledSkillToolHandlers(
        IShellExecutionService shellExecutionService,
        IFileOperationService fileOperationService,
        IFileSystem fs,
        ILogger<BundledSkillToolHandlers>? logger = null)
    {
        _shellExecutionService = shellExecutionService ?? throw new ArgumentNullException(nameof(shellExecutionService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    #region Simplify Skill

    /// <summary>
    /// 简化代码 - 分析并提供代码简化建议
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillSimplify, "Simplify code and provide refactoring suggestions", "skill")]
    public async Task<ToolResult> SkillSimplifyAsync(
        [McpToolParameter("File path")] string file_path,
        [McpToolParameter("Simplification type (all/readability/performance/complexity)", Required = false, DefaultValue = "all")] string? simplify_type = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return ResultBuilder.Error().WithText("file_path cannot be empty").Build();
        }

        var readResult = await _fileOperationService.ReadFileAsync(file_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!readResult.Success)
        {
            return ResultBuilder.Error().WithText($"File does not exist or read failed: {file_path}").Build();
        }

        var content = readResult.Content;
        var extension = Path.GetExtension(file_path).ToLowerInvariant();

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Gear.ToValue()} Code Simplification Analysis");
        response.AppendLine($"File: {file_path}");
        response.AppendLine($"Type: {simplify_type ?? "all"}");
        response.AppendLine();

        // 基于文件类型提供简化建议
        var suggestions = AnalyzeCodeForSimplification(content, extension, CodeSimplifyTypeExtensions.FromValue(simplify_type) ?? CodeSimplifyType.All);

        if (suggestions.Count == 0)
        {
            response.AppendLine($"{StatusSymbol.Tick.ToValue()} Code already looks clean! No obvious simplification opportunities found.");
        }
        else
        {
            response.AppendLine($"Found {suggestions.Count} simplification suggestions:");
            response.AppendLine();

            for (int i = 0; i < suggestions.Count; i++)
            {
                var (category, line, suggestion) = suggestions[i];
                response.AppendLine($"{i + 1}. [{category}] Line {line}");
                response.AppendLine($"   {suggestion}");
                response.AppendLine();
            }
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #endregion

    #region Verify Skill

    /// <summary>
    /// 验证代码 - 检查语法、运行测试
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillVerify, "Verify code correctness, run tests", "skill")]
    public async Task<ToolResult> SkillVerifyAsync(
        [McpToolParameter("Project path or file path")] string path,
        [McpToolParameter("Verification type (syntax/build/test/all)", Required = false, DefaultValue = "all")] string? verify_type = null,
        [McpToolParameter("Test filter (optional)", Required = false)] string? test_filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResultBuilder.Error().WithText("path cannot be empty").Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Tick.ToValue()} Code Verification");
        response.AppendLine($"Path: {path}");
        response.AppendLine();

        var type = CodeVerifyTypeExtensions.FromValue(verify_type) ?? CodeVerifyType.All;
        var isProject = _fs.DirectoryExists(path);
        var isFile = _fileOperationService.FileExists(path);

        if (!isProject)
        {
            var fileResult = await _fileOperationService.ReadFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!fileResult.Success)
            {
                return ResultBuilder.Error().WithText($"Path does not exist: {path}").Build();
            }
            isFile = true;
        }

        var results = new List<(string Check, bool Passed, string Message)>();

        if (type is CodeVerifyType.All or CodeVerifyType.Syntax)
        {
            var syntaxResult = await CheckSyntaxAsync(path, cancellationToken).ConfigureAwait(false);
            results.Add(("Syntax check", syntaxResult.Success, syntaxResult.Message));
        }

        if (type is CodeVerifyType.All or CodeVerifyType.Build && isProject)
        {
            var buildResult = await CheckBuildAsync(path, cancellationToken).ConfigureAwait(false);
            results.Add(("Build check", buildResult.Success, buildResult.Message));
        }

        if (type is CodeVerifyType.All or CodeVerifyType.Test && isProject)
        {
            var testResult = await RunTestsAsync(path, test_filter, cancellationToken).ConfigureAwait(false);
            results.Add(("Test run", testResult.Success, testResult.Message));
        }

        // 输出结果
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);

        response.AppendLine($"Check results: {passed}/{results.Count} passed");
        response.AppendLine();

        foreach (var (check, passedCheck, message) in results)
        {
            var icon = passedCheck ? StatusSymbol.Tick.ToValue() : StatusSymbol.Cross.ToValue();
            response.AppendLine($"{icon} {check}: {message}");
        }

        if (failed > 0)
        {
            return ResultBuilder.Error().WithText(response.ToString()).Build();
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #endregion

    #region Debug Skill

    /// <summary>
    /// 调试辅助 - 分析问题并提供诊断建议
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillDebug, "Debug assistance, problem diagnosis", "skill")]
    public async Task<ToolResult> SkillDebugAsync(
        [McpToolParameter("Project path or file path")] string path,
        [McpToolParameter("Error message or problem description")] string? error_message = null,
        [McpToolParameter("Diagnostic type (error/performance/memory/all)", Required = false, DefaultValue = "error")] string? debug_type = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResultBuilder.Error().WithText("path cannot be empty").Build();
        }

        var isDirectory = _fs.DirectoryExists(path);
        if (!isDirectory)
        {
            var fileResult = await _fileOperationService.ReadFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!fileResult.Success)
            {
                return ResultBuilder.Error().WithText($"Path does not exist: {path}").Build();
            }
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Pencil.ToValue()} Debug Diagnostics");
        response.AppendLine($"Path: {path}");
        response.AppendLine();

        if (!string.IsNullOrEmpty(error_message))
        {
            response.AppendLine("Error message:");
            response.AppendLine("```");
            response.AppendLine(error_message);
            response.AppendLine("```");
            response.AppendLine();
        }

        var type = CodeDebugTypeExtensions.FromValue(debug_type) ?? CodeDebugType.Error;

        // 分析错误信息
        if (!string.IsNullOrEmpty(error_message))
        {
            var analysis = AnalyzeError(error_message);
            response.AppendLine($"{ObjectSymbol.Search.ToValue()} Error analysis:");
            response.AppendLine(analysis);
            response.AppendLine();
        }

        // 检查常见问题
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Diagnostic suggestions:");

        var suggestions = await GetDebugSuggestionsAsync(path, type, error_message, cancellationToken).ConfigureAwait(false);

        if (suggestions.Count == 0)
        {
            response.AppendLine("- No obvious issues detected");
        }
        else
        {
            foreach (var suggestion in suggestions)
            {
                response.AppendLine($"- {suggestion}");
            }
        }

        response.AppendLine();
        response.AppendLine($"{ObjectSymbol.Gear.ToValue()} Debugging steps:");
        response.AppendLine("1. Check log files for detailed error information");
        response.AppendLine("2. Use a debugger to step through code");
        response.AppendLine("3. Check related dependencies and configuration");
        response.AppendLine("4. Verify input data and boundary conditions");

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #endregion

    #region Batch Skill

    /// <summary>
    /// 批量处理 - 在多个文件上执行相同操作
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillBatch, "Batch process files", "skill")]
    public async Task<ToolResult> SkillBatchAsync(
        [McpToolParameter("File pattern (e.g. *.cs, **/*.txt)")] string pattern,
        [McpToolParameter("Operation type (search/replace/delete/count)")] string operation,
        [McpToolParameter("Search content (for search/replace)", Required = false)] string? search = null,
        [McpToolParameter("Replace content (for replace)", Required = false)] string? replace = null,
        [McpToolParameter("Recursively search subdirectories", Required = false, DefaultValue = "true")] bool? recursive = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return ResultBuilder.Error().WithText("pattern cannot be empty").Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Batch Processing");
        response.AppendLine($"Pattern: {pattern}");
        response.AppendLine($"Operation: {operation}");
        response.AppendLine();

        // 查找匹配的文件
        var files = FindFiles(pattern, recursive ?? true);

        response.AppendLine($"Found {files.Count} matching files");
        response.AppendLine();

        if (files.Count == 0)
        {
            return ResultBuilder.Success().WithText(response.ToString()).Build();
        }

        var results = new List<(string File, bool Success, string Message)>();

        var tasks = files.Select(async file =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await ExecuteBatchOperationAsync(file, operation, search, replace, cancellationToken).ConfigureAwait(false);
                return (file, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                return (file, false, ex.Message);
            }
        });
        results.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));

        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        response.AppendLine($"Results: {succeeded} succeeded, {failed} failed");
        response.AppendLine();

        // 显示失败项
        if (failed > 0)
        {
            response.AppendLine($"{StatusSymbol.Cross.ToValue()} Failed items:");
            foreach (var (file, _, message) in results.Where(r => !r.Success).Take(10))
            {
                response.AppendLine($"  - {file}: {message}");
            }

            if (failed > 10)
            {
                response.AppendLine($"  ... and {failed - 10} more failed items");
            }

            response.AppendLine();
        }

        // 显示成功项摘要
        if (succeeded > 0)
        {
            response.AppendLine($"{StatusSymbol.Tick.ToValue()} Successful items summary:");
            foreach (var (file, _, message) in results.Where(r => r.Success).Take(5))
            {
                response.AppendLine($"  - {file}: {message}");
            }

            if (succeeded > 5)
            {
                response.AppendLine($"  ... and {succeeded - 5} more successful items");
            }
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #endregion

    #region Stuck Skill

    /// <summary>
    /// 卡住帮助 - 当任务卡住时提供替代方案
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillStuck, "Provide help and alternatives when stuck", "skill")]
    public Task<ToolResult> SkillStuckAsync(
        [McpToolParameter("Current approach or problem description")] string current_approach,
        [McpToolParameter("Error message or obstacle encountered", Required = false)] string? obstacle = null,
        [McpToolParameter("Goal or expected result", Required = false)] string? goal = null,
        CancellationToken cancellationToken = default)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{StatusSymbol.Cross.ToValue()} Stuck Help");
        response.AppendLine();

        if (!string.IsNullOrEmpty(current_approach))
        {
            response.AppendLine("Current approach:");
            response.AppendLine(current_approach);
            response.AppendLine();
        }

        if (!string.IsNullOrEmpty(obstacle))
        {
            response.AppendLine("Obstacle encountered:");
            response.AppendLine(obstacle);
            response.AppendLine();
        }

        if (!string.IsNullOrEmpty(goal))
        {
            response.AppendLine("Goal:");
            response.AppendLine(goal);
            response.AppendLine();
        }

        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Suggested alternatives:");
        response.AppendLine();

        // 提供通用的替代方案建议
        var suggestions = GetStuckSuggestions(current_approach, obstacle);

        for (int i = 0; i < suggestions.Count; i++)
        {
            response.AppendLine($"{i + 1}. {suggestions[i].Title}");
            response.AppendLine($"   {suggestions[i].Description}");
            response.AppendLine();
        }

        response.AppendLine($"{StatusSymbol.Refresh.ToValue()} Debugging strategies:");
        response.AppendLine("- Break down: split large problems into small steps");
        response.AppendLine("- Simplify: try the simplest viable case");
        response.AppendLine("- Verify assumptions: check each prerequisite");
        response.AppendLine("- Seek help: consult docs or ask peers");

        return Task.FromResult(ResultBuilder.Success().WithText(response.ToString()).Build());
    }

    #endregion

    #region Private Methods

    private IReadOnlyList<(string Category, int Line, string Suggestion)> AnalyzeCodeForSimplification(string content, string extension, CodeSimplifyType type)
    {
        var suggestions = new List<(string Category, int Line, string Suggestion)>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // 通用简化建议
            if (type is CodeSimplifyType.All or CodeSimplifyType.Readability)
            {
                // 检测过长的行
                if (line.Length > WorkflowConstants.Limits.LineLengthMax)
                {
                    suggestions.Add(("Readability", lineNum, "Line too long, consider splitting into multiple lines or extracting variables"));
                }

                // 检测复杂条件
                if (line.Count(c => c == '&' || c == '|') > 2)
                {
                    suggestions.Add(("Readability", lineNum, "Complex condition, consider extracting to boolean variable or method"));
                }
            }

            if (type is CodeSimplifyType.All or CodeSimplifyType.Complexity)
            {
                // 检测嵌套层级（简单检测缩进）
                var indent = line.TakeWhile(char.IsWhiteSpace).Count();
                if (indent > 16)
                {
                    suggestions.Add(("Complexity", lineNum, "Deep nesting, consider early returns or extracting methods"));
                }
            }

            // C# 特定建议
            if (extension == ".cs")
            {
                if (type is CodeSimplifyType.All or CodeSimplifyType.Performance)
                {
                    // 检测 string concatenation in loop
                    if (line.Contains("for") && line.Contains("+") && line.Contains("\""))
                    {
                        suggestions.Add(("Performance", lineNum, "String concatenation in loop, consider using StringBuilder"));
                    }
                }
            }
        }

        return suggestions.Take(20).ToList();
    }

    private async Task<(bool Success, string Message)> CheckSyntaxAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".cs" => await CheckCSharpSyntaxAsync(path, cancellationToken).ConfigureAwait(false),
            ".py" => await CheckPythonSyntaxAsync(path, cancellationToken).ConfigureAwait(false),
            ".js" or ".ts" => await CheckJavaScriptSyntaxAsync(path, cancellationToken).ConfigureAwait(false),
            _ => (true, "Cannot check syntax for this file type")
        };
    }

    private async Task<(bool Success, string Message)> CheckCSharpSyntaxAsync(string path, CancellationToken cancellationToken)
    {
        if (_fs.DirectoryExists(path))
        {
            // 检查项目
            var result = await _shellExecutionService.ExecuteAsync(
                "dotnet build --verbosity quiet --no-restore",
                workingDirectory: path,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return (result.Success, result.Success ? "Syntax check passed" : result.Stderr ?? "Build failed");
        }
        else
        {
            // 检查单个文件
            return (true, "Single file syntax check requires full project context");
        }
    }

    private async Task<(bool Success, string Message)> CheckPythonSyntaxAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _shellExecutionService.ExecuteAsync(
            $"python -m py_compile \"{path}\"",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (result.Success, result.Success ? "Syntax check passed" : result.Stderr ?? "Syntax error");
    }

    private async Task<(bool Success, string Message)> CheckJavaScriptSyntaxAsync(string path, CancellationToken cancellationToken)
    {
        // 尝试使用 node 检查语法
        var result = await _shellExecutionService.ExecuteAsync(
            $"node --check \"{path}\"",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (result.Success, result.Success ? "Syntax check passed" : result.Stderr ?? "Syntax error");
    }

    private async Task<(bool Success, string Message)> CheckBuildAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _shellExecutionService.ExecuteAsync(
            "dotnet build --verbosity quiet",
            workingDirectory: path,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (result.Success, result.Success ? "Build succeeded" : "Build failed");
    }

    private async Task<(bool Success, string Message)> RunTestsAsync(string path, string? filter, CancellationToken cancellationToken)
    {
        var filterArg = !string.IsNullOrEmpty(filter) ? $" --filter \"{filter}\"" : "";
        var result = await _shellExecutionService.ExecuteAsync(
            $"dotnet test --verbosity quiet{filterArg}",
            workingDirectory: path,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (result.Success, result.Success ? "Tests passed" : "Tests failed");
    }

    private string AnalyzeError(string errorMessage)
    {
        var analysis = new System.Text.StringBuilder();

        // 常见错误模式识别
        if (errorMessage.Contains("NullReferenceException") || errorMessage.Contains("null reference"))
        {
            analysis.AppendLine("- Null reference exception detected");
            analysis.AppendLine("  Possible cause: object used before initialization");
            analysis.AppendLine("  Suggestion: add null checks or use null-coalescing operator");
        }
        else if (errorMessage.Contains("IndexOutOfRange") || errorMessage.Contains("索引超出范围"))
        {
            analysis.AppendLine("- Index out of range detected");
            analysis.AppendLine("  Possible cause: array/list index exceeded bounds");
            analysis.AppendLine("  Suggestion: check index bounds, use Count/Length for validation");
        }
        else if (errorMessage.Contains("FileNotFound") || errorMessage.Contains("找不到文件"))
        {
            analysis.AppendLine("- File not found detected");
            analysis.AppendLine("  Possible cause: incorrect file path or file does not exist");
            analysis.AppendLine("  Suggestion: verify file path, check working directory");
        }
        else if (errorMessage.Contains("timeout") || errorMessage.Contains("超时"))
        {
            analysis.AppendLine("- Timeout detected");
            analysis.AppendLine("  Possible cause: operation took too long or deadlock");
            analysis.AppendLine("  Suggestion: check async operations, optimize performance, increase timeout");
        }
        else
        {
            analysis.AppendLine("- Unrecognized error type");
            analysis.AppendLine("  Suggestion: review full stack trace, search error message");
        }

        return analysis.ToString();
    }

    private async Task<IReadOnlyList<string>> GetDebugSuggestionsAsync(string path, CodeDebugType type, string? errorMessage, CancellationToken cancellationToken)
    {
        var suggestions = new List<string>();

        // 检查日志文件
        var logFiles = _fs.GetFiles(path, "*.log", SearchOption.AllDirectories).Take(5).ToList();
        if (logFiles.Any())
        {
            suggestions.Add($"Found {logFiles.Count} log files, review for detailed error information");
        }

        // 检查配置文件
        if (type is CodeDebugType.All or CodeDebugType.Error)
        {
            var configFiles = new[] { WorkflowConstants.FileExtensions.AppSettings, WorkflowConstants.FileExtensions.WebConfig, WorkflowConstants.FileExtensions.Env };
            var missingConfigs = new List<string>();
            foreach (var configFile in configFiles)
            {
                var configPath = Path.Combine(path, configFile);
                var configResult = await _fileOperationService.ReadFileAsync(configPath, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!configResult.Success)
                {
                    missingConfigs.Add(configFile);
                }
            }
            if (missingConfigs.Count > 0)
            {
                suggestions.Add("Check if configuration files exist and are configured correctly");
            }
        }

        // 检查依赖
        var nodeModulesResult = await _fileOperationService.ListDirectoryAsync(Path.Combine(path, "node_modules"), cancellationToken: cancellationToken).ConfigureAwait(false);
        var packageJsonResult = await _fileOperationService.ReadFileAsync(Path.Combine(path, "package.json"), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (nodeModulesResult.Success || packageJsonResult.Success)
        {
            suggestions.Add("Node.js project: try 'npm install' to ensure dependencies are complete");
        }

        var packagesConfigResult = await _fileOperationService.ReadFileAsync(Path.Combine(path, "packages.config"), cancellationToken: cancellationToken).ConfigureAwait(false);
        var csprojFiles = _fs.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        if (packagesConfigResult.Success || csprojFiles.Length > 0)
        {
            suggestions.Add(".NET project: try 'dotnet restore' to restore NuGet packages");
        }

        return suggestions;
    }

    private IReadOnlyList<string> FindFiles(string pattern, bool recursive)
    {
        try
        {
            var directory = _fs.GetCurrentDirectory();
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // 处理 **/ 前缀（递归所有子目录）
            if (pattern.StartsWith("**/"))
            {
                pattern = pattern[3..];
                searchOption = SearchOption.AllDirectories;
            }

            return _fs.GetFiles(directory, pattern, searchOption).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<(bool Success, string Message)> ExecuteBatchOperationAsync(
        string file, string operation, string? search, string? replace, CancellationToken cancellationToken)
    {
        var op = BatchOperationTypeExtensions.FromValue(operation) ?? throw new ArgumentException($"Unsupported operation: {operation}");
        return op switch
        {
            BatchOperationType.Count => (true, "File counted"),
            BatchOperationType.Search => await SearchInFileAsync(file, search, cancellationToken).ConfigureAwait(false),
            BatchOperationType.Replace => await ReplaceInFileAsync(file, search, replace, cancellationToken).ConfigureAwait(false),
            BatchOperationType.Delete => await DeleteFileAsync(file, cancellationToken).ConfigureAwait(false),
            _ => (false, $"Unsupported operation: {operation}")
        };
    }

    private async Task<(bool Success, string Message)> SearchInFileAsync(string file, string? search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(search))
        {
            return (false, "search parameter cannot be empty");
        }

        var readResult = await _fileOperationService.ReadFileAsync(file, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!readResult.Success)
        {
            return (false, $"Cannot read file: {file}");
        }

        var content = readResult.Content;
        var count = content.Split(new[] { search }, StringSplitOptions.None).Length - 1;

        return (true, $"Found {count} matches");
    }

    private async Task<(bool Success, string Message)> ReplaceInFileAsync(string file, string? search, string? replace, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(search))
        {
            return (false, "search parameter cannot be empty");
        }

        var editResult = await _fileOperationService.EditFileAsync(
            file,
            search,
            replace ?? "",
            replaceAll: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!editResult.Success)
        {
            return (false, editResult.ErrorMessage ?? "Replace failed");
        }

        if (editResult.ReplaceCount == 0)
        {
            return (true, "No content to replace");
        }

        return (true, $"Replaced {editResult.ReplaceCount} occurrences");
    }

    private async Task<(bool Success, string Message)> DeleteFileAsync(string file, CancellationToken cancellationToken)
    {
        var success = await _fileOperationService.DeleteFileAsync(file, cancellationToken).ConfigureAwait(false);
        return success
            ? (true, "File deleted")
            : (false, $"Failed to delete file: {file}");
    }

    private IReadOnlyList<(string Title, string Description)> GetStuckSuggestions(string? approach, string? obstacle)
    {
        var suggestions = new List<(string Title, string Description)>
        {
            ("Try a different approach", "If current approach doesn't work, consider alternative algorithms or techniques"),
            ("Simplify the problem", "Create a minimal reproducible example to isolate from complex factors"),
            ("Review documentation", "Re-read relevant docs, may have missed important details or config"),
            ("Take a break", "Step away temporarily, may gain new perspective on return"),
            ("Seek help", "Describe the problem to peers or community, explaining may reveal insights"),
            ("Check assumptions", "List all assumptions, verify each one"),
            ("Revert to last working version", "If it worked before, compare diffs to find the issue")
        };

        // 根据障碍添加特定建议（优先插入到头部）
        var prioritySuggestions = new List<(string Title, string Desc)>();
        if (obstacle?.Contains("permission", StringComparison.OrdinalIgnoreCase) == true ||
            obstacle?.Contains("权限", StringComparison.OrdinalIgnoreCase) == true)
        {
            prioritySuggestions.Add(("Check permissions", "Confirm current user has sufficient permissions"));
        }

        if (obstacle?.Contains("network", StringComparison.OrdinalIgnoreCase) == true ||
            obstacle?.Contains("网络", StringComparison.OrdinalIgnoreCase) == true)
        {
            prioritySuggestions.Add(("Check network", "Verify network connection, try proxy or VPN"));
        }

        if (obstacle?.Contains("memory", StringComparison.OrdinalIgnoreCase) == true ||
            obstacle?.Contains("内存", StringComparison.OrdinalIgnoreCase) == true)
        {
            prioritySuggestions.Add(("Optimize memory usage", "Check for memory leaks, consider batch processing for large data"));
        }

        // 优先建议在前，基础建议在后
        if (prioritySuggestions.Count > 0)
        {
            suggestions = prioritySuggestions.Concat(suggestions).ToList();
        }

        return suggestions;
    }

    #endregion
}
