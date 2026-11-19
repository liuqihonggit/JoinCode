namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 节点注册表 — 运行期统一管理所有已注册节点。
/// G5: 节点从编译期工厂 catalog 自动归纳；G1: 支持可逆注册（handle + 逆序回滚）。
/// </summary>
public sealed class NodeRegistry : INodeRegistry {
    /// <summary>一次注册的不可变记录：token 唯一标识句柄，factory 可就地替换。</summary>
    private sealed record Registration(long Token, Func<IServiceProvider, NodeMetadata?, INode> Factory);

    /// <summary>
    /// 单一原子状态：注册表与快照一起发布，杜绝双字段写乱序导致"已 Dispose 节点复活"。
    /// copy-on-write + CAS，读路径无锁（不用 lock 搅拌，符合项目并发约定）。
    /// </summary>
    private sealed record RegistryState(
        Dictionary<string, Registration[]> Registrations,
        FrozenDictionary<string, NodeEntry> Nodes);

    private RegistryState _state = new(
        new Dictionary<string, Registration[]>(StringComparer.Ordinal),
        FrozenDictionary<string, NodeEntry>.Empty);
    private long _nextToken;

    private readonly IServiceProvider _services;
    private readonly IFeatureFlagService? _featureFlagService;
    private readonly ILogger<NodeRegistry>? _logger;

    private NodeRegistry(IServiceProvider services, IFeatureFlagService? featureFlagService, ILogger<NodeRegistry>? logger) {
        _services = services;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    private static bool DebugLog => Environment.GetEnvironmentVariable("JCC_DEBUGLOG") == "1";

    /// <summary>已归纳的节点数（诊断用）</summary>
    public int NodeCount => Volatile.Read(ref _state).Nodes.Count;

    /// <summary>
    /// 从 DI 容器构建节点注册表。若容器中存在 <see cref="NodeServiceResolver"/>，
    /// 其编译期工厂 catalog 会被自动归纳为节点（G5）——新增服务不再需要手工登记工厂。
    /// </summary>
    public static NodeRegistry Build(IServiceProvider sp, IEnumerable<KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>>? customFactories = null) {
        var resolver = sp.GetService<NodeServiceResolver>();
        return resolver is null
            ? BuildCore(sp, [], customFactories)
            : BuildFromCatalog(sp, resolver.Factories, customFactories);
    }

    /// <summary>
    /// 从编译期工厂 catalog 自动归纳节点（G5）。catalog key 为 <c>consumerId:nodeId</c>，
    /// **完整 key 就是注册键** —— 同一 nodeId 在不同消费点可能是不同泛型实例
    /// （如 crash-snapshot-mw 的 16 个 Closed Generic），按裸 nodeId 去重会错丢服务。
    /// 已是 <see cref="INode"/> 的服务直接复用，其余包装为 <see cref="Node{TService}"/>，
    /// 让 G4 的 metadata 注入链作用于运行期节点。
    /// </summary>
    public static NodeRegistry BuildFromCatalog(
        IServiceProvider sp,
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> catalog,
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>>? customFactories = null) {
        var items = catalog.ToArray();   // 只枚举一次（调用方可能传 yield return 迭代器）
        var factories = new List<KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>>();
        foreach (var (key, rawFactory) in items) {
            if (string.IsNullOrWhiteSpace(key)) continue;
            factories.Add(new(key, (provider, metadata) => {
                var service = rawFactory(provider);
                return service is INode typed ? typed : new Node<object>(key, service, metadata);
            }));
        }
        if (DebugLog)
            Console.Error.WriteLine($"[DI-SCAN] NodeRegistry 从工厂 catalog 归纳 {factories.Count} 个节点（无效跳过 {items.Length - factories.Count}）");
        return BuildCore(sp, factories, customFactories);
    }

    private static NodeRegistry BuildCore(
        IServiceProvider sp,
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>> factories,
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>>? customFactories) {
        var registry = new NodeRegistry(
            sp,
            sp.GetService<IFeatureFlagService>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger<NodeRegistry>());
        // 构建期一次性批量写入并只发布一次快照（避免逐条 Register 造成的 O(n²) 重建）。
        registry.Mutate(regs => {
            foreach (var (nodeId, factory) in factories)
                Push(regs, nodeId, Interlocked.Increment(ref registry._nextToken), factory);
            // 显式自定义工厂作为栈顶覆写层（后写者胜）——与 G2 节点继承合并语义一致。
            if (customFactories is not null)
                foreach (var (nodeId, factory) in customFactories)
                    Push(regs, nodeId, Interlocked.Increment(ref registry._nextToken), factory);
            return regs;
        });
        return registry;
    }

    /// <summary>
    /// 运行期注册单个节点，返回可回滚句柄（G1，对齐 DSH ctx.effect()）。
    /// 同一 ID 重复注册保持栈语义：后注册的先 Dispose 会恢复前一个工厂。
    /// </summary>
    /// <param name="nodeId">节点 ID（全局唯一节点用裸 ID；消费点专属节点用 consumerId:nodeId）。</param>
    /// <param name="factory">节点工厂。</param>
    /// <returns>回滚/替换句柄。</returns>
    public RegistrationHandle Register(string nodeId, Func<IServiceProvider, NodeMetadata?, INode> factory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(factory);
        var token = Interlocked.Increment(ref _nextToken);
        Mutate(regs => {
            Push(regs, nodeId, token, factory);
            return regs;
        });
        if (DebugLog) Console.Error.WriteLine($"[REGISTER] {nodeId} token={token} count={NodeCount}");
        return new RegistrationHandle(
            () => Unregister(nodeId, token),
            newFactory => Replace(nodeId, token, newFactory));
    }

    private static void Push(
        Dictionary<string, Registration[]> regs,
        string nodeId,
        long token,
        Func<IServiceProvider, NodeMetadata?, INode> factory) {
        var stack = regs.TryGetValue(nodeId, out var existing) ? existing : [];
        regs[nodeId] = [.. stack, new Registration(token, factory)];
    }

    /// <summary>
    /// 单次 CAS 发布整个状态（注册表 + 快照），杜绝双字段写乱序。
    /// mutate 拿到的字典已是当前状态的副本，可安全修改；CAS 失败则重试。
    /// </summary>
    private void Mutate(Func<Dictionary<string, Registration[]>, Dictionary<string, Registration[]>> mutate) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var registrations = mutate(new Dictionary<string, Registration[]>(current.Registrations, StringComparer.Ordinal));
            var snapshot = new Dictionary<string, NodeEntry>(StringComparer.Ordinal);
            foreach (var (nodeId, stack) in registrations) {
                if (stack.Length > 0) snapshot[nodeId] = new NodeEntry(nodeId, stack[^1].Factory);
            }
            var next = new RegistryState(registrations, snapshot.ToFrozenDictionary(StringComparer.Ordinal));
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current)) return;
        }
    }

    private void Unregister(string nodeId, long token) {
        Mutate(regs => {
            if (!regs.TryGetValue(nodeId, out var stack)) return regs;
            var remaining = stack.Where(registration => registration.Token != token).ToArray();
            if (remaining.Length == 0) regs.Remove(nodeId);
            else regs[nodeId] = remaining;
            return regs;
        });
        if (DebugLog) Console.Error.WriteLine($"[DISPOSE] {nodeId} token={token} count={NodeCount}");
    }

    private void Replace(string nodeId, long token, Func<IServiceProvider, NodeMetadata?, INode> factory) {
        var found = false;
        Mutate(regs => {
            if (!regs.TryGetValue(nodeId, out var stack)) return regs;
            found = stack.Any(registration => registration.Token == token);
            if (!found) return regs;
            regs[nodeId] = stack
                .Select(registration => registration.Token == token ? registration with { Factory = factory } : registration)
                .ToArray();
            return regs;
        });
        if (!found)
            throw new InvalidOperationException(
                $"节点 '{nodeId}' 的注册句柄已失效，无法 Replace（由 NodeRegistry.Replace 拦截）。" +
                "原因: 该 token 已被 Dispose 或注册表已重建。正确写法: 重新 Register 后再 Replace。");
        if (DebugLog) Console.Error.WriteLine($"[REPLACE] {nodeId} token={token} count={NodeCount}");
    }

    /// <summary>按节点 ID 尝试获取单个节点（全局唯一节点用裸 ID；消费点专属请用消费点方法）。</summary>
    public bool TryGetNode(string nodeId, [NotNullWhen(true)] out INode? node) {
        if (Volatile.Read(ref _state).Nodes.TryGetValue(nodeId, out var entry)) {
            node = entry.CreateNode(_services);
            return true;
        }
        node = null;
        return false;
    }

    /// <summary>消费点内节点键：先查完整 consumerId:nodeId，再回退裸 nodeId（兼容手工注册）。</summary>
    private string LookupKey(string consumerId, string nodeId) {
        var nodes = Volatile.Read(ref _state).Nodes;
        var combined = NodeServiceResolver.CombineKey(consumerId, nodeId);
        if (nodes.ContainsKey(combined) || !nodes.ContainsKey(nodeId)) return combined;
        return nodeId;
    }

    /// <summary>获取指定消费点的所有节点（按 Profile 配置和 Priority 排序，检查 enabled-when feature flag）</summary>
    public async Task<IReadOnlyList<INode>> GetNodesForConsumerAsync(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return [];
        var result = new List<INode>();
        var nodes = Volatile.Read(ref _state).Nodes;
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (nodeEntry.EnabledWhenFeatureFlag is not null) {
                if (_featureFlagService is null) continue;
                var isEnabled = await _featureFlagService.IsEnabledAsync(
                    nodeEntry.EnabledWhenFeatureFlag, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!isEnabled) continue;
            }
            var key = LookupKey(consumerId, nodeEntry.Id);
            if (nodes.TryGetValue(key, out var entry)) {
                // G4 真实注入链：metadata 来自该 Profile 节点的 meta.* 保留键，
                // 路由字段以 ResolvedNodeEntry 为权威值补齐（避免同一节点出现两个 priority）。
                var declared = NodeMetadata.FromConfig(nodeEntry.Config);
                var metadata = (declared ?? NodeMetadata.Unspecified) with {
                    Priority = nodeEntry.Priority,
                    EnabledWhenFeatureFlag = nodeEntry.EnabledWhenFeatureFlag
                };
                result.Add(entry.CreateNode(_services, metadata));
            } else {
                _logger?.LogWarning("Node '{NodeId}' required by consumer '{ConsumerId}' is not registered in NodeRegistry", nodeEntry.Id, consumerId);
            }
        }
        return result;
    }

    /// <summary>获取指定消费点的所有服务实例（类型安全版本，检查 enabled-when feature flag）</summary>
    /// <param name="consumerId">消费点 ID。</param>
    /// <param name="profile">已解析的 profile。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="failFastOnMissing">
    /// 解析不到实例时是否抛错（TASK100 S10 / §25.7）。默认 false = 记 Warning 并跳过；
    /// 实例型消费点应传 true —— 那里的节点缺失等于「整批功能静默消失」。
    /// </param>
    public async Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(
        string consumerId,
        ResolvedProfile profile,
        CancellationToken cancellationToken = default,
        bool failFastOnMissing = false) where T : class {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return [];
        var resolver = _services.GetService<NodeServiceResolver>();
        var result = new List<T>();
        var nodes = Volatile.Read(ref _state).Nodes;
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (nodeEntry.EnabledWhenFeatureFlag is not null) {
                if (_featureFlagService is null) continue;
                var isEnabled = await _featureFlagService.IsEnabledAsync(
                    nodeEntry.EnabledWhenFeatureFlag, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!isEnabled) continue;
            }
            if (resolver is not null && resolver.TryResolve<T>(consumerId, nodeEntry.Id, _services, out var service)) {
                result.Add(service);
            } else if (nodes.TryGetValue(LookupKey(consumerId, nodeEntry.Id), out var entry) && entry.CreateNode(_services) is Node<T> typedNode) {
                result.Add(typedNode.Service);
            } else {
                // TASK100 S10（§25.7）：过去这里只记 Warning 就把节点丢掉 ——
                // 在 command-dispatch / prompt-assembly 这类消费点上，丢一个节点 =
                // 斜杠命令整批消失 / 提示词整组段落不见，现象与原因相隔很远。
                if (failFastOnMissing) {
                    throw new InvalidOperationException(
                        $"节点装配失败: consumer='{consumerId}', node='{nodeEntry.Id}', " +
                        $"期望类型='{typeof(T).Name}' — 既没有匹配的工厂，节点本身也不携带实例。" +
                        "该节点已被 profile 声明却无法实例化；宁可启动失败，也不要静默少一块功能。");
                }
                _logger?.LogWarning("Node '{NodeId}' in consumer '{ConsumerId}' could not be resolved for service type '{ServiceType}'", nodeEntry.Id, consumerId, typeof(T).Name);
            }
        }
        return result;
    }
}
