
namespace Core.Configuration.ConfigPipeline;

[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class EnvOverrideMiddleware : IConfigLoadMiddleware
{
    private readonly SettingsMapper _mapper;

    public EnvOverrideMiddleware(SettingsMapper mapper)
    {
        _mapper = mapper;
    }

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        _mapper.ApplyEnvOverrides(context.Config);

        return next(context, ct);
    }
}
