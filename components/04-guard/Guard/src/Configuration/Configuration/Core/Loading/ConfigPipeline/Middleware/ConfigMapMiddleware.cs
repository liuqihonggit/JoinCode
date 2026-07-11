namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 3: SettingsJson → WorkflowConfig 映射
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class ConfigMapMiddleware : IConfigLoadMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        context.Config = SettingsMapper.ToWorkflowConfig(context.Settings);

        return next(context, ct);
    }
}
