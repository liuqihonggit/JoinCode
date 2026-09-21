namespace JoinCode.App.Middlewares;

[Register(typeof(IPipelinePostHook<QueryMiddlewareContext>), ServiceLifetime.Singleton)]
internal sealed partial class QueryTelemetryHook : TelemetryPostHook<QueryMiddlewareContext> {
    /// <summary>初始化查询管道遥测后置钩子实例。</summary>
    public QueryTelemetryHook(ITelemetryService? telemetryService)
        : base(telemetryService, "query.count", "Query pipeline count") { }
}