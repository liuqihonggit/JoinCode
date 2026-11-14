namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 节点服务解析器 — 将 (consumerId, nodeId) 组合映射到 DI 容器中的服务实例
/// 同一节点ID在不同消费点中可能对应不同的泛型实例（如 CrashSnapshotMiddleware&lt;TContext&gt;）
/// 因此使用 consumerId + nodeId 组合 key 确保正确解析
/// </summary>
public sealed class NodeServiceResolver {
    private readonly FrozenDictionary<string, Func<IServiceProvider, object>> _factories;

    private NodeServiceResolver(FrozenDictionary<string, Func<IServiceProvider, object>> factories) {
        _factories = factories;
    }

    /// <summary>从工厂映射表构建解析器</summary>
    public static NodeServiceResolver Build(IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> factories) {
        return new NodeServiceResolver(factories.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>按 consumerId + nodeId 尝试解析服务实例</summary>
    public bool TryResolve(string consumerId, string nodeId, IServiceProvider sp, [NotNullWhen(true)] out object? service) {
        var key = CombineKey(consumerId, nodeId);
        if (_factories.TryGetValue(key, out var factory)) {
            service = factory(sp);
            return true;
        }
        service = null;
        return false;
    }

    /// <summary>按 consumerId + nodeId 尝试解析服务实例（类型安全版本）</summary>
    public bool TryResolve<T>(string consumerId, string nodeId, IServiceProvider sp, [NotNullWhen(true)] out T? service) where T : class {
        if (TryResolve(consumerId, nodeId, sp, out var raw) && raw is T typed) {
            service = typed;
            return true;
        }
        service = null;
        return false;
    }

    /// <summary>组合 key 格式：consumerId + ":" + nodeId</summary>
    public static string CombineKey(string consumerId, string nodeId) {
        if (consumerId.Contains(':') || nodeId.Contains(':'))
            throw new ArgumentException($"consumerId and nodeId must not contain ':' — got consumerId='{consumerId}', nodeId='{nodeId}'");
        return $"{consumerId}:{nodeId}";
    }
}
