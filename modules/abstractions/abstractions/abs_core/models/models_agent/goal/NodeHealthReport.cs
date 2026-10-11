namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 节点健康检查报告
/// </summary>
public sealed class NodeHealthReport {
    /// <summary>获取告警列表。</summary>
    public IReadOnlyList<NodeHealthAlert> Alerts { get; init; } = [];

    /// <summary>获取是否存在告警。</summary>
    public bool HasAlerts => Alerts.Count > 0;

    /// <summary>创建健康报告。</summary>
    public static NodeHealthReport Healthy() => new();
    /// <summary>创建带告警的健康报告。</summary>
    public static NodeHealthReport WithAlerts(IReadOnlyList<NodeHealthAlert> alerts) => new() { Alerts = alerts };
}

/// <summary>
/// 节点健康告警
/// </summary>
public sealed class NodeHealthAlert {
    /// <summary>获取节点 ID。</summary>
    public required string NodeId { get; init; }
    /// <summary>获取告警种类。</summary>
    public required NodeAlertKind Kind { get; init; }
    /// <summary>获取告警消息。</summary>
    public required string Message { get; init; }
}
