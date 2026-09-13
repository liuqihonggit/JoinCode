
namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// API Key 解析中间件 — 在配置加载管道中解析供应商 API Key 并写入上下文
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ApiKeyResolveMiddleware : ServiceEntity, IConfigLoadMiddleware
{
    private readonly IFileSystem _fs;
    private readonly ConfigLoader _loader;

    /// <summary>
    /// 构造 API Key 解析中间件
    /// </summary>
    public ApiKeyResolveMiddleware(IFileSystem fs, ConfigLoader loader)
    {
        _fs = fs;
        _loader = loader;
    }


    /// <inheritdoc />
    public async Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var config = context.Config;
        config.Provider.ApiKey = await _loader.ResolveApiKeyAsync(
            config.Provider.Vendor, config.Provider.Definition, _fs, ct).ConfigureAwait(false);

        context.ResolvedApiKey = config.Provider.ApiKey;

        await next(context, ct).ConfigureAwait(false);
    }
}
