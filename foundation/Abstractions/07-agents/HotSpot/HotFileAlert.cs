namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 热文件告警 — Worker 私自改热文件未上报时生成
/// 仅兜底纠错，不增加认领计数
/// </summary>
public sealed record HotFileAlert
{
    /// <summary>
    /// 被改的热文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 修改者 ID
    /// </summary>
    public required string ChangerId { get; init; }

    /// <summary>
    /// 告警消息
    /// </summary>
    public required string AlertMessage { get; init; }

    /// <summary>
    /// 告警时间（UTC）
    /// </summary>
    public required DateTimeOffset AlertedAt { get; init; }
}
