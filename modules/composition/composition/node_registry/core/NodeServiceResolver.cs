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

    /// <summary>
    /// 构建解析器：<paramref name="overrides"/> 优先，<paramref name="fallback"/> 只补缺。
    /// <para>
    /// TASK100 S8（§24.2 / §24.3）：运行期过去只吃「手写清单」，而 <c>--dump-nodes</c> 与门禁吃
    /// 「编译期 catalog」—— 两份真相。新增 <c>[NodeFactory]</c> 却忘了改手写清单时，
    /// 门禁全绿而运行期静默跳过该中间件（安全域表现为「少一道守卫」）。
    /// </para>
    /// <para>
    /// 合并规则必须是「手写优先」而不是「catalog 替换」：22 条参数化工厂
    /// （<c>CreateCrashSnapshot</c> / <c>CreateRateLimit</c> / <c>CreateRetry</c> …）在 catalog 侧
    /// 只会生成 <c>GetRequiredService&lt;T&gt;()</c>，会丢掉 consumer 名与限流/重试参数（§24.3）。
    /// 覆盖层保持原行为，catalog 只负责兜底新增节点。
    /// </para>
    /// </summary>
    /// <param name="overrides">优先来源（手写 / 参数化工厂）。</param>
    /// <param name="fallback">兜底来源（编译期 catalog）。</param>
    public static NodeServiceResolver BuildWithFallback(
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> overrides,
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> fallback) {
        var merged = new Dictionary<string, Func<IServiceProvider, object>>(StringComparer.Ordinal);
        foreach (var kv in overrides) merged[kv.Key] = kv.Value;
        foreach (var kv in fallback) {
            if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
        }
        return new NodeServiceResolver(merged.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// 编译期确定的工厂 catalog（G5 节点自动归纳的唯一数据源，只读暴露）。
    /// 与 <c>NodeRegistry.BuildFromCatalog</c> 共用同一份，不另生成第二套节点清单。
    /// </summary>
    public IReadOnlyDictionary<string, Func<IServiceProvider, object>> Factories => _factories;

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
