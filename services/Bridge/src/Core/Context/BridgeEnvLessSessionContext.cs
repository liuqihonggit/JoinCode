namespace Core.Bridge;

/// <summary>
/// Env-less 桥会话上下文 — 聚合会话标识 + 初始化状态 + 核心参数
/// </summary>
internal sealed record BridgeEnvLessSessionContext
{
    /// <summary>
    /// 会话标识聚合
    /// </summary>
    public required BridgeSessionInfo Session { get; init; }

    /// <summary>
    /// 桥初始化共享状态
    /// </summary>
    public required BridgeInitState State { get; init; }

    /// <summary>
    /// v2 env-less 桥核心参数
    /// </summary>
    public required BridgeEnvLessParams Parameters { get; init; }
}
