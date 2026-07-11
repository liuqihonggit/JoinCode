
namespace Core.Agents;

[Register(typeof(IAgentWorktreeService))]
[Register(typeof(IWorktreePipelineOperations))]
public sealed partial class AgentWorktreeService : IAgentWorktreeService, IWorktreePipelineOperations, IAsyncDisposable {
    [Inject] private readonly ILogger<AgentWorktreeService>? _logger;
    [Inject] private readonly IClockService _clock;
    [Inject] private readonly IProcessService _processService;
    private readonly IFileOperationService _fileOperationService;
    private readonly WorktreeOptions _defaultOptions;
    private readonly ITelemetryService? _telemetryService;
    private readonly Dictionary<string, AgentWorktreeSession> _sessions = new();
    private readonly SemaphoreSlim _sessionLock;
    private readonly MiddlewarePipeline<WorktreeCreateContext>? _createPipeline;

    public AgentWorktreeService(
        IFileOperationService fileOperationService,
        IProcessService processService,
        IEnumerable<IWorktreeCreateMiddleware>? createMiddlewares = null,
        ILogger<AgentWorktreeService>? logger = null,
        WorktreeOptions? defaultOptions = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _defaultOptions = defaultOptions ?? new WorktreeOptions();
        _telemetryService = telemetryService;
        _sessionLock = new SemaphoreSlim(1, 1);
        if (createMiddlewares != null)
        {
            _createPipeline = new MiddlewarePipeline<WorktreeCreateContext>(createMiddlewares);
        }
    }

    public async Task<WorktreeCreateResult> CreateAgentWorktreeAsync(
        string agentId,
        string? gitRootPath = null,
        WorktreeOptions? options = null,
        CancellationToken cancellationToken = default) {
        if (_createPipeline != null)
        {
            var context = new WorktreeCreateContext
            {
                AgentId = agentId,
                GitRootPath = gitRootPath,
                Options = options ?? _defaultOptions,
                CancellationToken = cancellationToken
            };

            var span = _telemetryService?.StartSpan("worktree.create", TelemetrySpanKind.Server);
            span?.SetTag("worktree.agent_id", agentId);

            try
            {
                await _createPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

                if (context.Failed)
                {
                    span?.SetStatus(TelemetryStatusCode.Error, context.ErrorMessage);
                    span?.Dispose();
                    RecordWorktreeMetrics("create", isSuccess: false);
                    return WorktreeCreateResult.FailureResult(context.ErrorMessage ?? "未知错误");
                }

                span?.SetStatus(TelemetryStatusCode.Ok);
                span?.SetTag("worktree.duration_ms", context.CreationDurationMs ?? 0);
                span?.Dispose();
                RecordWorktreeMetrics("create", isSuccess: true);

                return context.Result ?? WorktreeCreateResult.FailureResult("未知错误");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "创建 worktree 时出错: {AgentId}", agentId);
                span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
                span?.RecordException(ex);
                span?.Dispose();
                RecordWorktreeMetrics("create", isSuccess: false);
                return WorktreeCreateResult.FailureResult($"创建 worktree 时出错: {ex.Message}");
            }
        }

        throw new InvalidOperationException("Worktree 创建管道未初始化");
    }

    public async Task<WorktreeCleanupResult> RemoveAgentWorktreeAsync(
        string agentId,
        bool force = false,
        CancellationToken cancellationToken = default) {
        var session = await GetSessionAsync(agentId).ConfigureAwait(false);
        if (session == null) {
            return WorktreeCleanupResult.FailureResult($"未找到 Agent {agentId} 的 worktree 会话");
        }

        var span = _telemetryService?.StartSpan("worktree.remove", TelemetrySpanKind.Server);
        span?.SetTag("worktree.agent_id", agentId);
        span?.SetTag("worktree.force", force);

        try {
            if (!force && await HasUncommittedChangesAsync(session.WorktreePath, cancellationToken)) {
                return WorktreeCleanupResult.BlockedResult("worktree 中有未提交的更改");
            }

            if (!force && await HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, cancellationToken)) {
                return WorktreeCleanupResult.BlockedResult("worktree 中有未推送的提交");
            }

            var removeResult = await ExecuteGitCommandAsync(
                session.GitRootPath,
                $"worktree remove --force \"{session.WorktreePath}\"",
                cancellationToken).ConfigureAwait(false);

            if (!removeResult.Success) {
                return WorktreeCleanupResult.FailureResult($"移除 worktree 失败: {removeResult.Error}");
            }

            await ExecuteGitCommandAsync(
                session.GitRootPath,
                $"branch -D {session.BranchName}",
                cancellationToken).ConfigureAwait(false);

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            await RemoveSessionAsync(agentId).ConfigureAwait(false);

            _logger?.LogInformation(
                "成功移除 worktree: {WorktreePath}, Agent: {AgentId}, Forced: {Forced}",
                session.WorktreePath, agentId, force);

            span?.SetStatus(TelemetryStatusCode.Ok);
            span?.Dispose();
            RecordWorktreeMetrics("remove", isSuccess: true);

            return WorktreeCleanupResult.SuccessResult(force);
        } catch (Exception ex) {
            _logger?.LogError(ex, "移除 worktree 时出错: {AgentId}", agentId);

            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            span?.Dispose();
            RecordWorktreeMetrics("remove", isSuccess: false);

            return WorktreeCleanupResult.FailureResult($"移除 worktree 时出错: {ex.Message}");
        }
    }

    public async Task<AgentWorktreeSession?> GetSessionAsync(string agentId, CancellationToken cancellationToken = default) {
        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            return _sessions.TryGetValue(agentId, out var session) ? session : null;
        } finally {
            _sessionLock.Release();
        }
    }

    public async Task<bool> HasActiveWorktreeAsync(string agentId, CancellationToken cancellationToken = default) {
        var session = await GetSessionAsync(agentId).ConfigureAwait(false);
        if (session == null) {
            return false;
        }
        return _fileOperationService.DirectoryExists(session.WorktreePath);
    }

    public async Task<IReadOnlyList<AgentWorktreeSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default) {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return _sessions.Values.ToList();
        } finally {
            _sessionLock.Release();
        }
    }

    public async Task<int> CleanupStaleWorktreesAsync(
        WorktreeOptions? options = null,
        CancellationToken cancellationToken = default) {
        var opts = options ?? _defaultOptions;
        var cleanedCount = 0;

        try {
            var gitRoot = await FindGitRootAsync(_fileOperationService.GetCurrentDirectory()).ConfigureAwait(false);
            if (string.IsNullOrEmpty(gitRoot)) {
                return 0;
            }

            var worktreesDir = _fileOperationService.CombinePath(gitRoot, WorkflowConstants.Paths.ProjectConfigFolderName, WorkflowConstants.Paths.WorktreeFolderName);
            if (!_fileOperationService.DirectoryExists(worktreesDir)) {
                return 0;
            }

            var entries = _fileOperationService.GetDirectories(worktreesDir, "*", SearchOption.TopDirectoryOnly);
            var cutoffTime = _clock.GetUtcNow().Subtract(opts.StaleTimeout);

            foreach (var entry in entries) {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(entry);

                if (!IsEphemeralWorktree(dirName, opts.EphemeralPatterns)) {
                    continue;
                }

                var lastWriteTime = _fileOperationService.GetDirectoryLastWriteTimeUtc(entry);
                if (lastWriteTime > cutoffTime) {
                    continue;
                }

                if (opts.CheckUncommittedChanges && await HasUncommittedChangesAsync(entry, cancellationToken)) {
                    _logger?.LogDebug("跳过清理：worktree 有未提交更改: {Worktree}", entry);
                    continue;
                }

                if (opts.CheckUnpushedCommits) {
                    var hasUnpushed = await HasUnpushedCommitsAsync(entry, null, cancellationToken).ConfigureAwait(false);
                    if (hasUnpushed) {
                        _logger?.LogDebug("跳过清理：worktree 有未推送提交: {Worktree}", entry);
                        continue;
                    }
                }

                var removeResult = await ExecuteGitCommandAsync(
                    gitRoot,
                    $"worktree remove --force \"{entry}\"",
                    cancellationToken).ConfigureAwait(false);

                if (removeResult.Success) {
                    cleanedCount++;
                    _logger?.LogInformation("清理过期 worktree: {Worktree}", entry);
                } else {
                    _logger?.LogWarning("清理 worktree 失败: {Worktree}, 错误: {Error}", entry, removeResult.Error);
                }
            }

            await ExecuteGitCommandAsync(gitRoot, "worktree prune", cancellationToken).ConfigureAwait(false);

            if (cleanedCount > 0) {
                _logger?.LogInformation("共清理 {Count} 个过期 worktree", cleanedCount);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "清理过期 worktree 时出错");
        }

        return cleanedCount;
    }

    public async Task<bool> HasUncommittedChangesAsync(string worktreePath, CancellationToken cancellationToken = default) {
        var result = await ExecuteGitCommandAsync(
            worktreePath,
            "status --porcelain",
            cancellationToken).ConfigureAwait(false);

        if (!result.Success) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<bool> HasUnpushedCommitsAsync(
        string worktreePath,
        string? baseCommitSha = null,
        CancellationToken cancellationToken = default) {
        var command = string.IsNullOrEmpty(baseCommitSha)
            ? $"{GitSubCommand.RevList.ToValue()} --count HEAD --not --remotes"
            : $"{GitSubCommand.RevList.ToValue()} --count {baseCommitSha}..HEAD";

        var result = await ExecuteGitCommandAsync(worktreePath, command, cancellationToken).ConfigureAwait(false);

        if (!result.Success || !int.TryParse(result.Output.Trim(), out var count)) {
            return false;
        }

        return count > 0;
    }

    public async Task<string?> FindGitRootAsync(string startPath, CancellationToken cancellationToken = default) {
        var currentPath = startPath;

        while (!string.IsNullOrEmpty(currentPath)) {
            var gitDir = _fileOperationService.CombinePath(currentPath, ".git");
            if (_fileOperationService.DirectoryExists(gitDir)) {
                return currentPath;
            }

            if (_fileOperationService.FileExists(gitDir)) {
                var canonicalRoot = ResolveCanonicalGitRootFromGitdirFile(gitDir, currentPath);
                if (canonicalRoot is not null) {
                    return canonicalRoot;
                }
                return currentPath;
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath == currentPath || string.IsNullOrEmpty(parentPath)) {
                break;
            }
            currentPath = parentPath;
        }

        return null;
    }

    private string? ResolveCanonicalGitRootFromGitdirFile(string gitdirFilePath, string worktreePath) {
        try {
            var readResult = _fileOperationService.ReadFileAsync(gitdirFilePath).GetAwaiter().GetResult();
            if (!readResult.Success) return null;

            var content = readResult.Content.Trim();
            if (!content.StartsWith("gitdir: ", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            var gitdirRelative = content["gitdir: ".Length..].Trim();
            var gitdirAbs = _fileOperationService.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = Path.GetFullPath(gitdirAbs);

            var worktreesMarker = Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar + "worktrees" + Path.DirectorySeparatorChar;
            var markerIdx = normalizedGitdir.IndexOf(worktreesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0) {
                return null;
            }

            var canonicalRoot = normalizedGitdir[..markerIdx];
            var canonicalGitDir = _fileOperationService.CombinePath(canonicalRoot, ".git");
            if (_fileOperationService.DirectoryExists(canonicalGitDir)) {
                return canonicalRoot;
            }

            return null;
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "解析 .gitdir 文件失败: {Path}", gitdirFilePath);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListWorktreesAsync(
        string? gitRootPath = null,
        CancellationToken cancellationToken = default) {
        var gitRoot = gitRootPath ?? await FindGitRootAsync(_fileOperationService.GetCurrentDirectory()).ConfigureAwait(false);
        if (string.IsNullOrEmpty(gitRoot)) {
            return Array.Empty<string>();
        }

        var result = await ExecuteGitCommandAsync(gitRoot, "worktree list --porcelain", cancellationToken).ConfigureAwait(false);

        if (!result.Success) {
            return Array.Empty<string>();
        }

        var worktrees = new List<string>();
        var outputSpan = result.Output.AsSpan();

        while (!outputSpan.IsEmpty)
        {
            var newlineIndex = outputSpan.IndexOf('\n');
            var line = newlineIndex >= 0 ? outputSpan[..newlineIndex] : outputSpan;

            if (line.StartsWith("worktree ".AsSpan()))
            {
                var path = line[9..].Trim();
                worktrees.Add(path.ToString());
            }

            if (newlineIndex >= 0)
                outputSpan = outputSpan[(newlineIndex + 1)..];
            else
                break;
        }

        return worktrees;
    }

    private void RecordWorktreeMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("worktree.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Worktree operation count");

    #region Private Methods

    public async Task SaveSessionAsync(AgentWorktreeSession session) {
        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            _sessions[session.AgentId] = session;
        } finally {
            _sessionLock.Release();
        }
    }

    internal async Task RemoveSessionAsync(string agentId) {
        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            _sessions.Remove(agentId);
        } finally {
            _sessionLock.Release();
        }
    }

    public async ValueTask DisposeAsync() {
        _sessionLock.Dispose();
    }

    public async Task<bool> IsValidWorktreeAsync(string worktreePath, string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, "worktree list --porcelain").ConfigureAwait(false);
        if (!result.Success) {
            return false;
        }

        var normalizedWorktreePath = Path.GetFullPath(worktreePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outputSpan = result.Output.AsSpan();

        while (!outputSpan.IsEmpty)
        {
            var newlineIndex = outputSpan.IndexOf('\n');
            var line = newlineIndex >= 0 ? outputSpan[..newlineIndex] : outputSpan;

            if (line.StartsWith("worktree ".AsSpan()))
            {
                var listedPath = line[9..].Trim().ToString();
                var normalizedListedPath = Path.GetFullPath(listedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(normalizedWorktreePath, normalizedListedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (newlineIndex >= 0)
                outputSpan = outputSpan[(newlineIndex + 1)..];
            else
                break;
        }

        return false;
    }

    public async Task<string?> GetCurrentBranchAsync(string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, "branch --show-current").ConfigureAwait(false);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<string?> GetHeadCommitShaAsync(string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, $"{GitSubCommand.RevParse.ToValue()} HEAD").ConfigureAwait(false);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<bool> HasLocalBranchAsync(string gitRoot, string branchName, CancellationToken cancellationToken) {
        var result = await ExecuteGitCommandAsync(
            gitRoot, $"{GitSubCommand.RevParse.ToValue()} --verify refs/heads/{branchName}", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<bool> ApplySparseCheckoutAsync(
        string worktreePath,
        IReadOnlyList<string> sparsePaths,
        CancellationToken cancellationToken) {
        var initResult = await ExecuteGitCommandAsync(
            worktreePath,
            "sparse-checkout init --cone",
            cancellationToken).ConfigureAwait(false);

        if (!initResult.Success) {
            return false;
        }

        var paths = string.Join(" ", sparsePaths.Select(p => $"\"{p}\""));
        var setResult = await ExecuteGitCommandAsync(
            worktreePath,
            $"{GitSubCommand.SparseCheckout.ToValue()} set {paths}",
            cancellationToken).ConfigureAwait(false);

        return setResult.Success;
    }

    private async Task CopyConfigFilesAsync(string gitRoot, string worktreePath, WorktreeOptions options) {
        if (options.ConfigFilesToCopy?.Count == 0) {
            return;
        }

        foreach (var relativePath in options.ConfigFilesToCopy!) {
            try {
                var sourcePath = _fileOperationService.CombinePath(gitRoot, relativePath);
                var destPath = _fileOperationService.CombinePath(worktreePath, relativePath);

                if (!_fileOperationService.FileExists(sourcePath)) {
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !_fileOperationService.DirectoryExists(destDir)) {
                    _fileOperationService.CreateDirectory(destDir);
                }

                await _fileOperationService.CopyFileAsync(sourcePath, destPath, overwrite: true).ConfigureAwait(false);
                _logger?.LogDebug("复制配置文件: {Source} -> {Dest}", sourcePath, destPath);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "复制配置文件失败: {File}", relativePath);
            }
        }
    }

    private async Task CopyWorktreeIncludeFilesAsync(string gitRoot, string worktreePath, CancellationToken cancellationToken) {
        var includeFilePath = _fileOperationService.CombinePath(gitRoot, ".worktreeinclude");
        if (!_fileOperationService.FileExists(includeFilePath)) {
            return;
        }

        try {
            var readResult = await _fileOperationService.ReadFileAsync(includeFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success) return;

            var patterns = readResult.Content
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();

            if (patterns.Count == 0) return;

            var lsResult = await ExecuteGitCommandAsync(
                gitRoot,
                "ls-files --others --ignored --exclude-standard --directory",
                cancellationToken).ConfigureAwait(false);

            if (!lsResult.Success || string.IsNullOrWhiteSpace(lsResult.Output)) return;

            var entries = lsResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var copied = new List<string>();

            foreach (var entry in entries) {
                var trimmed = entry.Trim();
                if (trimmed.EndsWith('/')) continue;

                var matches = patterns.Any(p => MatchesWorktreeIncludePattern(trimmed, p.TrimStart('/')));
                if (!matches) continue;

                var srcPath = _fileOperationService.CombinePath(gitRoot, trimmed);
                var destPath = _fileOperationService.CombinePath(worktreePath, trimmed);

                if (!_fileOperationService.FileExists(srcPath)) continue;

                try {
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !_fileOperationService.DirectoryExists(destDir)) {
                        _fileOperationService.CreateDirectory(destDir);
                    }
                    await _fileOperationService.CopyFileAsync(srcPath, destPath, overwrite: true, cancellationToken).ConfigureAwait(false);
                    copied.Add(trimmed);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "复制 .worktreeinclude 文件失败: {File}", trimmed);
                }
            }

            if (copied.Count > 0) {
                _logger?.LogInformation("从 .worktreeinclude 复制了 {Count} 个文件: {Files}", copied.Count, string.Join(", ", copied));
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "处理 .worktreeinclude 时出错");
        }
    }

    private static bool MatchesWorktreeIncludePattern(string filePath, string pattern) {
        if (pattern.Contains('*')) {
            var regexStr = "^" + Regex.Escape(pattern).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$";
            try {
                return Regex.IsMatch(filePath, regexStr, RegexOptions.IgnoreCase);
            } catch {
                return false;
            }
        }
        return filePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
               filePath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConfigureWorktreeHooksPathAsync(string gitRoot, string worktreePath, CancellationToken cancellationToken) {
        try {
            var hooksResult = await ExecuteGitCommandAsync(
                gitRoot, "config --get core.hooksPath", cancellationToken).ConfigureAwait(false);

            if (hooksResult.Success && !string.IsNullOrWhiteSpace(hooksResult.Output)) {
                var hooksPath = hooksResult.Output.Trim();
                var absHooksPath = Path.IsPathRooted(hooksPath)
                    ? hooksPath
                    : _fileOperationService.CombinePath(gitRoot, hooksPath);

                if (_fileOperationService.DirectoryExists(absHooksPath)) {
                    await ExecuteGitCommandAsync(
                        worktreePath,
                        $"config core.hooksPath \"{absHooksPath}\"",
                        cancellationToken).ConfigureAwait(false);
                    _logger?.LogDebug("配置 worktree hooks 路径: {Path}", absHooksPath);
                }
            }
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "配置 worktree hooks 路径失败");
        }
    }

    private async Task CreateSymlinksAsync(string gitRoot, string worktreePath, IReadOnlyList<string> directories) {
        foreach (var dir in directories) {
            try {
                var sourcePath = _fileOperationService.CombinePath(gitRoot, dir);
                var destPath = _fileOperationService.CombinePath(worktreePath, dir);

                if (!_fileOperationService.DirectoryExists(sourcePath)) {
                    continue;
                }

                if (_fileOperationService.DirectoryExists(destPath) || _fileOperationService.FileExists(destPath)) {
                    continue;
                }

                _fileOperationService.CreateSymbolicLink(destPath, sourcePath);
                _logger?.LogDebug("创建符号链接: {Source} -> {Dest}", sourcePath, destPath);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "创建符号链接失败: {Directory}", dir);
            }
        }
    }

    private static bool IsEphemeralWorktree(string dirName, IReadOnlyList<string> patterns) {
        return patterns.Any(pattern => {
            var regex = new Regex(
                "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                RegexOptions.IgnoreCase);
            return regex.IsMatch(dirName);
        });
    }

    public async Task<GitCommandResult> ExecuteGitCommandAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default) {
        try {
            var options = new ProcessOptions {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GIT_ASKPASS"] = ""
                }
            };

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            return new GitCommandResult {
                Success = result.Success,
                Output = result.StandardOutput,
                Error = result.StandardError,
                ExitCode = result.ExitCode
            };
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return new GitCommandResult {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    #endregion
}
