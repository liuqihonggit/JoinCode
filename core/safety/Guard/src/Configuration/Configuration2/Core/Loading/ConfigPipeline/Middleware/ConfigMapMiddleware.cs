
namespace Core.Configuration.ConfigPipeline;

[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class ConfigMapMiddleware : IConfigLoadMiddleware
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
