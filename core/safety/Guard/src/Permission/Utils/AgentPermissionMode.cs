namespace Core.Utils;

/// <summary>
/// Agent权限管理器实现
/// </summary>
[Register(typeof(IAgentPermissionManager), ServiceLifetime.Singleton)]
public sealed partial class AgentPermissionManager : IAgentPermissionManager, IAsyncDisposable
{
    private readonly Dictionary<string, AgentPermissionRule> _rules = new(StringComparer.Ordinal);
    private readonly AsyncLock _lock = new();
    private readonly ITelemetryService? _telemetryService;

    public AgentPermissionManager(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public async Task AddRuleAsync(AgentPermissionRule rule, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _rules.Remove(rule.AgentPattern);
            _rules[rule.AgentPattern] = rule;
            RecordPermissionManagerMetrics("add_rule");
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRuleAsync(string agentPattern, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            var removed = _rules.Remove(agentPattern);
            if (removed) RecordPermissionManagerMetrics("remove_rule");
            return removed;
        }
    }

    /// <inheritdoc />
    public async Task<PermissionCheckResult> CheckToolPermissionAsync(string agentName, string toolName, Dictionary<string, JsonElement>? parameters = null, CancellationToken ct = default)
    {
        var rule = await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);

        if (rule == null)
        {
            return new PermissionCheckResult
            {
                IsAllowed = true,
                Mode = PermissionMode.Auto,
                Reason = "未找到匹配的规则，默认允许"
            };
        }

        // 检查拒绝列表
        if (rule.DeniedTools?.Contains(toolName) == true)
        {
            return new PermissionCheckResult
            {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"工具 '{toolName}' 在拒绝列表中",
                MatchedRule = rule
            };
        }

        // 检查允许列表
        if (rule.AllowedTools?.Count > 0 && !rule.AllowedTools.Contains(toolName))
        {
            return new PermissionCheckResult
            {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"工具 '{toolName}' 不在允许列表中",
                MatchedRule = rule
            };
        }

        // 根据权限级别检查
        var canExecute = rule.Level >= PermissionLevel.Execute ||
                        (rule.Level >= PermissionLevel.Write && !IsDestructiveTool(toolName));

        if (!canExecute)
        {
            return new PermissionCheckResult
            {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"权限级别不足，当前级别: {rule.Level}",
                MatchedRule = rule
            };
        }

        return new PermissionCheckResult
        {
            IsAllowed = rule.Mode != PermissionMode.Ask,
            Mode = rule.Mode,
            Reason = $"匹配规则: {rule.Description ?? rule.AgentPattern}",
            MatchedRule = rule
        };
    }

    /// <inheritdoc />
    public async Task<PermissionCheckResult> CheckPathPermissionAsync(string agentName, string path, CancellationToken ct = default)
    {
        var rule = await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);

        if (rule == null)
        {
            return new PermissionCheckResult
            {
                IsAllowed = true,
                Mode = PermissionMode.Auto,
                Reason = "未找到匹配的规则，默认允许"
            };
        }

        var normalizedPath = Path.GetFullPath(path);

        if (rule.DeniedPaths?.Any(p => normalizedPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return new PermissionCheckResult
            {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"路径在拒绝列表中",
                MatchedRule = rule
            };
        }

        if (rule.AllowedPaths?.Count > 0 &&
            !rule.AllowedPaths.Any(p => normalizedPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return new PermissionCheckResult
            {
                IsAllowed = false,
                Mode = PermissionMode.Ask,
                Reason = $"路径不在允许列表中",
                MatchedRule = rule
            };
        }

        return new PermissionCheckResult
        {
            IsAllowed = rule.Mode != PermissionMode.Ask,
            Mode = rule.Mode,
            Reason = $"匹配规则: {rule.Description ?? rule.AgentPattern}",
            MatchedRule = rule
        };
    }

    /// <inheritdoc />
    public async Task<AgentPermissionRule?> GetRuleForAgentAsync(string agentName, CancellationToken ct = default)
    {
        return await GetMatchingRuleAsync(agentName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentPermissionRule>> ListRulesAsync(CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            return _rules.Values.OrderByDescending(r => r.Priority).ToList();
        }
    }

    /// <inheritdoc />
    public async Task ClearRulesAsync(CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _rules.Clear();
        }
    }

    #region Private Methods

    private async Task<AgentPermissionRule?> GetMatchingRuleAsync(string agentName, CancellationToken ct)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            // 首先尝试精确匹配
            if (_rules.TryGetValue(agentName, out var exactMatch)) return exactMatch;

            // 尝试通配符匹配（按优先级降序遍历）
            foreach (var rule in _rules.Values.OrderByDescending(r => r.Priority))
            {
                if (rule.AgentPattern != "*" && IsWildcardMatch(agentName, rule.AgentPattern))
                {
                    return rule;
                }
            }

            // 尝试默认规则 (*)
            return _rules.TryGetValue("*", out var defaultRule) ? defaultRule : null;
        }
    }

    private void RecordPermissionManagerMetrics(string operation)
        => _telemetryService?.RecordCount("permission.manager.count", new() { ["operation"] = operation }, description: "Permission manager operation count");

    private static bool IsWildcardMatch(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);

    private static readonly AhoCorasick<string> AgentDestructiveToolAc = AhoCorasick.Create(
        ToolClassification.AgentDestructiveTools, ignoreCase: true);

    private static bool IsDestructiveTool(string toolName)
    {
        return ToolClassification.AgentDestructiveTools.Contains(toolName) ||
               AgentDestructiveToolAc.ContainsAny(toolName.AsSpan());
    }

    #endregion

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}

/// <summary>
/// 权限过滤工具
/// </summary>
public static class PermissionFilters
{
    /// <summary>
    /// 异步过滤被拒绝的代理
    /// </summary>
    public static async Task<List<string>> FilterDeniedAgentsAsync(IAgentPermissionManager permissionManager, List<string> agentNames, CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var name in agentNames)
        {
            var rule = await permissionManager.GetRuleForAgentAsync(name, ct).ConfigureAwait(false);
            if (rule?.Mode != PermissionMode.Ask)
            {
                result.Add(name);
            }
        }
        return result;
    }

    /// <summary>
    /// 异步获取代理的拒绝规则
    /// </summary>
    public static async Task<AgentPermissionRule?> GetDenyRuleForAgentAsync(IAgentPermissionManager permissionManager, string agentName, CancellationToken ct = default)
    {
        var rule = await permissionManager.GetRuleForAgentAsync(agentName, ct).ConfigureAwait(false);
        return rule?.Mode == PermissionMode.Ask ? rule : null;
    }

    /// <summary>
    /// 异步检查是否需要确认
    /// </summary>
    public static async Task<bool> RequiresConfirmationAsync(IAgentPermissionManager permissionManager, string agentName, string toolName, CancellationToken ct = default)
    {
        var result = await permissionManager.CheckToolPermissionAsync(agentName, toolName, null, ct).ConfigureAwait(false);
        return result.RequiresConfirmation;
    }

    /// <summary>
    /// 异步检查是否需要计划
    /// </summary>
    public static async Task<bool> RequiresPlanAsync(IAgentPermissionManager permissionManager, string agentName, string toolName, CancellationToken ct = default)
    {
        var result = await permissionManager.CheckToolPermissionAsync(agentName, toolName, null, ct).ConfigureAwait(false);
        return result.RequiresPlan;
    }
}
