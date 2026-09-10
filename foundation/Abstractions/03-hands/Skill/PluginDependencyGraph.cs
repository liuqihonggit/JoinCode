namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件依赖图 — 动态拓扑解析(ADR 0098)
/// <para>记录"插件→依赖的服务类型"声明,卸载时通过 providerResolver 动态解析当前提供者</para>
/// <para>服务热替换(A 卸载后 C 重新 Provide)自动生效,无需快照</para>
/// <para>线程安全由 Actor 串行保证,内部无锁</para>
/// </summary>
public sealed class PluginDependencyGraph
{
    private readonly Dictionary<string, HashSet<Type>> _declarations = new();

    /// <summary>声明插件依赖某服务类型</summary>
    public void DeclareServiceDependency(string plugin, Type serviceType)
    {
        if (!_declarations.TryGetValue(plugin, out var set))
            _declarations[plugin] = set = new();
        set.Add(serviceType);
    }

    /// <summary>移除插件的所有声明 — 卸载后清理</summary>
    public void RemovePlugin(string id) => _declarations.Remove(id);

    /// <summary>
    /// 根据当前服务注册表动态解析拓扑卸载顺序
    /// </summary>
    /// <param name="root">起始卸载插件</param>
    /// <param name="providerResolver">服务类型 → 当前提供者插件 id 或 null</param>
    /// <returns>拓扑卸载顺序(依赖者先,被依赖者后)</returns>
    public IReadOnlyList<string> GetUnloadOrder(string root, Func<Type, string?> providerResolver)
    {
        var dependents = BuildDependentsMap(providerResolver);
        var order = new List<string>();
        var visited = new HashSet<string>();
        Visit(root);
        return order;

        void Visit(string n)
        {
            if (!visited.Add(n)) return;
            if (dependents.TryGetValue(n, out var ds))
                foreach (var d in ds) Visit(d);
            order.Add(n);
        }
    }

    /// <summary>描述当前依赖关系(用于诊断)</summary>
    public string Describe(Func<Type, string?> providerResolver)
    {
        var dependents = BuildDependentsMap(providerResolver);
        var lines = new List<string>();
        foreach (var (provider, ds) in dependents)
            foreach (var d in ds)
                lines.Add($"  {provider} 被 [{d}] 依赖");
        return lines.Count == 0 ? "  (空)" : string.Join(Environment.NewLine, lines);
    }

    private Dictionary<string, HashSet<string>> BuildDependentsMap(Func<Type, string?> providerResolver)
    {
        var dependents = new Dictionary<string, HashSet<string>>();
        foreach (var (plugin, types) in _declarations)
        {
            foreach (var t in types)
            {
                var provider = providerResolver(t);
                if (provider == null) continue;
                if (!dependents.TryGetValue(provider, out var ds))
                    dependents[provider] = ds = new();
                ds.Add(plugin);
            }
        }
        return dependents;
    }
}
