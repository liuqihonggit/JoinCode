namespace Core.Bridge;

/// <summary>
/// 桥会话标识聚合 — 将 sessionId、environmentId、sessionIngressUrl 合并为单一参数
/// </summary>
public sealed record BridgeSessionInfo
{
    /// <summary>
    /// 会话 ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 环境 ID（v2 env-less 模式为空字符串）
    /// </summary>
    public required string EnvironmentId { get; init; }

    /// <summary>
    /// 会话入口 URL
    /// </summary>
    public required string SessionIngressUrl { get; init; }
}
