namespace Core.Configuration;

/// <summary>
/// 子代理并发热重载中间件 — settings.json 变更时更新 spawn/execute/fork 三阶段上限（ADR 0048）
/// 通过 DI 注入 IEnumerable&lt;ISubAgentConcurrencyUpdater&gt; 调用各组件 UpdateConcurrencyOptions
/// </summary>
[Register(typeof(ISettingsMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SubAgentConcurrencyMiddleware : ServiceEntity, ISettingsMiddleware
{
    private readonly ISubAgentConcurrencyUpdater[] _updaters;
    private readonly ILogger<SubAgentConcurrencyMiddleware>? _logger;

    public SubAgentConcurrencyMiddleware(
        IEnumerable<ISubAgentConcurrencyUpdater>? updaters = null,
        ILogger<SubAgentConcurrencyMiddleware>? logger = null)
    {
        _updaters = updaters?.ToArray() ?? [];
        _logger = logger;
    }

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        var sub = context.NewSettings?.Current?.SubAgentConcurrency;
        if (sub is not null && _updaters.Length > 0)
        {
            sub.Validate();
            foreach (var updater in _updaters)
            {
                updater.UpdateConcurrencyOptions(sub);
            }
            _logger?.LogInformation("子代理并发配置已热重载: spawns={Spawns}, executions={Executions}, forks={Forks}",
                sub.MaxConcurrentSpawns, sub.MaxConcurrentExecutions, sub.MaxConcurrentForks);
        }

        return next(context, ct);
    }
}
