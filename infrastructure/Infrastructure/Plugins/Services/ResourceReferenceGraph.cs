namespace Core.Plugins;

/// <summary>
/// 资源引用图实现 — 维护跨插件资源引用关系
/// <para>线程安全:ConcurrentDictionary 存储引用关系</para>
/// <para>连带卸载:GetConsumers 返回引用方插件列表,框架通知放弃引用</para>
/// </summary>
[Register(typeof(IResourceReferenceGraph), ServiceLifetime.Singleton)]
public sealed class ResourceReferenceGraph : IResourceReferenceGraph
{
    private readonly ConcurrentDictionary<(ObjectId Consumer, ObjectId Target), ResourceReference> _references = new();
    private readonly ConcurrentDictionary<string, List<ResourceReference>> _byConsumer = new();
    private readonly ConcurrentDictionary<string, List<ResourceReference>> _byTarget = new();
    private readonly object _lock = new();

    /// <summary>记录引用 — 插件B 引用 插件A 的资源</summary>
    public void AddReference(ResourceReference reference)
    {
        var key = (reference.ConsumerResourceId, reference.TargetResourceId);
        if (!_references.TryAdd(key, reference)) return;

        lock (_lock)
        {
            _byConsumer.AddOrUpdate(
                reference.ConsumerPluginName,
                [reference],
                (_, list) => { list.Add(reference); return list; });
            _byTarget.AddOrUpdate(
                reference.TargetPluginName,
                [reference],
                (_, list) => { list.Add(reference); return list; });
        }
    }

    /// <summary>移除引用 — 引用方放弃引用</summary>
    public void RemoveReference(ObjectId consumerResourceId, ObjectId targetResourceId)
    {
        var key = (consumerResourceId, targetResourceId);
        if (!_references.TryRemove(key, out var reference)) return;

        lock (_lock)
        {
            if (_byConsumer.TryGetValue(reference.ConsumerPluginName, out var consumerList))
                consumerList.Remove(reference);
            if (_byTarget.TryGetValue(reference.TargetPluginName, out var targetList))
                targetList.Remove(reference);
        }
    }

    /// <summary>获取引用某插件资源的所有引用方插件名 — 用于连带卸载</summary>
    public IReadOnlyList<string> GetConsumers(string targetPluginName)
    {
        if (!_byTarget.TryGetValue(targetPluginName, out var list)) return [];
        lock (_lock)
        {
            return list.Select(r => r.ConsumerPluginName).Distinct().ToList();
        }
    }

    /// <summary>获取某插件引用的所有外部资源 — 用于释放引用</summary>
    public IReadOnlyList<ResourceReference> GetReferencesBy(string consumerPluginName)
    {
        if (!_byConsumer.TryGetValue(consumerPluginName, out var list)) return [];
        lock (_lock)
        {
            return list.ToList();
        }
    }

    /// <summary>获取某插件所有资源的引用计数 — 用于卸载前检查是否归零</summary>
    public IReadOnlyDictionary<ObjectId, int> GetReferenceCounts(string pluginName)
    {
        if (!_byTarget.TryGetValue(pluginName, out var list)) return new Dictionary<ObjectId, int>();
        lock (_lock)
        {
            return list.GroupBy(r => r.TargetResourceId)
                       .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    /// <summary>移除某插件的所有引用关系 — 卸载完成后清理</summary>
    public void RemoveAllForPlugin(string pluginName)
    {
        lock (_lock)
        {
            if (_byConsumer.TryRemove(pluginName, out var consumerList))
            {
                foreach (var r in consumerList)
                    _references.TryRemove((r.ConsumerResourceId, r.TargetResourceId), out _);
            }
            if (_byTarget.TryRemove(pluginName, out var targetList))
            {
                foreach (var r in targetList)
                    _references.TryRemove((r.ConsumerResourceId, r.TargetResourceId), out _);
            }
        }
    }
}
