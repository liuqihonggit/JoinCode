namespace Core.Configuration;

/// <summary>
/// 工具评分热重载中间件 — settings.json 变更时更新黑名单、降权、超边配置
/// 双变量切换模式：构建新快照 → 原子替换引用，读取端无锁
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class ToolScoreSettingsMiddleware : ISettingsMiddleware
{
    [Inject] private readonly IToolHealthMonitor? _healthMonitor;
    [Inject] private readonly IHyperedgeReloadable? _hyperedgeReloadable;
    [Inject] private readonly ILogger<ToolScoreSettingsMiddleware>? _logger;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        if (context.NewSettings is not null)
        {
            ApplyBlacklistAndPenalties(context.NewSettings);
            ApplyHyperedges(context.NewSettings);
        }

        return next(context, ct);
    }

    private void ApplyBlacklistAndPenalties(SettingsJson settings)
    {
        if (_healthMonitor is null) return;

        if (settings.BlacklistedTools is { Count: > 0 })
        {
            var newBlacklist = new HashSet<string>(settings.BlacklistedTools, StringComparer.OrdinalIgnoreCase);
            _healthMonitor.UpdateBlacklist(newBlacklist);
        }

        if (settings.ToolPenalties is { Count: > 0 })
        {
            var newPenalties = new Dictionary<string, int>(settings.ToolPenalties, StringComparer.OrdinalIgnoreCase);
            _healthMonitor.UpdatePenalties(newPenalties);
        }
    }

    private void ApplyHyperedges(SettingsJson settings)
    {
        if (_hyperedgeReloadable is null) return;

        if (settings.CustomHyperedges is not { Count: > 0 }) return;

        _hyperedgeReloadable.LoadCustomHyperedges(settings.CustomHyperedges);
        _logger?.LogInformation("超图自定义超边已热重载: {Count} 条", settings.CustomHyperedges.Count);
    }
}
