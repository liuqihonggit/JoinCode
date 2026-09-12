namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 资源扫描报告 — 卸载后扫描检查资源是否正确注销
/// </summary>
public sealed record ResourceScanReport(
    string PluginName,
    IReadOnlyList<ObjectId> LeakedResourceIds)
{
    /// <summary>是否有资源泄漏(未注销的 ObjectId)</summary>
    public bool HasLeaks => LeakedResourceIds.Count > 0;
}
