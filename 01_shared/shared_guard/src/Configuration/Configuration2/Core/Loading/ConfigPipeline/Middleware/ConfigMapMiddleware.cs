
namespace Core.Configuration.ConfigPipeline;

[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ConfigMapMiddleware : ServiceEntity, IConfigLoadMiddleware
{
    private readonly SettingsMapper _mapper;

    public ConfigMapMiddleware(SettingsMapper mapper)
    {
        _mapper = mapper;
    }


    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        context.Config = _mapper.ToWorkflowConfig(context.Settings);

        return next(context, ct);
    }
}
