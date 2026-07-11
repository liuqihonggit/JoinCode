
namespace Core.Policy;

[Register]
[Register(typeof(JoinCode.Abstractions.Interfaces.IRemotePolicyService))]
public sealed partial class RemotePolicyService : JoinCode.Abstractions.Interfaces.IRemotePolicyService, IDisposable
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<RemotePolicyService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly RemotePolicyOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;
    private readonly ConcurrentDictionary<string, PolicyRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _usageCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _windowStartTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCts = new();
    private DateTime _lastFetchTime = DateTime.MinValue;
    private bool _disposed;

    private static readonly PolicyJsonContext JsonContext = PolicyJsonContext.Default;

    public event EventHandler<PolicyEvaluationResult>? PolicyViolated;

    public RemotePolicyService(
        HttpClient httpClient,
        IOptions<RemotePolicyOptions>? options = null,
        ILogger<RemotePolicyService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _options = options?.Value ?? new RemotePolicyOptions();

        if (!string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _refreshTimer = new Timer(
                _ => _ = RefreshAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false),
                null,
                _options.RefreshInterval,
                _options.RefreshInterval);
        }
        else
        {
            _refreshTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<PolicyEvaluationResult> EvaluateAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var applicableRules = _rules.Values
            .Where(r => r.Enabled)
            .Where(r => MatchesAction(r, action))
            .OrderByDescending(r => r.Priority)
            .ToList();

        foreach (var rule in applicableRules)
        {
            var result = EvaluateRule(rule, action, context);
            if (result.Action == PolicyAction.Deny)
            {
                if (_options.EnableNotifications)
                {
                    PolicyViolated?.Invoke(this, result);
                }

                return result;
            }

            if (result.Action == PolicyAction.Warn)
            {
                _logger?.LogWarning("策略警告: {RuleName} - {Reason}", rule.Name, result.Reason);
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
        return _rules.Values.Where(r => r.Enabled).OrderByDescending(r => r.Priority).ToList();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _logger?.LogDebug("未配置远程策略 API 端点，跳过刷新");
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger?.LogDebug("正在刷新远程策略配置");

            var requestUrl = _options.ApiEndpoint;
            if (!string.IsNullOrEmpty(_options.ClientKey))
            {
                var separator = requestUrl.Contains('?') ? "&" : "?";
                requestUrl = $"{requestUrl}{separator}clientKey={Uri.EscapeDataString(_options.ClientKey)}";
            }

            var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var policyResponse = JsonSerializer.Deserialize(json, JsonContext.PolicyFetchResponse);

            if (policyResponse?.Rules != null)
            {
                _rules.Clear();
                foreach (var rule in policyResponse.Rules)
                {
                    _rules[rule.RuleId] = rule;
                }

                _lastFetchTime = _clock.GetUtcNow();
                _logger?.LogInformation("已刷新 {Count} 条远程策略规则", policyResponse.Rules.Count);
                RecordPolicyMetrics("refresh", true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "刷新远程策略失败");
            RecordPolicyMetrics("refresh", false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<IReadOnlyList<PolicyEvaluationResult>> EvaluateAllAsync(string action, Dictionary<string, string>? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var applicableRules = _rules.Values
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
        var now = _clock.GetUtcNow();

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

        var currentHour = _clock.GetUtcNow().Hour;
        var allowedHours = hoursStr.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var h) ? (int?)h : null)
            .Where(h => h.HasValue)
            .Select(h => h!.Value)
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
        if (rule.Conditions == null || rule.Conditions.Count == 0)
        {
            return true;
        }

        if (context == null || context.Count == 0)
        {
            return rule.Conditions.Count == 0;
        }

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

    private void RecordPolicyMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("policy.remote.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Remote policy operation count");

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_rules.IsEmpty || (_options.EnableCache && _clock.GetUtcNow() - _lastFetchTime > _options.CacheExpiration))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
        _disposeCts.Dispose();
        _disposed = true;
    }

    async Task<JoinCode.Abstractions.Models.Policy.PolicyEvaluationResult> JoinCode.Abstractions.Interfaces.IRemotePolicyService.EvaluateAsync(
        string action, Dictionary<string, string>? context, CancellationToken cancellationToken)
    {
        var result = await EvaluateAsync(action, context, cancellationToken).ConfigureAwait(false);
        return MapToContractResult(result);
    }

    async Task<IReadOnlyList<JoinCode.Abstractions.Models.Policy.PolicyRule>> JoinCode.Abstractions.Interfaces.IRemotePolicyService.GetActiveRulesAsync(
        CancellationToken cancellationToken)
    {
        var rules = await GetActiveRulesAsync(cancellationToken).ConfigureAwait(false);
        return rules.Select(MapToContractRule).ToList();
    }

    Task JoinCode.Abstractions.Interfaces.IRemotePolicyService.RefreshAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(cancellationToken);
    }

    async Task<IReadOnlyList<JoinCode.Abstractions.Models.Policy.PolicyEvaluationResult>> JoinCode.Abstractions.Interfaces.IRemotePolicyService.EvaluateAllAsync(
        string action, Dictionary<string, string>? context, CancellationToken cancellationToken)
    {
        var results = await EvaluateAllAsync(action, context, cancellationToken).ConfigureAwait(false);
        return results.Select(MapToContractResult).ToList();
    }

    private static JoinCode.Abstractions.Models.Policy.PolicyEvaluationResult MapToContractResult(PolicyEvaluationResult result)
    {
        return new JoinCode.Abstractions.Models.Policy.PolicyEvaluationResult
        {
            RuleId = result.RuleId,
            Allowed = result.Allowed,
            Action = (JoinCode.Abstractions.Models.Policy.PolicyAction)result.Action,
            Reason = result.Reason,
            Metadata = result.Metadata,
            RemainingLimit = result.RemainingLimit,
            RetryAfter = result.RetryAfter
        };
    }

    private static JoinCode.Abstractions.Models.Policy.PolicyRule MapToContractRule(PolicyRule rule)
    {
        return new JoinCode.Abstractions.Models.Policy.PolicyRule
        {
            RuleId = rule.RuleId,
            Name = rule.Name,
            Type = (JoinCode.Abstractions.Models.Policy.PolicyType)rule.Type,
            Action = (JoinCode.Abstractions.Models.Policy.PolicyAction)rule.Action,
            Conditions = rule.Conditions,
            Limit = rule.Limit,
            Window = rule.Window,
            CostLimit = rule.CostLimit,
            RestrictedTools = rule.RestrictedTools,
            Enabled = rule.Enabled,
            Priority = rule.Priority,
            UpdatedAt = rule.UpdatedAt
        };
    }
}
