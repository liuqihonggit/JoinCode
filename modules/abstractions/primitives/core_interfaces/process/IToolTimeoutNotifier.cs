namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工具超时通知信息 — 传递给 IToolTimeoutNotifier（ADR 0153）
/// </summary>
/// <param name="ToolName">工具唯一描述名（如 "bash:dotnet build"）</param>
/// <param name="ProcessId">子进程 ID</param>
/// <param name="AgentId">启动此工具的子代理 ID（null=主代理）</param>
/// <param name="Elapsed">已运行时长</param>
/// <param name="WarningThreshold">警报阈值</param>
/// <param name="KillThreshold">强制杀死阈值</param>
public sealed record ToolTimeoutInfo(
    string ToolName,
    int ProcessId,
    string? AgentId,
    TimeSpan Elapsed,
    TimeSpan WarningThreshold,
    TimeSpan KillThreshold);

/// <summary>
/// 工具超时通知器 — 工具到达 WarningThreshold 时唤醒宿主 AI 决策，到达 KillThreshold 时强制杀死（ADR 0153）
/// <para>
/// 由 ToolTimeoutNotifier 实现（Composition），注入 ISteerInputMailbox 轮内注入提示词 + 中断当前步。
/// SessionToolMap.StartMonitoring 调用此接口，替代旧的 Func 委托回调。
/// </para>
/// </summary>
public interface IToolTimeoutNotifier {
    /// <summary>
    /// 工具到达 WarningThreshold 时唤醒宿主 AI — 注入 steer 提示 + 中断当前步（ADR 0153 唤醒策略）
    /// </summary>
    /// <param name="info">工具超时信息</param>
    /// <param name="ct">取消令牌</param>
    Task NotifyWarningAsync(ToolTimeoutInfo info, CancellationToken ct = default);

    /// <summary>
    /// 工具到达 KillThreshold 时强制杀死 — 通知宿主 AI 已杀死（KillByAgent 已处理进程杀死，此方法为通知补充）
    /// </summary>
    /// <param name="info">工具超时信息</param>
    /// <param name="ct">取消令牌</param>
    Task NotifyKillAsync(ToolTimeoutInfo info, CancellationToken ct = default);
}
