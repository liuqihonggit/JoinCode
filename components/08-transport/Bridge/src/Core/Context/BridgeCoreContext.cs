namespace Core.Bridge;

/// <summary>
/// 桥核心上下文聚合 — 将 BridgeWorkPollLoop + BridgeCoreParams 合并为单一参数
/// </summary>
public sealed record BridgeCoreContext
{
    /// <summary>
    /// 工作轮询循环 — 对齐 TS 端 workPollLoop
    /// </summary>
    public required BridgeWorkPollLoop PollLoop { get; init; }

    /// <summary>
    /// v1 桥核心参数 — 对齐 TS 端 BridgeCoreParams
    /// </summary>
    public required BridgeCoreParams Parameters { get; init; }
}
