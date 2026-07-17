
namespace Core.Policy;

[Register]
[Register(typeof(JoinCode.Abstractions.Interfaces.IRemotePolicyService))]
public sealed partial class RemotePolicyService : RemoteCacheRefreshServiceBase<PolicyRule>, JoinCode.Abstractions.Interfaces.IRemotePolicyService
{
    private static readonly PolicyJsonContext JsonContext = PolicyJsonContext.Default;

    private readonly ConcurrentDictionary<string, int> _usageCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _windowStartTimes = new(StringComparer.OrdinalIgnoreCase);

    protected override string MetricsPrefix => "policy.remote";
    protected override string RefreshLogLabel => "远程策略";

    public event EventHandler<PolicyEvaluationResult>? PolicyViolated;

    public RemotePolicyService(
        HttpClient httpClient,
        IOptions<RemotePolicyOptions>? options = null,
        ILogger<RemotePolicyService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
        : base(httpClient, options?.Value ?? new RemotePolicyOptions(), logger, telemetryService, clock)
    {
    }

    protected override async Task<RemoteRefreshResult<PolicyRule>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var policyResponse = JsonSerializer.Deserialize(json, JsonContext.PolicyFetchResponse);

        return new RemoteRefreshResult<PolicyRule>
        {
            Items = policyResponse?.Rules?.ToDictionary(r => r.RuleId, r => r, StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task<PolicyEvaluationResult> EvaluateAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var applicableRules = Cache.Values
            .Where(r => r.Enabled)
            .Where(r => MatchesAction(r, action))
            .OrderByDescending(r => r.Priority)
            .ToList();

        foreach (var rule in applicableRules)
        {
            var result = EvaluateRule(rule, action, context);
            if (result.Action == PolicyAction.Deny)
            {
                var options = RefreshOptions as RemotePolicyOptions;
                if (options?.EnableNotifications == true)
                {
                    PolicyViolated?.Invoke(this, result);
                }

                return result;
            }

            if (result.Action == PolicyAction.Warn)
            {
                Logger?.LogWarning("策略警告: {RuleName} - {Reason}", rule.Name, result.Reason);
            }
        }

        return new PolicyEvaluationResult
        {
            RuleId = "default",
            Allowed = true,
            Action = PolicyAction.Allow,
            Reason = "无策略限制"
        };
    }

    public async Task<IReadOnlyList<PolicyRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return Cache.Values.Where(r => r.Enabled).OrderByDescending(r => r.Priority).ToList();
    }

    public async Task<IReadOnlyList<PolicyEvaluationResult>> EvaluateAllAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var applicableRules = Cache.Values
            .Where(r => r.Enabled)
            .Where(r => MatchesAction(r, action))
            .OrderByDescending(r => r.Priority)
            .ToList();

        var results = new List<PolicyEvaluationResult>();

        foreach (var rule in applicableRules)
        {
            var result = EvaluateRule(rule, action, context);
            results.Add(result);
        }

        return results;
    }

    private PolicyEvaluationResult EvaluateRule(PolicyRule rule, string action, Dictionary<string, string>? context)
    {
        if (!MatchesConditions(rule, context))
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow,
                Reason = "条件不匹配"
            };
        }

        return rule.Type switch
        {
            PolicyType.ToolUsageLimit => EvaluateUsageLimit(rule, action),
            PolicyType.CostLimit => EvaluateCostLimit(rule),
            PolicyType.RateLimit => EvaluateRateLimit(rule, action),
            PolicyType.ToolRestriction => EvaluateToolRestriction(rule, action),
            PolicyType.TimeRestriction => EvaluateTimeRestriction(rule),
            _ => new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow,
                Reason = "未知策略类型"
            }
        };
    }

    private PolicyEvaluationResult EvaluateUsageLimit(PolicyRule rule, string action)
    {
        var counterKey = $"{rule.RuleId}:{action}";
        var currentCount = _usageCounters.GetValueOrDefault(counterKey, 0);

        if (rule.Limit.HasValue && currentCount >= rule.Limit.Value)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = false,
                Action = rule.Action,
                Reason = $"已达到使用限制: {currentCount}/{rule.Limit.Value}",
                RemainingLimit = 0
            };
        }

        _usageCounters.AddOrUpdate(counterKey, 1, (_, v) => v + 1);

        return new PolicyEvaluationResult
        {
            RuleId = rule.RuleId,
            Allowed = true,
            Action = PolicyAction.Allow,
            RemainingLimit = rule.Limit.HasValue ? rule.Limit.Value - currentCount - 1 : null
        };
    }

    private PolicyEvaluationResult EvaluateCostLimit(PolicyRule rule)
    {
        if (!rule.CostLimit.HasValue)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow
            };
        }

        var costKey = $"{rule.RuleId}:cost";
        var currentCost = _usageCounters.GetValueOrDefault(costKey, 0) / 100.0;

        if (currentCost >= rule.CostLimit.Value)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = false,
                Action = rule.Action,
                Reason = $"已达到成本限制: {currentCost:F2}/{rule.CostLimit.Value:F2}"
            };
        }

        return new PolicyEvaluationResult
        {
            RuleId = rule.RuleId,
            Allowed = true,
            Action = PolicyAction.Allow
        };
    }

    private PolicyEvaluationResult EvaluateRateLimit(PolicyRule rule, string action)
    {
        if (!rule.Window.HasValue)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow
            };
        }

        var windowKey = $"{rule.RuleId}:{action}:window";
        var counterKey = $"{rule.RuleId}:{action}:rate";
        var now = Clock.GetUtcNow();

        if (!_windowStartTimes.TryGetValue(windowKey, out var windowStart) ||
            now - windowStart >= rule.Window.Value)
        {
            _windowStartTimes[windowKey] = now;
            _usageCounters[counterKey] = 1;

            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow,
                RemainingLimit = rule.Limit.HasValue ? rule.Limit.Value - 1 : null
            };
        }

        var currentCount = _usageCounters.GetValueOrDefault(counterKey, 0);

        if (rule.Limit.HasValue && currentCount >= rule.Limit.Value)
        {
            var retryAfter = rule.Window.Value - (now - windowStart);

            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = false,
                Action = rule.Action,
                Reason = $"速率限制: {currentCount}/{rule.Limit.Value} 每 {rule.Window.Value}",
                RetryAfter = retryAfter
            };
        }

        _usageCounters.AddOrUpdate(counterKey, 1, (_, v) => v + 1);

        return new PolicyEvaluationResult
        {
            RuleId = rule.RuleId,
            Allowed = true,
            Action = PolicyAction.Allow,
            RemainingLimit = rule.Limit.HasValue ? rule.Limit.Value - currentCount - 1 : null
        };
    }

    private PolicyEvaluationResult EvaluateToolRestriction(PolicyRule rule, string action)
    {
        if (rule.RestrictedTools == null || rule.RestrictedTools.Count == 0)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow
            };
        }

        var isRestricted = rule.RestrictedTools.Any(t =>
            string.Equals(t, action, StringComparison.OrdinalIgnoreCase));

        if (isRestricted)
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = false,
                Action = rule.Action,
                Reason = $"工具 '{action}' 被限制"
            };
        }

        return new PolicyEvaluationResult
        {
            RuleId = rule.RuleId,
            Allowed = true,
            Action = PolicyAction.Allow
        };
    }

    private PolicyEvaluationResult EvaluateTimeRestriction(PolicyRule rule)
    {
        if (rule.Conditions == null || !rule.Conditions.TryGetValue("allowedHours", out var hoursStr))
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = true,
                Action = PolicyAction.Allow
            };
        }

        var currentHour = Clock.GetUtcNow().Hour;
        var allowedHours = hoursStr.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var h) ? (int?)h : null)
            .Where(h => h.HasValue)
            .Select(h => h.GetValueOrDefault())
            .ToHashSet();

        if (allowedHours.Count > 0 && !allowedHours.Contains(currentHour))
        {
            return new PolicyEvaluationResult
            {
                RuleId = rule.RuleId,
                Allowed = false,
                Action = rule.Action,
                Reason = $"当前时间 {currentHour}:00 不在允许范围内"
            };
        }

        return new PolicyEvaluationResult
        {
            RuleId = rule.RuleId,
            Allowed = true,
            Action = PolicyAction.Allow
        };
    }

    private static bool MatchesAction(PolicyRule rule, string action)
    {
        if (rule.Type == PolicyType.ToolRestriction && rule.RestrictedTools != null)
        {
            return rule.RestrictedTools.Any(t =>
                string.Equals(t, action, StringComparison.OrdinalIgnoreCase) ||
                t == "*");
        }

        return true;
    }

    private static bool MatchesConditions(PolicyRule rule, Dictionary<string, string>? context)
    {
        if (rule.Conditions == null || rule.Conditions.Count == 0) return true;
        if (context == null || context.Count == 0) return rule.Conditions.Count == 0;

        foreach (var condition in rule.Conditions)
        {
            if (condition.Key == "allowedHours") continue;

            if (!context.TryGetValue(condition.Key, out var value) ||
                !string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
