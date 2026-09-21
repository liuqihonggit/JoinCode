namespace Core.Utils;

/// <summary>
/// Agent权限管理器实现
/// <para>使用 Actor 邮箱管道串行化所有操作，消除显式锁 — TASK001</para>
/// </summary>
[Register(typeof(IAgentPermissionManager), ServiceLifetime.Singleton)]
public sealed partial class AgentPermissionManager : IAgentPermissionManager, IAsyncDisposable {
    private readonly Dictionary<string, AgentPermissionRule> _rules = new(StringComparer.Ordinal);
    private readonly PermissionActor _actor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IPersistencePipeline? _persistencePipeline;
    private readonly IFileSystem? _fs;
    private readonly ILogger<AgentPermissionManager>? _logger;
    private int _rulesLoaded;
    private static readonly string RulesSubDir = Path.Combine(AppDataConstants.AppDataFolder, "permission");
    private const string RulesFileName = "rules.json";
    private int _disposed;

    /// <summary>
    /// 构造代理权限管理器
    /// </summary>
    public AgentPermissionManager(
        ITelemetryService? telemetryService = null,
        IPersistencePipeline? persistencePipeline = null,
        IFileSystem? fs = null,
        ILogger<AgentPermissionManager>? logger = null) {
        _telemetryService = telemetryService;
        _persistencePipeline = persistencePipeline;
        _logger = logger;
        _fs = fs;
        _actor = new PermissionActor(this, logger);
    }

    /// <inheritdoc />
    public async Task AddRuleAsync(AgentPermissionRule rule, CancellationToken ct = default) {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _actor.SendAsync(new AddRuleCmd(rule, reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRuleAsync(string agentPattern, CancellationToken ct = default) {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _actor.SendAsync(new RemoveRuleCmd(agentPattern, reply), ct).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PermissionCheckResult> CheckToolPermissionAsync(string agentName, string toolName, Dictionary<string, JsonElement>? parameters = null, CancellationToken ct = default) {
        var rule = await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);

        if (rule == null) {
            return new PermissionCheckResult {
                IsAllowed = true,
                Mode = PermissionMode.Auto,
                Reason = "未找到匹配的规则，默认允许"
            };
        }

        // 检查拒绝列表
        if (rule.DeniedTools?.Contains(toolName) == true) {
            return new PermissionCheckResult {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"工具 '{toolName}' 在拒绝列表中",
                MatchedRule = rule
            };
        }

        // 检查允许列表
        if (rule.AllowedTools?.Count > 0 && !rule.AllowedTools.Contains(toolName)) {
            return new PermissionCheckResult {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"工具 '{toolName}' 不在允许列表中",
                MatchedRule = rule
            };
        }

        // 根据权限级别检查
        var canExecute = rule.Level >= PermissionLevel.Execute ||
                        (rule.Level >= PermissionLevel.Write && !IsDestructiveTool(toolName));

        if (!canExecute) {
            return new PermissionCheckResult {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"权限级别不足，当前级别: {rule.Level}",
                MatchedRule = rule
            };
        }

        return new PermissionCheckResult {
            IsAllowed = rule.Mode != PermissionMode.Ask,
            Mode = rule.Mode,
            Reason = $"匹配规则: {rule.Description ?? rule.AgentPattern}",
            MatchedRule = rule
        };
    }

    /// <inheritdoc />
    public async Task<PermissionCheckResult> CheckPathPermissionAsync(string agentName, string path, CancellationToken ct = default) {
        var rule = await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);

        if (rule == null) {
            return new PermissionCheckResult {
                IsAllowed = true,
                Mode = PermissionMode.Auto,
                Reason = "未找到匹配的规则，默认允许"
            };
        }

        var normalizedPath = Path.GetFullPath(path);

        if (rule.DeniedPaths?.Any(p => normalizedPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true) {
            return new PermissionCheckResult {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"路径在拒绝列表中",
                MatchedRule = rule
            };
        }

        if (rule.AllowedPaths?.Count > 0 &&
            !rule.AllowedPaths.Any(p => normalizedPath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) {
            return new PermissionCheckResult {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"路径不在允许列表中",
                MatchedRule = rule
            };
        }

        return new PermissionCheckResult {
            IsAllowed = rule.Mode != PermissionMode.Ask,
            Mode = rule.Mode,
            Reason = $"匹配规则: {rule.Description ?? rule.AgentPattern}",
            MatchedRule = rule
        };
    }

    /// <inheritdoc />
    public async Task<AgentPermissionRule?> GetRuleForAgentAsync(string agentName, CancellationToken ct = default) {
        return await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentPermissionRule>> ListRulesAsync(CancellationToken ct = default) {
        var reply = new TaskCompletionSource<IReadOnlyList<AgentPermissionRule>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _actor.SendAsync(new ListRulesCmd(reply), ct).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearRulesAsync(CancellationToken ct = default) {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _actor.SendAsync(new ClearRulesCmd(reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 持久化权限规则到 .jcc/permission/rules.json(通过统一持久化管道)。
    /// </summary>
    private async Task SaveRulesAsync(CancellationToken ct) {
        if (_persistencePipeline is null) return;

        var snapshot = _rules.Values.ToList();
        var json = RelaxedJsonSerializer.Serialize(snapshot, PermissionJsonContext.Default);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PersistRequest {
            Category = "permission",
            Directory = RulesSubDir,
            FileName = RulesFileName,
            Content = json,
            Completion = tcs,
        };
        await _persistencePipeline.EnqueueAsync(request, ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 从 .jcc/permission/rules.json 加载权限规则(若存在且尚未加载)。Interlocked 保证只执行一次。
    /// </summary>
    private async Task EnsureRulesLoadedAsync(CancellationToken ct) {
        if (_fs is null || Interlocked.CompareExchange(ref _rulesLoaded, 1, 0) != 0) return;

        try {
            var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs!);
            if (root is null) return;
            var path = Path.Combine(Path.Combine(root, RulesSubDir), RulesFileName);
            if (!_fs.FileExists(path)) return;
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var list = RelaxedJsonSerializer.Deserialize<List<AgentPermissionRule>>(json, PermissionJsonContext.Default);
            if (list is null) return;
            foreach (var r in list) {
                _rules[r.AgentPattern] = r;
            }
        } catch (Exception ex) {
            _logger?.LogError("加载规则失败: {Message}", ex.Message);
        }
    }

    #region Private Methods

    private async Task<AgentPermissionRule?> GetMatchingRuleAsync(string agentName, CancellationToken ct) {
        var reply = new TaskCompletionSource<AgentPermissionRule?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _actor.SendAsync(new GetMatchingRuleCmd(agentName, reply), ct).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加规则内部实现 — 由 PermissionActor Consumer 串行调用，无需锁
    /// </summary>
    private async Task AddRuleInternalAsync(AgentPermissionRule rule, CancellationToken ct) {
        await EnsureRulesLoadedAsync(ct).ConfigureAwait(false);
        _rules.Remove(rule.AgentPattern);
        _rules[rule.AgentPattern] = rule;
        RecordPermissionManagerMetrics("add_rule");
        await SaveRulesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 移除规则内部实现 — 由 PermissionActor Consumer 串行调用，无需锁
    /// </summary>
    private async Task<bool> RemoveRuleInternalAsync(string agentPattern, CancellationToken ct) {
        await EnsureRulesLoadedAsync(ct).ConfigureAwait(false);
        var removed = _rules.Remove(agentPattern);
        if (removed) {
            RecordPermissionManagerMetrics("remove_rule");
            await SaveRulesAsync(ct).ConfigureAwait(false);
        }
        return removed;
    }

    /// <summary>
    /// 列出规则内部实现 — 由 PermissionActor Consumer 串行调用，无需锁
    /// </summary>
    private async Task<IReadOnlyList<AgentPermissionRule>> ListRulesInternalAsync(CancellationToken ct) {
        await EnsureRulesLoadedAsync(ct).ConfigureAwait(false);
        return _rules.Values.OrderByDescending(r => r.Priority).ToList();
    }

    /// <summary>
    /// 清空规则内部实现 — 由 PermissionActor Consumer 串行调用，无需锁
    /// </summary>
    private async Task ClearRulesInternalAsync(CancellationToken ct) {
        await EnsureRulesLoadedAsync(ct).ConfigureAwait(false);
        _rules.Clear();
        await SaveRulesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取匹配规则内部实现 — 由 PermissionActor Consumer 串行调用，无需锁
    /// </summary>
    private async Task<AgentPermissionRule?> GetMatchingRuleInternalAsync(string agentName, CancellationToken ct) {
        await EnsureRulesLoadedAsync(ct).ConfigureAwait(false);
        // 首先尝试精确匹配
        if (_rules.TryGetValue(agentName, out var exactMatch)) return exactMatch;

        // 尝试通配符匹配（按优先级降序遍历）
        foreach (var rule in _rules.Values.OrderByDescending(r => r.Priority)) {
            if (rule.AgentPattern != "*" && IsWildcardMatch(agentName, rule.AgentPattern)) {
                return rule;
            }
        }

        // 尝试默认规则 (*)
        return _rules.TryGetValue("*", out var defaultRule) ? defaultRule : null;
    }

    private void RecordPermissionManagerMetrics(string operation)
        => _telemetryService?.RecordCount("permission.manager.count", new() { ["operation"] = operation }, description: "Permission manager operation count");

    private static bool IsWildcardMatch(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);

    private static readonly AhoCorasick<string> AgentDestructiveToolAc = AhoCorasick.Create(
        ToolClassification.AgentDestructiveTools, ignoreCase: true);

    private static bool IsDestructiveTool(string toolName) {
        return ToolClassification.AgentDestructiveTools.Contains(toolName) ||
               AgentDestructiveToolAc.ContainsAny(toolName.AsSpan());
    }

    #endregion

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Agent 权限管理 Actor — 串行化全部权限操作，消除显式 AsyncLock — TASK001
    /// </summary>
    private sealed class PermissionActor : ActorBase<AgentPermissionCommand, Unit> {
        private readonly AgentPermissionManager _owner;
        private readonly ILogger<AgentPermissionManager>? _logger;

        /// <summary>构造 Agent 权限管理 Actor。</summary>
        /// <param name="owner">所属 AgentPermissionManager 实例。</param>
        /// <param name="logger">日志记录器。</param>
        public PermissionActor(AgentPermissionManager owner, ILogger<AgentPermissionManager>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复（无返回值）— 暴露 protected AskAwait 供 AgentPermissionManager 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（有返回值）— 暴露 protected AskAwait 供 AgentPermissionManager 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(AgentPermissionCommand cmd, CancellationToken ct) {
            try {
                switch (cmd) {
                    case AddRuleCmd(var rule, var reply):
                    await _owner.AddRuleInternalAsync(rule, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case RemoveRuleCmd(var agentPattern, var reply):
                    reply.SetResult(await _owner.RemoveRuleInternalAsync(agentPattern, ct).ConfigureAwait(false));
                    break;
                    case ListRulesCmd(var reply):
                    reply.SetResult(await _owner.ListRulesInternalAsync(ct).ConfigureAwait(false));
                    break;
                    case ClearRulesCmd(var reply):
                    await _owner.ClearRulesInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case GetMatchingRuleCmd(var agentName, var reply):
                    reply.SetResult(await _owner.GetMatchingRuleInternalAsync(agentName, ct).ConfigureAwait(false));
                    break;
                }
            } catch (OperationCanceledException) { throw; } catch (Exception ex) { SetExceptionOnReply(cmd, ex); }
        }

        private static void SetExceptionOnReply(AgentPermissionCommand cmd, Exception ex) {
            switch (cmd) {
                case AddRuleCmd(_, var r): r.SetException(ex); break;
                case RemoveRuleCmd(_, var r): r.SetException(ex); break;
                case ListRulesCmd(var r): r.SetException(ex); break;
                case ClearRulesCmd(var r): r.SetException(ex); break;
                case GetMatchingRuleCmd(_, var r): r.SetException(ex); break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "PermissionActor 命令处理异常");
    }
}

/// <summary>
/// 权限过滤工具
/// </summary>
public static class PermissionFilters {
    /// <summary>
    /// 异步过滤被拒绝的代理
    /// </summary>
    public static async Task<List<string>> FilterDeniedAgentsAsync(IAgentPermissionManager permissionManager, List<string> agentNames, CancellationToken ct = default) {
        var result = new List<string>();
        foreach (var name in agentNames) {
            var rule = await permissionManager.GetRuleForAgentAsync(name, ct).ConfigureAwait(false);
            if (rule?.Mode != PermissionMode.Ask) {
                result.Add(name);
            }
        }
        return result;
    }

    /// <summary>
    /// 异步获取代理的拒绝规则
    /// </summary>
    public static async Task<AgentPermissionRule?> GetDenyRuleForAgentAsync(IAgentPermissionManager permissionManager, string agentName, CancellationToken ct = default) {
        var rule = await permissionManager.GetRuleForAgentAsync(agentName, ct).ConfigureAwait(false);
        return rule?.Mode == PermissionMode.Ask ? rule : null;
    }

    /// <summary>
    /// 异步检查是否需要确认
    /// </summary>
    public static async Task<bool> RequiresConfirmationAsync(IAgentPermissionManager permissionManager, string agentName, string toolName, CancellationToken ct = default) {
        var result = await permissionManager.CheckToolPermissionAsync(agentName, toolName, null, ct).ConfigureAwait(false);
        return result.RequiresConfirmation;
    }

    /// <summary>
    /// 异步检查是否需要计划
    /// </summary>
    public static async Task<bool> RequiresPlanAsync(IAgentPermissionManager permissionManager, string agentName, string toolName, CancellationToken ct = default) {
        var result = await permissionManager.CheckToolPermissionAsync(agentName, toolName, null, ct).ConfigureAwait(false);
        return result.RequiresPlan;
    }
}