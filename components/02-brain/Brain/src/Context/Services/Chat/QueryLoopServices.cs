using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// QueryLoopMiddleware 的可选服务聚合 — 减少构造函数参数注入
/// </summary>
[Register]
public sealed record QueryLoopServices(
    IChatContentReplacer? ContentReplacer = null,
    IChatFileContextService? FileContextService = null,
    IChatIdleDetector? IdleDetector = null,
    ITelemetryService? TelemetryService = null,
    IPostSamplingCallbackManager? PostSamplingCallbacks = null)
{
    /// <summary>
    /// 从 DI 容器解析所有可选服务 — 保持向后兼容
    /// </summary>
    public static QueryLoopServices FromServiceProvider(IServiceProvider sp) => new(
        ContentReplacer: sp.GetService<IChatContentReplacer>(),
        FileContextService: sp.GetService<IChatFileContextService>(),
        IdleDetector: sp.GetService<IChatIdleDetector>(),
        TelemetryService: sp.GetService<ITelemetryService>(),
        PostSamplingCallbacks: sp.GetService<IPostSamplingCallbackManager>());
}
