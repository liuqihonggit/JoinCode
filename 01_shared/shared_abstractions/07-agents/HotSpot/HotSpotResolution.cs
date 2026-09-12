namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 热点处置决策 — 给定热点文件，决定队长是否接管、通知哪些Worker
/// 不可变 record，由 IHotSpotResolutionPolicy 生成
/// </summary>
public sealed record HotSpotResolution
{
    /// <summary>
    /// 热点文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 队长是否接管该文件的契约修改
    /// </summary>
    public required bool ShouldCaptainTakeOver { get; init; }

    /// <summary>
    /// 需要通知的 Worker 列表（正在改该文件契约的Worker，需停止契约改、提交半成品）
    /// </summary>
    public required IReadOnlyList<string> WorkersToNotify { get; init; }

    /// <summary>
    /// 通知消息内容（发给Worker的"请停止+提交半成品+队长接管"消息）
    /// </summary>
    public required string NotificationMessage { get; init; }

    /// <summary>
    /// 是否需要处置（有Worker需通知或队长需接管）
    /// </summary>
    public bool RequiresAction => ShouldCaptainTakeOver || WorkersToNotify.Count > 0;
}
