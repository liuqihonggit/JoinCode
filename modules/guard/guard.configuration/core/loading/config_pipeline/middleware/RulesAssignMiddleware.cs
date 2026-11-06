namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 6: 规则赋值
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RulesAssignMiddleware : ServiceEntity, IConfigLoadMiddleware {

    /// <inheritdoc/>
    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct) {
        context.Config = context.Config with {
            ProjectRules = context.ProjectRules,
            ExternalRules = context.ExternalRules
        };

        return next(context, ct);
    }
}