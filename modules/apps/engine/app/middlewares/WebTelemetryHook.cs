namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<WebContext>), ServiceLifetime.Singleton)]
internal sealed partial class WebTelemetryHook : TelemetryPostHook<WebContext> {
    /// <summary>初始化 Web 抓取管道遥测后置钩子实例。</summary>
    public WebTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "web.fetch.count", "Web pipeline count") { }
}