namespace Core.Configuration;

/// <summary>
/// 工具评分热重载中间件 — settings.json 变更时更新黑名单、降权、超边配置
/// 双变量切换模式：构建新快照 → 原子替换引用，读取端无锁
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class ToolScoreSettingsMiddleware : ServiceEntity, ISettingsMiddleware
{
    [Inject] private readonly IToolHealthMonitor? _healthMonitor;
    [Inject] private readonly IHyperedgeReloadable? _hyperedgeReloadable;
    [Inject] private readonly ISearchScopeReloadable? _searchScopeReloadable;
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
            ApplySearchScope(context.NewSettings);
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

    private void ApplySearchScope(SettingsJson settings)
    {
        if (_searchScopeReloadable is null) return;

        var scopeSettings = settings.SearchScope;
        var config = new SearchScopeConfig
        {
            Enabled = scopeSettings?.Enabled ?? true,
            ExtraDangerousFlags = BuildExtraDangerousFlags(scopeSettings),
            ExtraExcessivePathPrefixes = scopeSettings?.ExtraExcessivePathPrefixes is { Count: > 0 }
                ? FrozenSet.Create(StringComparer.OrdinalIgnoreCase, [.. scopeSettings.ExtraExcessivePathPrefixes])
                : FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase),
        };

        _searchScopeReloadable.ReloadSearchScope(config);
        _logger?.LogInformation("搜索范围安全配置已热重载: Enabled={Enabled}, ExtraFlags={FlagCount}, ExtraPaths={PathCount}",
            config.Enabled, config.ExtraDangerousFlags.Count, config.ExtraExcessivePathPrefixes.Count);
    }

    private static Dictionary<string, FrozenSet<string>> BuildExtraDangerousFlags(SearchScopeSettings? settings)
    {
        if (settings?.ExtraDangerousFlags is not { Count: > 0 })
        {
            return new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cmd, flags) in settings.ExtraDangerousFlags)
        {
            if (flags is { Count: > 0 })
            {
                result[cmd] = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, [.. flags]);
            }
        }

        return result;
    }
}
