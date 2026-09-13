namespace Core.Context;

/// <summary>
/// QueryLoopMiddleware 的可选服务聚合 — 减少构造函数参数注入
/// </summary>
/// <param name="ContentReplacer">内容替换器（可选）</param>
/// <param name="FileContextService">文件上下文服务（可选）</param>
/// <param name="IdleDetector">聊天空闲检测器（可选）</param>
/// <param name="TelemetryService">遥测服务（可选）</param>
/// <param name="PostSamplingCallbacks">采样后回调管理器（可选）</param>
[Register(typeof(QueryLoopServices), ServiceLifetime.Singleton)]
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
    /// <param name="sp">DI 服务提供者</param>
    /// <returns>从容器解析的 QueryLoopServices 实例</returns>
    public static QueryLoopServices FromServiceProvider(IServiceProvider sp) => new(
        ContentReplacer: sp.GetService<IChatContentReplacer>(),
        FileContextService: sp.GetService<IChatFileContextService>(),
        IdleDetector: sp.GetService<IChatIdleDetector>(),
        TelemetryService: sp.GetService<ITelemetryService>(),
        PostSamplingCallbacks: sp.GetService<IPostSamplingCallbackManager>());
}
