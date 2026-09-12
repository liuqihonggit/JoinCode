namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 节点健康告警类型
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 NodeAlertKindConstants + NodeAlertKindExtensions
/// </summary>
public enum NodeAlertKind
{
    [EnumValue("node_timeout")] NodeTimeout,
    [EnumValue("dead_loop")] DeadLoop,
    [EnumValue("file_conflict")] FileConflict,
}
