namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 6: 规则赋值
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class RulesAssignMiddleware : IConfigLoadMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var config = context.Config;
        config.ProjectRules = context.ProjectRules;
        config.ExternalRules = context.ExternalRules;

        return next(context, ct);
    }
}
