namespace Core.Context;

/// <summary>
/// 空闲检测器接口 — 工具空闲检测、提醒注入
/// </summary>
public interface IChatIdleDetector
{
    /// <summary>
    /// 记录助手轮次
    /// </summary>
    void RecordAssistantTurn(string? toolNameUsed);

    /// <summary>
    /// 处理空闲检测
    /// </summary>
    Task HandleIdleDetectionAsync(CancellationToken ct);

    /// <summary>
    /// 重置状态
    /// </summary>
    void Reset();
}
