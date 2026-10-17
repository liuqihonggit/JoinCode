namespace Services.Web;

/// <summary>
/// SSRF 私有网络地址检查中间件 — 阻止请求访问内网/链路本地/回环地址
/// Order=150 在 URL 验证(100)之后、缓存检查(200)之前执行
/// <para>
/// 对齐 Reasonix web_fetch SSRF 防护：在 DNS 解析后检查解析后的 IP 地址。
/// 判定逻辑委托 <see cref="PrivateNetworkGuard"/> 的 <see langword="ValidateHost"/>（唯一数据源, ADR 0140），
/// 本层只负责调度与拒绝文案，不复制判定。
/// </para>
/// <para>
/// fail-closed (ADR 0140)：DNS 解析失败或解析结果不可验证时拒绝请求，禁止放行。
/// <see cref="WebFetchMiddleware"/>（Order=400）在每次请求(含重定向跟随)前复检同一入口。
/// </para>
/// </summary>
[Register(typeof(IWebMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WebSsrfGuardMiddleware : ServiceEntity, IWebMiddleware {

    /// <summary>
    /// 初始化 <see cref="WebSsrfGuardMiddleware"/> 实例。
    /// </summary>
    /// <param name="logger">可选的日志记录器。</param>
    /// <param name="dnsResolver">可选的 DNS 解析函数(惰性注入仅测试使用,生产默认 Dns.GetHostAddresses)。</param>
    public WebSsrfGuardMiddleware(
        ILogger<WebSsrfGuardMiddleware>? logger = null,
        Func<string, IPAddress[]>? dnsResolver = null) {
        _logger = logger;
        _dnsResolver = dnsResolver ?? Dns.GetHostAddresses;
    }
    private readonly ILogger<WebSsrfGuardMiddleware>? _logger;

    private readonly Func<string, IPAddress[]> _dnsResolver;

    /// <inheritdoc />
    public Task InvokeAsync(WebContext context, MiddlewareDelegate<WebContext> next, CancellationToken ct) {
        var url = context.UpgradedUrl ?? context.Url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) {
            return next(context, ct);
        }

        var decision = PrivateNetworkGuard.ValidateHost(parsed.Host, _dnsResolver);
        if (!decision.Allowed) {
            var message = $"{decision.Reason}\n\n" +
                          $"拦截层: SSRF guard (WebSsrfGuardMiddleware)\n" +
                          "正确做法: 确认目标主机可公网解析且非内网地址后再请求，内网地址不允许通过 web 工具访问";
            context.Result = new WebFetchResult(false, context.Url, ErrorMessage: message);
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
