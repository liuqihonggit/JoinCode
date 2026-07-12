
namespace Core.Configuration.ConfigPipeline;

[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class ApiKeyResolveMiddleware : IConfigLoadMiddleware
{
    private readonly IFileSystem _fs;
    private readonly ConfigLoader _loader;

    public ApiKeyResolveMiddleware(IFileSystem fs, ConfigLoader loader)
    {
        _fs = fs;
        _loader = loader;
    }

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var config = context.Config;
        config.Provider.ApiKey = await _loader.ResolveApiKeyAsync(
            config.Provider.Provider, config.Provider.Definition, _fs, ct).ConfigureAwait(false);

        context.ResolvedApiKey = config.Provider.ApiKey;

        await next(context, ct).ConfigureAwait(false);
    }
}
