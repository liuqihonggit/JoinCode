namespace Core.Security.Services;

[Register]
public sealed partial class HttpProxyService : IHttpProxyService
{
    [Inject] private readonly ILogger<HttpProxyService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly ProxyOptions? _currentSettings;

    public HttpProxyService(ILogger<HttpProxyService>? logger = null, ITelemetryService? telemetryService = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
        _currentSettings = LoadFromEnvironment();
    }

    public bool IsProxyConfigured => !string.IsNullOrEmpty(_currentSettings?.ProxyUrl);

    public HttpClientHandler CreateProxyHandler(ProxyOptions? options = null)
    {
        var effectiveOptions = options ?? _currentSettings ?? new ProxyOptions();
        var handler = new HttpClientHandler();

        if (string.IsNullOrEmpty(effectiveOptions.ProxyUrl))
        {
            return handler;
        }

        try
        {
            var proxyUri = new Uri(effectiveOptions.ProxyUrl);
            var proxy = new WebProxy(proxyUri);

            if (!string.IsNullOrEmpty(effectiveOptions.ProxyUsername))
            {
                proxy.Credentials = new System.Net.NetworkCredential(
                    effectiveOptions.ProxyUsername,
                    effectiveOptions.ProxyPassword ?? string.Empty);
            }
            else if (effectiveOptions.UseDefaultCredentials)
            {
                proxy.Credentials = System.Net.CredentialCache.DefaultCredentials;
            }

            if (effectiveOptions.BypassHosts is { Count: > 0 })
            {
                proxy.BypassList = effectiveOptions.BypassHosts.ToArray();
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;

            _logger?.LogInformation("[HttpProxyService] 已配置 HTTP 代理: {ProxyUrl}", effectiveOptions.ProxyUrl);
            RecordProxyMetrics("create_handler", true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[HttpProxyService] 配置代理失败: {ProxyUrl}", effectiveOptions.ProxyUrl);
            RecordProxyMetrics("create_handler", false);
        }

        return handler;
    }

    public ProxyOptions GetCurrentProxySettings()
    {
        return _currentSettings ?? new ProxyOptions();
    }

    private void RecordProxyMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("proxy.operation.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Proxy operation count");

    private static ProxyOptions? LoadFromEnvironment()
    {
        var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                         ?? Environment.GetEnvironmentVariable("https_proxy");

        var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY")
                        ?? Environment.GetEnvironmentVariable("http_proxy");

        var proxyUrl = httpsProxy ?? httpProxy;

        if (string.IsNullOrEmpty(proxyUrl))
        {
            return null;
        }

        var noProxy = Environment.GetEnvironmentVariable("NO_PROXY")
                      ?? Environment.GetEnvironmentVariable("no_proxy");

        var bypassHosts = new List<string>();
        if (!string.IsNullOrEmpty(noProxy))
        {
            bypassHosts.AddRange(noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new ProxyOptions
        {
            ProxyUrl = proxyUrl,
            BypassHosts = bypassHosts.Count > 0 ? bypassHosts : null,
            UseDefaultCredentials = true
        };
    }
}
