namespace Core.Plugins;

/// <summary>
/// 插件资源扫描器 — 卸载完成后扫描检查资源是否正确注销
/// <para>事件触发:每次插件卸载完成后扫描一次,检查 ObjectId 是否全部注销</para>
/// <para>泄漏检测:如果 ObjectIdManager.IsRegistered 仍返回 true,表示资源未正确释放</para>
/// <para>资源 ObjectId 按 ObjectType 分组 + LongRangeSet 区间压缩存储(ADR 0117)</para>
/// </summary>
public sealed class PluginResourceScanner {
    private readonly ILogger<PluginResourceScanner>? _logger;

    /// <summary>创建资源扫描器</summary>
    public PluginResourceScanner(ILogger<PluginResourceScanner>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 扫描已卸载插件的资源是否全部注销
    /// <para>卸载完成后调用,检查所有资源的 ObjectId 是否已从 ObjectIdManager 注销</para>
    /// <para>遍历 ObjectType 分组的 LongRangeSet 区间,构造 lookup ObjectId 反查</para>
    /// </summary>
    public ResourceScanReport ScanPluginResources(string pluginName, IReadOnlyDictionary<ObjectType, LongRangeSet> resourceIds) {
        ArgumentNullException.ThrowIfNull(pluginName);
        ArgumentNullException.ThrowIfNull(resourceIds);

        var leaked = new List<ObjectId>();
        foreach (var (objType, ranges) in resourceIds) {
            if (ranges.IsEmpty) continue;
            foreach (var seq in ranges.Enumerate()) {
                var id = new ObjectId(objType, seq);
                if (ObjectIdManager.IsRegistered(id))
                    leaked.Add(id);
            }
        }

        var report = new ResourceScanReport(pluginName, leaked);
        if (report.HasLeaks) {
            _logger?.LogWarning(
                "插件 {Plugin} 有 {Count} 个资源未注销: {LeakedIds}",
                pluginName, leaked.Count, string.Join(", ", leaked));
        } else {
            _logger?.LogDebug("插件 {Plugin} 所有资源已正确注销", pluginName);
        }

        return report;
    }
}
