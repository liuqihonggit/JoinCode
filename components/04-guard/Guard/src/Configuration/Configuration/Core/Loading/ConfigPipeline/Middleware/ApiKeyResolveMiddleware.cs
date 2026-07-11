namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 5: 统一 API Key 解析（auth.json → JCC_API_KEY → Provider 专属变量）
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class ApiKeyResolveMiddleware : IConfigLoadMiddleware
{
    [Inject] private readonly IFileSystem _fs;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var config = context.Config;
        config.Provider.ApiKey = await ConfigLoader.ResolveApiKeyAsync(
            config.Provider.Provider, config.Provider.Definition, _fs, ct).ConfigureAwait(false);

        context.ResolvedApiKey = config.Provider.ApiKey;

        await next(context, ct).ConfigureAwait(false);
    }
}
