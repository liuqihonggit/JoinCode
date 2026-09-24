
namespace Core.Agents;

/// <summary>
/// Agent Worktree 服务实现 — 管理子代理的 git worktree 创建、清理、会话持久化与稀疏检出
/// </summary>
[Register(typeof(IAgentWorktreeService), ServiceLifetime.Singleton)]
[Register(typeof(IWorktreePipelineOperations), ServiceLifetime.Singleton)]
public sealed partial class AgentWorktreeService : IAgentWorktreeService, IWorktreePipelineOperations, IAsyncDisposable {
    private readonly ILogger<AgentWorktreeService>? _logger;
    private readonly IClockService _clock;
    private readonly IGitCommandRunner _gitRunner;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly WorktreeOptions _defaultOptions;
    private readonly ITelemetryService? _telemetryService;
    private ImmutableDictionary<string, AgentWorktreeSession> _sessions = ImmutableDictionary<string, AgentWorktreeSession>.Empty;
    private readonly WorktreeSessionActor _sessionActor;
    private readonly MiddlewarePipeline<WorktreeCreateContext>? _createPipeline;
    private int _disposed;

    /// <summary>
    /// 构造 AgentWorktreeService 实例，注入文件操作服务、git 命令运行器、文件系统及可选的创建中间件、日志器等
    /// </summary>
    public AgentWorktreeService(
        IFileOperationService fileOperationService,
        IGitCommandRunner gitRunner,
        IFileSystem fs,
        IEnumerable<IWorktreeCreateMiddleware>? createMiddlewares = null,
        ILoggerFactory? loggerFactory = null,
        ILogger<AgentWorktreeService>? logger = null,
        WorktreeOptions? defaultOptions = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _defaultOptions = defaultOptions ?? new WorktreeOptions();
        _telemetryService = telemetryService;

        if (createMiddlewares != null && loggerFactory != null) {
            _createPipeline = new PipelineBuilder<WorktreeCreateContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(createMiddlewares.OrderBy(m => m.Order))
                .Build();
        } else if (createMiddlewares != null) {
            _createPipeline = new MiddlewarePipeline<WorktreeCreateContext>(createMiddlewares.OrderBy(m => m.Order));
        }

        _sessionActor = new WorktreeSessionActor(this, _logger);
    }

    /// <summary>
    /// 为指定代理创建 git worktree，通过创建管道执行；失败时返回失败结果
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    /// <param name="gitRootPath">git 根目录（可选）</param>
    /// <param name="options">worktree 选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>worktree 创建结果</returns>
    public async Task<WorktreeCreateResult> CreateAgentWorktreeAsync(
        string agentId,
        string? gitRootPath = null,
        WorktreeOptions? options = null,
        CancellationToken cancellationToken = default) {
        if (_createPipeline != null) {
            var context = new WorktreeCreateContext {
                AgentId = agentId,
                GitRootPath = gitRootPath,
                Options = options ?? _defaultOptions,
                CancellationToken = cancellationToken
            };

            await using var span = _telemetryService?.StartSpan("worktree.create", TelemetrySpanKind.Server);
            span?.SetTag("worktree.agent_id", agentId);

            try {
                await _createPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

                if (context.Failed) {
                    span?.SetStatus(TelemetryStatusCode.Error, context.ErrorMessage);
                    RecordWorktreeMetrics("create", isSuccess: false);
                    return WorktreeCreateResult.FailureResult(context.ErrorMessage ?? "未知错误");
                }

                span?.SetStatus(TelemetryStatusCode.Ok);
                span?.SetTag("worktree.duration_ms", context.CreationDurationMs ?? 0);
                RecordWorktreeMetrics("create", isSuccess: true);

                return context.Result ?? WorktreeCreateResult.FailureResult("未知错误");
            } catch (Exception ex) {
                _logger?.LogError(ex, "创建 worktree 时出错: {AgentId}", agentId);
                span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
                span?.RecordException(ex);
                RecordWorktreeMetrics("create", isSuccess: false);
                return WorktreeCreateResult.FailureResult($"创建 worktree 时出错: {ex.Message}");
            }
        }

        throw new InvalidOperationException("[AGT010] Worktree 创建管道未初始化");
    }

    /// <summary>
    /// 移除指定代理的 worktree 及关联分支；非强制模式下有未提交变更或未推送提交时阻止移除
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    /// <param name="force">是否强制移除（忽略未提交/未推送检查）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理结果</returns>
    public async Task<WorktreeCleanupResult> RemoveAgentWorktreeAsync(
        string agentId,
        bool force = false,
        CancellationToken cancellationToken = default) {
        var session = await GetSessionAsync(agentId).ConfigureAwait(false);
        if (session == null) {
            return WorktreeCleanupResult.FailureResult($"未找到 Agent {agentId} 的 worktree 会话");
        }

        await using var span = _telemetryService?.StartSpan("worktree.remove", TelemetrySpanKind.Server);
        span?.SetTag("worktree.agent_id", agentId);
        span?.SetTag("worktree.force", force);

        try {
            if (!force && await HasUncommittedChangesAsync(session.WorktreePath, cancellationToken).ConfigureAwait(false)) {
                return WorktreeCleanupResult.BlockedResult("worktree 中有未提交的更改");
            }

            if (!force && await HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, cancellationToken).ConfigureAwait(false)) {
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
            RecordWorktreeMetrics("remove", isSuccess: true);

            return WorktreeCleanupResult.SuccessResult(force);
        } catch (Exception ex) {
            _logger?.LogError(ex, "移除 worktree 时出错: {AgentId}", agentId);

            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            RecordWorktreeMetrics("remove", isSuccess: false);

            return WorktreeCleanupResult.FailureResult($"移除 worktree 时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取指定代理的 worktree 会话
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>worktree 会话；不存在时返回 null</returns>
    public async Task<AgentWorktreeSession?> GetSessionAsync(string agentId, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<AgentWorktreeSession?>();
        await _sessionActor.SendAsync(new GetSessionCmd(agentId, reply), cancellationToken).ConfigureAwait(false);
        return await _sessionActor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取会话的内部实现 — 由 Actor Consumer 串行调用，无需锁
    /// </summary>
    private AgentWorktreeSession? GetSessionInternal(string agentId)
        => _sessions.TryGetValue(agentId, out var session) ? session : null;

    /// <summary>
    /// 判断指定代理是否有活跃的 worktree（会话存在且目录存在）
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有活跃 worktree</returns>
    public async Task<bool> HasActiveWorktreeAsync(string agentId, CancellationToken cancellationToken = default) {
        var session = await GetSessionAsync(agentId).ConfigureAwait(false);
        if (session == null) {
            return false;
        }
        return _fileOperationService.DirectoryExists(session.WorktreePath);
    }

    /// <summary>
    /// 获取所有代理的 worktree 会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有 worktree 会话集合</returns>
    public async Task<IEnumerable<AgentWorktreeSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<IEnumerable<AgentWorktreeSession>>();
        await _sessionActor.SendAsync(new GetAllSessionsCmd(reply), cancellationToken).ConfigureAwait(false);
        return await _sessionActor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有会话的内部实现 — 由 Actor Consumer 串行调用，无需锁。返回不可变引用,无需拷贝。
    /// </summary>
    private IEnumerable<AgentWorktreeSession> GetAllSessionsInternal()
        => _sessions.Values;

    /// <summary>
    /// 清理过期的临时 worktree，按最后写入时间与未提交/未推送检查筛选
    /// </summary>
    /// <param name="options">worktree 选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的 worktree 数量</returns>
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

            var worktreesDir = _fileOperationService.CombinePath(gitRoot, AppDataConstants.Paths.ProjectConfigFolderName, AppDataConstants.Paths.WorktreeFolderName);
            if (!_fileOperationService.DirectoryExists(worktreesDir)) {
                return 0;
            }

            var entries = _fileOperationService.GetDirectories(worktreesDir, "*", SearchOption.TopDirectoryOnly);
            var cutoffTime = _clock.GetUtcNow().Subtract(opts.StaleTimeout);

            foreach (var entry in entries) {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(entry);

                if (!WorktreePatternMatcher.IsEphemeralWorktree(dirName, opts.EphemeralPatterns)) {
                    continue;
                }

                var lastWriteTime = _fileOperationService.GetDirectoryLastWriteTimeUtc(entry);
                if (lastWriteTime > cutoffTime) {
                    continue;
                }

                if (opts.CheckUncommittedChanges && await HasUncommittedChangesAsync(entry, cancellationToken).ConfigureAwait(false)) {
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

    /// <summary>
    /// 检查指定 worktree 路径是否有未提交的变更
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有未提交变更</returns>
    public async Task<bool> HasUncommittedChangesAsync(string worktreePath, CancellationToken cancellationToken = default) {
        return await _gitRunner.HasUncommittedChangesAsync(worktreePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查指定 worktree 是否有未推送的提交，可指定基准提交 SHA 进行比较
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="baseCommitSha">基准提交 SHA（可选，未指定时比较所有远程）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有未推送提交</returns>
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

    /// <summary>
    /// 从指定路径向上查找 git 仓库根目录
    /// </summary>
    /// <param name="startPath">起始搜索路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>git 根目录路径；未找到时返回 null</returns>
    public async Task<string?> FindGitRootAsync(string startPath, CancellationToken cancellationToken = default) {
        return await GitWorkspaceResolver.FindGitRootAsync(startPath, _fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保留指定代理的 worktree（仅移除会话记录，不删除 worktree 目录与分支）
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task KeepWorktreeAsync(string agentId, CancellationToken cancellationToken = default) {
        var session = await GetSessionAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (session is null) return;

        _logger?.LogInformation(
            "保留 worktree: {WorktreePath}, Agent: {AgentId}, 可通过 cd {WorktreePath} 继续工作",
            session.WorktreePath, agentId, session.WorktreePath);

        await RemoveSessionAsync(agentId).ConfigureAwait(false);
    }

    /// <summary>
    /// 列出 git 仓库下所有 worktree 路径
    /// </summary>
    /// <param name="gitRootPath">git 根目录（可选，默认自动查找）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>worktree 路径列表</returns>
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

        while (!outputSpan.IsEmpty) {
            var newlineIndex = outputSpan.IndexOf('\n');
            var line = newlineIndex >= 0 ? outputSpan[..newlineIndex] : outputSpan;

            if (line.StartsWith("worktree ".AsSpan())) {
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
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "worktree.operation.count", operation, isSuccess, "Worktree operation count");

    #region Private Methods

    /// <summary>
    /// 保存 worktree 会话到内存并持久化到本地设置文件
    /// </summary>
    /// <param name="session">要保存的 worktree 会话</param>
    public async Task SaveSessionAsync(AgentWorktreeSession session) {
        var reply = new TaskCompletionSource();
        await _sessionActor.SendAsync(new SaveSessionCmd(session, reply), default).ConfigureAwait(false);
        await _sessionActor.AskReplyAsync(reply, default).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存会话的内部实现 — 由 Actor Consumer 串行调用，无需锁
    /// </summary>
    private async Task SaveSessionInternalAsync(AgentWorktreeSession session) {
        _sessions = _sessions.SetItem(session.AgentId, session);
        await PersistActiveWorktreeSessionAsync(session).ConfigureAwait(false);
    }

    /// <summary>
    /// 移除指定代理的会话记录并清除持久化的活跃会话
    /// </summary>
    /// <param name="agentId">代理唯一标识</param>
    internal async Task RemoveSessionAsync(string agentId) {
        var reply = new TaskCompletionSource();
        await _sessionActor.SendAsync(new RemoveSessionCmd(agentId, reply), default).ConfigureAwait(false);
        await _sessionActor.AskReplyAsync(reply, default).ConfigureAwait(false);
    }

    /// <summary>
    /// 移除会话的内部实现 — 由 Actor Consumer 串行调用，无需锁
    /// </summary>
    private async Task RemoveSessionInternalAsync(string agentId) {
        _sessions = _sessions.Remove(agentId);
        await ClearActiveWorktreeSessionAsync().ConfigureAwait(false);
    }

    private async Task PersistActiveWorktreeSessionAsync(AgentWorktreeSession session) {
        try {
            var gitRoot = session.GitRootPath;
            var localSettingsPath = _fileOperationService.CombinePath(gitRoot, AppDataConstants.Paths.LocalSettingsRelativePath);

            var readResult = await _fileOperationService.ReadFileAsync(localSettingsPath).ConfigureAwait(false);
            var jsonStr = readResult.Success ? readResult.Content : "{}";

            var root = jsonStr.Length > 0 ? JsonNode.Parse(jsonStr) as JsonObject : new JsonObject();

            root ??= new JsonObject();
            root["activeWorktreeSession"] = new JsonObject {
                ["originalCwd"] = session.OriginalCwd,
                ["worktreePath"] = session.WorktreePath,
                ["worktreeName"] = session.AgentId,
                ["worktreeBranch"] = session.BranchName,
                ["originalBranch"] = session.OriginalBranch,
                ["originalHeadCommit"] = session.BaseCommitSha,
                ["sessionId"] = session.AgentId,
                ["hookBased"] = session.HookBased,
                ["creationDurationMs"] = session.CreationDurationMs,
                ["usedSparsePaths"] = session.SparsePaths?.Count > 0
            };

            var updatedJson = await WorktreeJsonFormatting.FormatJsonNode(root).ConfigureAwait(false);

            var dir = Path.GetDirectoryName(localSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !_fileOperationService.DirectoryExists(dir)) {
                _fileOperationService.CreateDirectory(dir);
            }

            await _fileOperationService.WriteFileAsync(localSettingsPath, updatedJson).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "持久化 worktree 会话失败");
        }
    }

    private async Task ClearActiveWorktreeSessionAsync() {
        try {
            if (_sessions.Count > 0) return;

            var cwd = _fileOperationService.GetCurrentDirectory();
            var gitRoot = await FindGitRootAsync(cwd).ConfigureAwait(false);
            if (string.IsNullOrEmpty(gitRoot)) return;

            var localSettingsPath = _fileOperationService.CombinePath(gitRoot, AppDataConstants.Paths.LocalSettingsRelativePath);
            if (!_fileOperationService.FileExists(localSettingsPath)) return;

            var readResult = await _fileOperationService.ReadFileAsync(localSettingsPath).ConfigureAwait(false);
            if (!readResult.Success) return;

            var root = JsonNode.Parse(readResult.Content) as JsonObject;
            if (root is null) return;

            root.Remove("activeWorktreeSession");

            var updatedJson = await WorktreeJsonFormatting.FormatJsonNode(root).ConfigureAwait(false);
            await _fileOperationService.WriteFileAsync(localSettingsPath, updatedJson).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "清除 worktree 会话持久化失败");
        }
    }

    /// <summary>
    /// 异步释放资源 — 仅释放 Actor,不自动删除 worktree。
    /// worktree 生命周期由显式 worktree_remove 指令控制,不由进程退出自动清理。
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _sessionActor.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 验证指定路径是否为有效 worktree
    /// </summary>
    /// <param name="worktreePath">待验证的 worktree 路径</param>
    /// <param name="gitRoot">git 根目录</param>
    /// <returns>是否为有效 worktree</returns>
    public async Task<bool> IsValidWorktreeAsync(string worktreePath, string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, "worktree list --porcelain").ConfigureAwait(false);
        if (!result.Success) {
            return false;
        }

        var normalizedWorktreePath = Path.GetFullPath(worktreePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outputSpan = result.Output.AsSpan();

        while (!outputSpan.IsEmpty) {
            var newlineIndex = outputSpan.IndexOf('\n');
            var line = newlineIndex >= 0 ? outputSpan[..newlineIndex] : outputSpan;

            if (line.StartsWith("worktree ".AsSpan())) {
                var listedPath = line[9..].Trim().ToString();
                var normalizedListedPath = Path.GetFullPath(listedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(normalizedWorktreePath, normalizedListedPath, StringComparison.OrdinalIgnoreCase)) {
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

    /// <summary>
    /// 获取 git 仓库的当前分支名称
    /// </summary>
    /// <param name="gitRoot">git 根目录</param>
    /// <returns>当前分支名称；失败时返回 null</returns>
    public async Task<string?> GetCurrentBranchAsync(string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, "branch --show-current").ConfigureAwait(false);
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>
    /// 获取 git 仓库 HEAD 的提交 SHA
    /// </summary>
    /// <param name="gitRoot">git 根目录</param>
    /// <returns>HEAD 提交 SHA；失败时返回 null</returns>
    public async Task<string?> GetHeadCommitShaAsync(string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, $"{GitSubCommand.RevParse.ToValue()} HEAD").ConfigureAwait(false);
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>
    /// 获取 git 仓库的默认分支，优先从 origin/HEAD 解析，回退到 main 再到 master
    /// </summary>
    /// <param name="gitRoot">git 根目录</param>
    /// <returns>默认分支名称；未找到时返回 null</returns>
    public async Task<string?> GetDefaultBranchAsync(string gitRoot) {
        var result = await ExecuteGitCommandAsync(gitRoot, "symbolic-ref refs/remotes/origin/HEAD --short").ConfigureAwait(false);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Output)) {
            var branch = result.Output.Trim();
            return branch.StartsWith("origin/") ? branch[7..] : branch;
        }

        var mainResult = await ExecuteGitCommandAsync(gitRoot, $"{GitSubCommand.RevParse.ToValue()} --verify refs/heads/main").ConfigureAwait(false);
        if (mainResult.Success) return "main";

        var masterResult = await ExecuteGitCommandAsync(gitRoot, $"{GitSubCommand.RevParse.ToValue()} --verify refs/heads/master").ConfigureAwait(false);
        return masterResult.Success ? "master" : null;
    }

    /// <summary>
    /// 解析 git 引用为提交 SHA
    /// </summary>
    /// <param name="gitRoot">git 根目录</param>
    /// <param name="refName">引用名称</param>
    /// <returns>提交 SHA；失败时返回 null</returns>
    public async Task<string?> ResolveRefAsync(string gitRoot, string refName) {
        var result = await ExecuteGitCommandAsync(gitRoot, $"{GitSubCommand.RevParse.ToValue()} {refName}").ConfigureAwait(false);
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>
    /// 检查 git 仓库是否存在指定的本地分支
    /// </summary>
    /// <param name="gitRoot">git 根目录</param>
    /// <param name="branchName">分支名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在本地分支</returns>
    public async Task<bool> HasLocalBranchAsync(string gitRoot, string branchName, CancellationToken cancellationToken) {
        var result = await ExecuteGitCommandAsync(
            gitRoot, $"{GitSubCommand.RevParse.ToValue()} --verify refs/heads/{branchName}", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>
    /// 对指定 worktree 应用稀疏检出，仅检出指定路径下的文件
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="sparsePaths">稀疏检出路径列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否应用成功</returns>
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
            $"sparse-checkout set --cone -- {paths}",
            cancellationToken).ConfigureAwait(false);

        return setResult.Success;
    }

    /// <summary>
    /// 在指定工作目录执行 git 命令
    /// </summary>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="arguments">git 命令参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>git 命令执行结果</returns>
    public Task<GitCommandResult> ExecuteGitCommandAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default)
        => _gitRunner.ExecuteAsync(arguments, workingDirectory, cancellationToken);

    #endregion

    /// <summary>
    /// Worktree 会话管理 Actor — 串行化所有 _sessions 字典访问，消除显式 AsyncLock — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// <para>_sessions 是 Dictionary 非线程安全，故读操作（GetSession/GetAllSessions）也经 Actor。</para>
    /// </summary>
    private sealed class WorktreeSessionActor : ActorBase<WorktreeSessionCommand, Unit> {
        private readonly AgentWorktreeService _owner;
        private readonly ILogger<AgentWorktreeService>? _logger;

        /// <summary>构造 WorktreeSessionActor。</summary>
        /// <param name="owner">所属工作树服务。</param>
        /// <param name="logger">日志器。</param>
        public WorktreeSessionActor(AgentWorktreeService owner, ILogger<AgentWorktreeService>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 AgentWorktreeService 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（无返回值）— 暴露 protected AskAwait 非泛型重载</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(WorktreeSessionCommand cmd, CancellationToken ct) {
            try {
                switch (cmd) {
                    case GetSessionCmd(var agentId, var reply):
                    reply.SetResult(_owner.GetSessionInternal(agentId));
                    break;
                    case GetAllSessionsCmd(var reply):
                    reply.SetResult(_owner.GetAllSessionsInternal());
                    break;
                    case SaveSessionCmd(var session, var reply):
                    await _owner.SaveSessionInternalAsync(session).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case RemoveSessionCmd(var agentId, var reply):
                    await _owner.RemoveSessionInternalAsync(agentId).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                }
            } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                _logger?.LogWarning(ex, "WorktreeSessionActor 命令处理异常");
                switch (cmd) {
                    case GetSessionCmd(_, var reply):
                    reply.TrySetException(ex);
                    break;
                    case GetAllSessionsCmd(var reply):
                    reply.TrySetException(ex);
                    break;
                    case SaveSessionCmd(_, var reply):
                    reply.TrySetException(ex);
                    break;
                    case RemoveSessionCmd(_, var reply):
                    reply.TrySetException(ex);
                    break;
                }
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "WorktreeSessionActor Consumer 异常");
    }
}