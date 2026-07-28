namespace Core.Bridge;

/// <summary>
/// Env-less 桥传输上下文 — 聚合传输层 + HTTP 客户端 + 配置 + 令牌刷新调度器
/// </summary>
internal sealed record BridgeEnvLessTransportContext
{
    /// <summary>
    /// REPL 桥传输
    /// </summary>
    public required IReplBridgeTransport Transport { get; init; }

    /// <summary>
    /// HTTP 客户端
    /// </summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// Env-less 桥配置
    /// </summary>
    public required BridgeEnvLessConfig Config { get; init; }

    /// <summary>
    /// 令牌刷新调度器
    /// </summary>
    public required BridgeTokenRefreshScheduler Refresh { get; init; }
}
