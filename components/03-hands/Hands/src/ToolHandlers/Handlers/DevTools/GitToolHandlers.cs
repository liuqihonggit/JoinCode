


namespace Tools.Handlers;

[McpToolHandler(ToolCategory.Git)]
public partial class GitToolHandlers
{
    [Inject] private readonly ILogger<GitToolHandlers>? _logger;
    [Inject] private readonly IProcessService _processService;
    private readonly IGitSecurityInterceptor? _securityInterceptor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private string? _currentWorkingDirectory;

    public GitToolHandlers(IFileSystem fs, IProcessService processService, ILogger<GitToolHandlers>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public GitToolHandlers(IFileSystem fs, IProcessService processService, IGitSecurityInterceptor securityInterceptor, ILogger<GitToolHandlers>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _securityInterceptor = securityInterceptor;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public void SetWorkingDirectory(string directory)
    {
        _currentWorkingDirectory = directory;
    }

    [McpTool(GitToolNameConstants.GitStatus, "Check Git repository status", "git")]
    public async Task<ToolResult> GitStatusAsync(
        [McpToolParameter("Working directory path (optional, defaults to current directory)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { GitSubCommand.Status.ToValue(), "--porcelain", "-b" };
        var result = await ExecuteGitCommandAsync(GitSubCommand.Status, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ResultBuilder.Error()
                .WithText($"Git status failed:\n{result.Error}")
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

        return ResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
    }

    [McpTool(GitToolNameConstants.GitAdd, "Add files to staging area", "git")]
    public async Task<ToolResult> GitAddAsync(
        [McpToolParameter("File path (supports wildcards *, use . for all files)")] string path,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResultBuilder.Error()
                .WithText("path cannot be empty")
                .Build();
        }

        var parts = new List<string> { GitSubCommand.Add.ToValue(), $"\"{path}\"" };
        var result = await ExecuteGitCommandAsync(GitSubCommand.Add, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ResultBuilder.Error()
                .WithText($"Git add failed:\n{result.Error}")
                .Build();
        }

        var securityResult = await ScanBeforeCommitAsync(working_dir, cancellationToken).ConfigureAwait(false);
        if (securityResult != null)
            return securityResult;

        return ResultBuilder.Success()
            .WithText($"Added: {path}")
            .Build();
    }

    [McpTool(GitToolNameConstants.GitCommit, "Commit staged changes", "git")]
    public async Task<ToolResult> GitCommitAsync(
        [McpToolParameter("Commit message")] string message,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Allow empty commit", Required = false)] bool? allow_empty = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ResultBuilder.Error()
                .WithText("message cannot be empty")
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
            return ResultBuilder.Error()
                .WithText($"Git commit failed:\n{result.Error}")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText($"Commit successful:\n{result.Output}")
            .Build();
    }

    [McpTool(GitToolNameConstants.GitPush, "Push to remote repository", "git")]
    public async Task<ToolResult> GitPushAsync(
        [McpToolParameter("Remote name (optional, defaults to origin)", Required = false)] string? remote = "origin",
        [McpToolParameter("Branch name (optional, defaults to current branch)", Required = false)] string? branch = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Force push", Required = false)] bool? force = false,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { GitSubCommand.Push.ToValue(), remote! };
        if (!string.IsNullOrEmpty(branch))
        {
            parts.Add(branch!);
        }
        if (force == true)
        {
            parts.Add("--force");
        }

        var result = await ExecuteGitCommandAsync(GitSubCommand.Push, string.Join(' ', parts), working_dir, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ResultBuilder.Error()
                .WithText($"Git push failed:\n{result.Error}")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText($"Push successful:\n{result.Output}")
            .Build();
    }

    [McpTool(GitToolNameConstants.GitPull, "Pull from remote repository", "git")]
    public async Task<ToolResult> GitPullAsync(
        [McpToolParameter("Remote name (optional, defaults to origin)", Required = false)] string? remote = "origin",
        [McpToolParameter("Branch name (optional, defaults to current branch)", Required = false)] string? branch = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { GitSubCommand.Pull.ToValue(), remote! };
        if (!string.IsNullOrEmpty(branch))
        {
            parts.Add(branch!);
        }
        var args = string.Join(' ', parts);

        var result = await ExecuteGitCommandAsync(GitSubCommand.Pull, args, working_dir, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ResultBuilder.Error()
                .WithText($"Git pull failed:\n{result.Error}")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText($"Pull successful:\n{result.Output}")
            .Build();
    }

    [McpTool(GitToolNameConstants.GitLog, "View commit history", "git")]
    public async Task<ToolResult> GitLogAsync(
        [McpToolParameter("Number of entries (optional, defaults to 10)", Required = false)] int? count = 10,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Format: oneline/short/full (optional)", Required = false)] string? format = "oneline",
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.ValidateRange(count, 1, 1000, "count");
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
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
            return ResultBuilder.Error()
                .WithText($"Git log failed:\n{result.Error}")
                .Build();
        }

        var response = new StringBuilder();
        response.AppendLine("Commit history:");
        response.AppendLine();
        response.AppendLine(result.Output);

        return ResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
    }

    [McpTool(GitToolNameConstants.GitDiff, "View file differences", "git")]
    public async Task<ToolResult> GitDiffAsync(
        [McpToolParameter("File path (optional, defaults to all files)", Required = false)] string? path = null,
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Compare mode: staged/cached/worktree (optional)", Required = false)] string? mode = "worktree",
        CancellationToken cancellationToken = default)
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
            return ResultBuilder.Error()
                .WithText($"Git diff failed:\n{result.Error}")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            return ResultBuilder.Success()
                .WithText("No differences")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText(result.Output)
            .Build();
    }

    [McpTool(GitToolNameConstants.GitBranch, "Create or switch branch", "git")]
    public async Task<ToolResult> GitBranchAsync(
        [McpToolParameter("Branch name")] string branch_name,
        [McpToolParameter("Operation: create/switch/delete (optional, defaults to switch)", Required = false)] string? operation = "switch",
        [McpToolParameter("Working directory path (optional)", Required = false)] string? working_dir = null,
        [McpToolParameter("Base branch (optional, for create)", Required = false)] string? base_branch = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch_name))
        {
            return ResultBuilder.Error()
                .WithText("branch_name cannot be empty")
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
                return ResultBuilder.Error()
                    .WithText($"Unsupported operation: {operation}")
                    .Build();
        }

        var result = await ExecuteGitCommandAsync(op == GitBranchOperation.Switch ? GitSubCommand.Switch : GitSubCommand.Branch, args, working_dir, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ResultBuilder.Error()
                .WithText($"Git branch {op.ToValue()} failed:\n{result.Error}")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText($"Branch '{op.ToValue()}' successful: {branch_name}")
            .Build();
    }

    [McpTool(GitToolNameConstants.GitClone, "Clone remote repository", "git")]
    public async Task<ToolResult> GitCloneAsync(
        [McpToolParameter("Repository URL")] string url,
        [McpToolParameter("Local directory name (optional)", Required = false)] string? directory = null,
        [McpToolParameter("Parent directory path (optional, defaults to current directory)", Required = false)] string? parent_dir = null,
        [McpToolParameter("Branch (optional)", Required = false)] string? branch = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ResultBuilder.Error()
                .WithText("url cannot be empty")
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
            return ResultBuilder.Error()
                .WithText($"Git clone failed:\n{result.Error}")
                .Build();
        }

        return ResultBuilder.Success()
            .WithText($"Clone successful:\n{result.Output}")
            .Build();
    }

    private void RecordGitMetrics(string command, bool isSuccess)
        => _telemetryService?.RecordCount("git.operation.count", new Dictionary<string, string> { ["command"] = command, ["success"] = isSuccess.ToString() }, description: "Git operation count");

    private async Task<ToolResult?> ScanBeforeCommitAsync(string? workingDir, CancellationToken ct)
    {
        if (_securityInterceptor == null)
            return null;

        var cwd = workingDir ?? _currentWorkingDirectory ?? _fs.GetCurrentDirectory();
        var scanResult = await _securityInterceptor.ScanBeforeCommitAsync(cwd, ct).ConfigureAwait(false);

        if (!scanResult.IsBlocked)
            return null;

        return ResultBuilder.Error()
            .WithText(scanResult.FormatReport())
            .Build();
    }

    private async Task<GitResult> ExecuteGitCommandAsync(GitSubCommand subCommand, string arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var cwd = workingDirectory ?? _currentWorkingDirectory ?? _fs.GetCurrentDirectory();

        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = cwd,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            RecordGitMetrics(subCommand.ToValue(), result.Success);
            return new GitResult
            {
                Success = result.Success,
                Output = result.StandardOutput.Trim(),
                Error = result.StandardError.Trim(),
                ExitCode = result.ExitCode
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行 Git 命令失败: {Arguments}", arguments);
            RecordGitMetrics(subCommand.ToValue(), false);
            return new GitResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    private sealed record GitResult
    {
        public required bool Success { get; init; }
        public required string Output { get; init; }
        public required string Error { get; init; }
        public required int ExitCode { get; init; }
    }
}
