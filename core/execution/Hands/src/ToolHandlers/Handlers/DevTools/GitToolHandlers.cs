


namespace Tools.Handlers;

[McpToolDispatch(ToolCategory.Git)]
public partial class GitToolHandlers
{
    private readonly ILogger<GitToolHandlers>? _logger;
    private readonly IGitCommandRunner _gitRunner;
    private readonly IGitSecurityInterceptor? _securityInterceptor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private string? _currentWorkingDirectory;

    public GitToolHandlers(IFileSystem fs, IGitCommandRunner gitRunner, ILogger<GitToolHandlers>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public GitToolHandlers(IFileSystem fs, IGitCommandRunner gitRunner, IGitSecurityInterceptor securityInterceptor, ILogger<GitToolHandlers>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _securityInterceptor = securityInterceptor;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public void SetWorkingDirectory(string directory)
    {
        _currentWorkingDirectory = directory;
    }

    [McpTool(GitToolNameConstants.GitStatus, "Check Git repository status", "git", ConcurrencySafe = true)]
    public async Task<ToolResult> GitStatusAsync(
        [McpToolParameter("Working directory path (optional, defaults to current directory)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parts = new List<string> { GitSubCommand.Status.ToValue(), "--porcelain", "-b" };
            var result = await ExecuteGitCommandAsync(GitSubCommand.Status, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitStatusFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            var response = new StringBuilder();
            response.AppendLine("Git status:");
            response.AppendLine();

            if (string.IsNullOrWhiteSpace(result.Output))
            {
                response.AppendLine("Working tree clean, no changes");
            }
            else
            {
                response.AppendLine(result.Output);
            }

            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_status", ex, _logger, "working_dir", working_dir ?? "(default)");
        }
    }

    [McpTool(GitToolNameConstants.GitAdd, "Add files to staging area", "git")]
    public async Task<ToolResult> GitAddAsync(
        [McpToolParameter("File path (supports wildcards *, use . for all files)")] string path,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var pathDiag = BuildPathEmptyDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(pathDiag.FormattedMessage)
                    .WithDiagnostic(pathDiag)
                    .Build();
            }

            var parts = new List<string> { GitSubCommand.Add.ToValue(), $"\"{path}\"" };
            var result = await ExecuteGitCommandAsync(GitSubCommand.Add, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitAddFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            var securityResult = await ScanBeforeCommitAsync(working_dir, cancellationToken).ConfigureAwait(false);
            if (securityResult != null)
                return securityResult;

            return ToolResultBuilder.Success()
                .WithText($"Added: {path}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_add", ex, _logger, "path", path ?? "(null)");
        }
    }

    [McpTool(GitToolNameConstants.GitCommit, "Commit staged changes", "git")]
    public async Task<ToolResult> GitCommitAsync(
        [McpToolParameter("Commit message")] string message,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Allow empty commit", Required = false)] bool? allow_empty = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                var msgDiag = BuildMessageEmptyDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(msgDiag.FormattedMessage)
                    .WithDiagnostic(msgDiag)
                    .Build();
            }

            var securityResult = await ScanBeforeCommitAsync(working_dir, cancellationToken).ConfigureAwait(false);
            if (securityResult != null)
                return securityResult;

            var escapedMessage = message.Replace("\"", "\\\"");
            var parts = new List<string> { GitSubCommand.Commit.ToValue(), "-m", $"\"{escapedMessage}\"" };
            if (allow_empty == true)
            {
                parts.Add("--allow-empty");
            }

            var result = await ExecuteGitCommandAsync(GitSubCommand.Commit, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitCommitFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText($"Commit successful:\n{result.Output}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_commit", ex, _logger, "working_dir", working_dir ?? "(default)");
        }
    }

    [McpTool(GitToolNameConstants.GitPush, "Push to remote repository", "git")]
    public async Task<ToolResult> GitPushAsync(
        [McpToolParameter("Remote name (optional, defaults to origin)", Required = false)] string? remote = "origin",
        [McpToolParameter("Branch name (optional, defaults to current branch)", Required = false)] string? branch = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Force push", Required = false)] bool? force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parts = new List<string> { GitSubCommand.Push.ToValue(), remote ?? "origin" };
            if (!string.IsNullOrEmpty(branch))
            {
                parts.Add(branch);
            }
            if (force == true)
            {
                parts.Add("--force");
            }

            var result = await ExecuteGitCommandAsync(GitSubCommand.Push, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitPushFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText($"Push successful:\n{result.Output}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_push", ex, _logger, "remote", remote ?? "origin");
        }
    }

    [McpTool(GitToolNameConstants.GitPull, "Pull from remote repository", "git")]
    public async Task<ToolResult> GitPullAsync(
        [McpToolParameter("Remote name (optional, defaults to origin)", Required = false)] string? remote = "origin",
        [McpToolParameter("Branch name (optional, defaults to current branch)", Required = false)] string? branch = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parts = new List<string> { GitSubCommand.Pull.ToValue(), remote ?? "origin" };
            if (!string.IsNullOrEmpty(branch))
            {
                parts.Add(branch);
            }
            var args = string.Join(' ', parts);

            var result = await ExecuteGitCommandAsync(GitSubCommand.Pull, args, working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitPullFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText($"Pull successful:\n{result.Output}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_pull", ex, _logger, "remote", remote ?? "origin");
        }
    }

    [McpTool(GitToolNameConstants.GitLog, "View commit history", "git", ConcurrencySafe = true)]
    public async Task<ToolResult> GitLogAsync(
        [McpToolParameter("Number of entries (optional, defaults to 10)", Required = false)] int? count = 10,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Format: oneline/short/full (optional)", Required = false)] string? format = "oneline",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationError = ValidationHelper.ValidateRange(count, 1, 1000, "count");
            if (validationError != null)
            {
                var validationDiag = BuildGitLogValidationDiagnostic(validationError);
                return ToolResultBuilder.Error()
                    .WithText(validationDiag.FormattedMessage)
                    .WithDiagnostic(validationDiag)
                    .Build();
            }

            var formatArg = format?.ToLowerInvariant() switch
            {
                "oneline" => "--oneline",
                "short" => "--pretty=format:%h - %s (%ar) <%an>",
                "full" => "--pretty=format:%H%nAuthor: %an <%ae>%nDate: %ad%n%n%s%n%b",
                _ => "--oneline"
            };

            var parts = new List<string> { GitSubCommand.Log.ToValue(), formatArg, "-n", $"{count ?? 10}" };
            var result = await ExecuteGitCommandAsync(GitSubCommand.Log, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitLogFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            var response = new StringBuilder();
            response.AppendLine("Commit history:");
            response.AppendLine();
            response.AppendLine(result.Output);

            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_log", ex, _logger, "working_dir", working_dir ?? "(default)");
        }
    }

    [McpTool(GitToolNameConstants.GitDiff, "View file differences", "git", ConcurrencySafe = true)]
    public async Task<ToolResult> GitDiffAsync(
        [McpToolParameter("File path (optional, defaults to all files)", Required = false)] string? path = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Compare mode: staged/cached/worktree (optional)", Required = false)] string? mode = "worktree",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parts = new List<string> { GitSubCommand.Diff.ToValue() };
            if (mode?.ToLowerInvariant() is "staged" or "cached")
            {
                parts.Add("--cached");
            }
            if (!string.IsNullOrEmpty(path))
            {
                parts.Add($"\"{path}\"");
            }

            var result = await ExecuteGitCommandAsync(GitSubCommand.Diff, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitDiffFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            if (string.IsNullOrWhiteSpace(result.Output))
            {
                var diffMsg = $"No differences\n[诊断] mode: {mode}, path: {path ?? "(all files)"}";
                return ToolResultBuilder.Success()
                    .WithText(diffMsg)
                    .WithDiagnostic(ToolDiagnostic.Create("NoDifferences", diffMsg,
                        [new DiagnosticDetail("mode", mode ?? "worktree"), new DiagnosticDetail("path", path ?? "(all files)")],
                        ["工作区与比较基准无差异，可能已经是最新状态。"]))
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText(result.Output)
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_diff", ex, _logger, "working_dir", working_dir ?? "(default)");
        }
    }

    [McpTool(GitToolNameConstants.GitBranch, "Create or switch branch", "git")]
    public async Task<ToolResult> GitBranchAsync(
        [McpToolParameter("Branch name")] string branch_name,
        [McpToolParameter("Operation: create/switch/delete (optional, defaults to switch)", Required = false)] string? operation = "switch",
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Base branch (optional, for create)", Required = false)] string? base_branch = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(branch_name))
            {
                var nameDiag = BuildBranchNameEmptyDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(nameDiag.FormattedMessage)
                    .WithDiagnostic(nameDiag)
                    .Build();
            }

            var opStr = operation?.ToLowerInvariant() ?? GitBranchOperationConstants.Switch;
            var op = GitBranchOperationExtensions.FromValue(opStr) ?? GitBranchOperation.Switch;

            string args;
            switch (op)
            {
                case GitBranchOperation.Create:
                {
                    var parts = new List<string> { GitSubCommand.Branch.ToValue(), $"\"{branch_name}\"" };
                    if (!string.IsNullOrEmpty(base_branch))
                    {
                        parts.Add($"\"{base_branch}\"");
                    }
                    args = string.Join(' ', parts);
                    break;
                }
                case GitBranchOperation.Switch:
                    args = $"{GitSubCommand.Switch.ToValue()} \"{branch_name}\"";
                    break;
                case GitBranchOperation.Delete:
                    args = $"{GitSubCommand.Branch.ToValue()} -d \"{branch_name}\"";
                    break;
                default:
                {
                    var opDiag = BuildUnsupportedBranchOperationDiagnostic(operation);
                    return ToolResultBuilder.Error()
                        .WithText(opDiag.FormattedMessage)
                        .WithDiagnostic(opDiag)
                        .Build();
                }
            }

            var result = await ExecuteGitCommandAsync(op == GitBranchOperation.Switch ? GitSubCommand.Switch : GitSubCommand.Branch, args, working_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitBranchFailedDiagnostic(op.ToValue(), result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText($"Branch '{op.ToValue()}' successful: {branch_name}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_branch", ex, _logger, "branch_name", branch_name ?? "(null)");
        }
    }

    [McpTool(GitToolNameConstants.GitClone, "Clone remote repository", "git")]
    public async Task<ToolResult> GitCloneAsync(
        [McpToolParameter("Repository URL")] string url,
        [McpToolParameter("Local directory name (optional)", Required = false)] string? directory = null,
        [McpToolParameter("Parent directory path (optional, defaults to current directory)", Required = false)] string? parent_dir = null,
        [McpToolParameter("Branch (optional)", Required = false)] string? branch = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                var urlDiag = BuildUrlEmptyDiagnostic();
                return ToolResultBuilder.Error()
                    .WithText(urlDiag.FormattedMessage)
                    .WithDiagnostic(urlDiag)
                    .Build();
            }

            var parts = new List<string> { GitSubCommand.Clone.ToValue(), $"\"{url}\"" };
            if (!string.IsNullOrEmpty(directory))
            {
                parts.Add($"\"{directory}\"");
            }
            if (!string.IsNullOrEmpty(branch))
            {
                parts.Add($"-b \"{branch}\"");
            }
            var args = string.Join(' ', parts);

            var result = await ExecuteGitCommandAsync(GitSubCommand.Clone, args, parent_dir, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                var diag = BuildGitCloneFailedDiagnostic(result.Error);
                return ToolResultBuilder.Error()
                    .WithText(diag.FormattedMessage)
                    .WithDiagnostic(diag)
                    .Build();
            }

            return ToolResultBuilder.Success()
                .WithText($"Clone successful:\n{result.Output}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("git_clone", ex, _logger, "url", url ?? "(null)");
        }
    }

    private void RecordGitMetrics(string command, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "git.operation.count", command, isSuccess);

    private async Task<ToolResult?> ScanBeforeCommitAsync(string? workingDir, CancellationToken ct)
    {
        if (_securityInterceptor == null)
            return null;

        var cwd = workingDir ?? _currentWorkingDirectory ?? _fs.GetCurrentDirectory();
        var scanResult = await _securityInterceptor.ScanBeforeCommitAsync(cwd, ct).ConfigureAwait(false);

        if (!scanResult.IsBlocked)
            return null;

        var scanDiag = BuildSecurityScanBlockedDiagnostic(scanResult.FormatReport());
        return ToolResultBuilder.Error()
            .WithText(scanDiag.FormattedMessage)
            .WithDiagnostic(scanDiag)
            .Build();
    }

    private async Task<GitCommandResult> ExecuteGitCommandAsync(GitSubCommand subCommand, string arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var cwd = workingDirectory ?? _currentWorkingDirectory ?? _fs.GetCurrentDirectory();

        var result = await _gitRunner.ExecuteAsync(arguments, cwd, cancellationToken).ConfigureAwait(false);
        RecordGitMetrics(subCommand.ToValue(), result.Success);

        if (!result.Success)
        {
            _logger?.LogError("执行 Git 命令失败: git {Arguments}, Error: {Error}", arguments, result.Error);
        }

        return new GitCommandResult
        {
            Success = result.Success,
            Output = result.Output.Trim(),
            Error = result.Error.Trim(),
            ExitCode = result.ExitCode
        };
    }

    #region Diagnostic Builders

    /// <summary>
    /// 构建 git status 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitStatusFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitStatusFailed",
            formattedMessage: $"Git status failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认当前目录是 Git 仓库（存在 .git 目录）。",
                "检查 Git 是否已安装并可用。",
            ]);
    }

    /// <summary>
    /// 构建 git add 路径为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildPathEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GitPathEmpty",
            formattedMessage: "path cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "path"),
            ],
            suggestions:
            [
                "提供要添加的文件路径，使用 . 表示所有文件。",
            ]);
    }

    /// <summary>
    /// 构建 git add 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitAddFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitAddFailed",
            formattedMessage: $"Git add failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认文件路径存在且可访问。",
                "检查是否有权限写入暂存区。",
            ]);
    }

    /// <summary>
    /// 构建 git commit 消息为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildMessageEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GitMessageEmpty",
            formattedMessage: "message cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "message"),
            ],
            suggestions:
            [
                "提供有意义的提交消息描述本次变更。",
            ]);
    }

    /// <summary>
    /// 构建 git commit 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitCommitFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitCommitFailed",
            formattedMessage: $"Git commit failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认暂存区有内容（先执行 git add）。",
                "检查是否有 pre-commit 钩子阻止提交。",
            ]);
    }

    /// <summary>
    /// 构建 git push 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitPushFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitPushFailed",
            formattedMessage: $"Git push failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认远程仓库配置正确且有推送权限。",
                "先执行 git pull 同步远程变更后再重试。",
            ]);
    }

    /// <summary>
    /// 构建 git pull 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitPullFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitPullFailed",
            formattedMessage: $"Git pull failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认远程仓库配置正确且有拉取权限。",
                "检查本地是否有未提交的冲突变更。",
            ]);
    }

    /// <summary>
    /// 构建 git log 参数校验失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitLogValidationDiagnostic(string validationError)
    {
        return ToolDiagnostic.Create(
            reason: "GitLogValidationError",
            formattedMessage: validationError,
            details:
            [
                new DiagnosticDetail("Error", validationError),
            ],
            suggestions:
            [
                "修正 count 参数使其落在合法范围内。",
            ]);
    }

    /// <summary>
    /// 构建 git log 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitLogFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitLogFailed",
            formattedMessage: $"Git log failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认当前目录是 Git 仓库且存在提交历史。",
            ]);
    }

    /// <summary>
    /// 构建 git diff 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitDiffFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitDiffFailed",
            formattedMessage: $"Git diff failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认当前目录是 Git 仓库。",
                "检查指定的比较模式和文件路径是否有效。",
            ]);
    }

    /// <summary>
    /// 构建 git branch 分支名为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildBranchNameEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GitBranchNameEmpty",
            formattedMessage: "branch_name cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "branch_name"),
            ],
            suggestions:
            [
                "提供有效的分支名称。",
            ]);
    }

    /// <summary>
    /// 构建 git branch 不支持操作的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildUnsupportedBranchOperationDiagnostic(string? operation)
    {
        return ToolDiagnostic.Create(
            reason: "GitUnsupportedOperation",
            formattedMessage: $"Unsupported operation: {operation}",
            details:
            [
                new DiagnosticDetail("Operation", operation ?? "(null)"),
            ],
            suggestions:
            [
                "使用 create、switch 或 delete 操作之一。",
            ]);
    }

    /// <summary>
    /// 构建 git branch 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitBranchFailedDiagnostic(string operation, string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitBranchFailed",
            formattedMessage: $"Git branch {operation} failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Operation", operation),
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认分支名称合法且不存在命名冲突。",
                "检查目标分支是否存在（switch/delete 场景）。",
            ]);
    }

    /// <summary>
    /// 构建 git clone URL 为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildUrlEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "GitUrlEmpty",
            formattedMessage: "url cannot be empty",
            details:
            [
                new DiagnosticDetail("Param", "url"),
            ],
            suggestions:
            [
                "提供有效的仓库 URL（HTTPS 或 SSH）。",
            ]);
    }

    /// <summary>
    /// 构建 git clone 命令失败的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildGitCloneFailedDiagnostic(string error)
    {
        return ToolDiagnostic.Create(
            reason: "GitCloneFailed",
            formattedMessage: $"Git clone failed:\n{error}",
            details:
            [
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "确认 URL 可访问且具有克隆权限。",
                "检查本地目标目录是否已存在同名目录。",
            ]);
    }

    /// <summary>
    /// 构建提交前安全扫描被阻止的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildSecurityScanBlockedDiagnostic(string report)
    {
        return ToolDiagnostic.Create(
            reason: "GitSecurityScanBlocked",
            formattedMessage: report,
            details:
            [
                new DiagnosticDetail("Report", report),
            ],
            suggestions:
            [
                "从暂存区移除被阻止的敏感文件后重试。",
                "确认安全拦截器的策略是否需要调整。",
            ]);
    }

    #endregion
}
