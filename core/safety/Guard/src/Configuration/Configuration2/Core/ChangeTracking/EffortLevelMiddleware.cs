namespace Core.Configuration;

/// <summary>
/// EffortLevel 更新中间件 — 对齐 TS 版 effortChanged 逻辑
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class EffortLevelMiddleware : ISettingsMiddleware
{
    [Inject] private readonly IExecutionSettingsProvider? _executionSettingsProvider;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        if (_executionSettingsProvider is not null && context.NewSettings is not null)
        {
            var newEffort = EffortLevelHelper.ParseEffortLevel(context.NewSettings.EffortLevel);
            if (newEffort is not null && _executionSettingsProvider.EffortLevel != newEffort)
            {
                _executionSettingsProvider.EffortLevel = newEffort.Value;
                context.Logger?.LogInformation("EffortLevel 已更新: {Level}", newEffort.Value.ToValue());
            }
        }

        return next(context, ct);
    }
}
