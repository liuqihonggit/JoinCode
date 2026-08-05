namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 节点健康检查报告
/// </summary>
public sealed class NodeHealthReport
{
    public IReadOnlyList<NodeHealthAlert> Alerts { get; init; } = [];

    public bool HasAlerts => Alerts.Count > 0;

    public static NodeHealthReport Healthy() => new();
    public static NodeHealthReport WithAlerts(IReadOnlyList<NodeHealthAlert> alerts) => new() { Alerts = alerts };
}

/// <summary>
/// 节点健康告警
/// </summary>
public sealed class NodeHealthAlert
{
    public required string NodeId { get; init; }
    public required NodeAlertKind Kind { get; init; }
    public required string Message { get; init; }
}
