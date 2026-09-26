namespace Core.Plugins;

/// <summary>
/// 资源引用图实现 — 维护跨插件资源引用关系
/// <para>线程安全:ConcurrentDictionary 存储引用关系</para>
/// <para>连带卸载:GetConsumers 返回引用方插件列表,框架通知放弃引用</para>
/// </summary>
[Register(typeof(IResourceReferenceGraph), ServiceLifetime.Singleton)]
public sealed class ResourceReferenceGraph : IResourceReferenceGraph {
    private ImmutableDictionary<(ObjectId Consumer, ObjectId Target), ResourceReference> _references = ImmutableDictionary<(ObjectId, ObjectId), ResourceReference>.Empty;
    private ImmutableDictionary<string, ImmutableList<ResourceReference>> _byConsumer = ImmutableDictionary<string, ImmutableList<ResourceReference>>.Empty;
    private ImmutableDictionary<string, ImmutableList<ResourceReference>> _byTarget = ImmutableDictionary<string, ImmutableList<ResourceReference>>.Empty;

    /// <summary>记录引用 — 插件B 引用 插件A 的资源</summary>
    public void AddReference(ResourceReference reference) {
        var key = (reference.ConsumerResourceId, reference.TargetResourceId);
        var added = false;
        ImmutableInterlocked.Update(ref _references, d => {
            if (d.ContainsKey(key)) return d;
            added = true;
            return d.Add(key, reference);
        });
        if (!added) return;

        ImmutableInterlocked.Update(ref _byConsumer,
            d => d.SetItem(reference.ConsumerPluginName, d.GetValueOrDefault(reference.ConsumerPluginName, ImmutableList<ResourceReference>.Empty).Add(reference)));
        ImmutableInterlocked.Update(ref _byTarget,
            d => d.SetItem(reference.TargetPluginName, d.GetValueOrDefault(reference.TargetPluginName, ImmutableList<ResourceReference>.Empty).Add(reference)));
    }

    /// <summary>移除引用 — 引用方放弃引用</summary>
    public void RemoveReference(ObjectId consumerResourceId, ObjectId targetResourceId) {
        var key = (consumerResourceId, targetResourceId);
        ResourceReference? removed = null;
        ImmutableInterlocked.Update(ref _references, d => {
            if (d.TryGetValue(key, out var r)) {
                removed = r;
                return d.Remove(key);
            }
            return d;
        });
        if (removed is not { } reference) return;

        ImmutableInterlocked.Update(ref _byConsumer,
            d => {
                var next = d.GetValueOrDefault(reference.ConsumerPluginName, ImmutableList<ResourceReference>.Empty).Remove(reference);
                return next.IsEmpty ? d.Remove(reference.ConsumerPluginName) : d.SetItem(reference.ConsumerPluginName, next);
            });
        ImmutableInterlocked.Update(ref _byTarget,
            d => {
                var next = d.GetValueOrDefault(reference.TargetPluginName, ImmutableList<ResourceReference>.Empty).Remove(reference);
                return next.IsEmpty ? d.Remove(reference.TargetPluginName) : d.SetItem(reference.TargetPluginName, next);
            });
    }

    /// <summary>获取引用某插件资源的所有引用方插件名 — 用于连带卸载</summary>
    public IReadOnlyList<string> GetConsumers(string targetPluginName) {
        if (!Volatile.Read(ref _byTarget).TryGetValue(targetPluginName, out var list)) return [];
        return list.Select(r => r.ConsumerPluginName).Distinct().ToList();
    }

    /// <summary>获取某插件引用的所有外部资源 — 用于释放引用</summary>
    public IReadOnlyList<ResourceReference> GetReferencesBy(string consumerPluginName) {
        if (!Volatile.Read(ref _byConsumer).TryGetValue(consumerPluginName, out var list)) return [];
        return list.ToList();
    }

    /// <summary>获取某插件所有资源的引用计数 — 用于卸载前检查是否归零</summary>
    public IReadOnlyDictionary<ObjectId, int> GetReferenceCounts(string pluginName) {
        if (!Volatile.Read(ref _byTarget).TryGetValue(pluginName, out var list)) return new Dictionary<ObjectId, int>();
        return list.GroupBy(r => r.TargetResourceId)
                   .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>移除某插件的所有引用关系 — 卸载完成后清理</summary>
    public void RemoveAllForPlugin(string pluginName) {
        var snapshot = Volatile.Read(ref _byConsumer);
        if (snapshot.TryGetValue(pluginName, out var consumerList)) {
            ImmutableInterlocked.Update(ref _byConsumer, d => d.Remove(pluginName));
            foreach (var r in consumerList)
                ImmutableInterlocked.Update(ref _references, d => d.Remove((r.ConsumerResourceId, r.TargetResourceId)));
        }
        var snapshot2 = Volatile.Read(ref _byTarget);
        if (snapshot2.TryGetValue(pluginName, out var targetList)) {
            ImmutableInterlocked.Update(ref _byTarget, d => d.Remove(pluginName));
            foreach (var r in targetList)
                ImmutableInterlocked.Update(ref _references, d => d.Remove((r.ConsumerResourceId, r.TargetResourceId)));
        }
    }
}