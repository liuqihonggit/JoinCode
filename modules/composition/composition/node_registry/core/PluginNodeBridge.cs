namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 插件节点桥 — G1 可逆注册的真实生命周期落点（DSH ctx.effect 语义）。
/// <para>插件加载时向 <see cref="INodeRegistry"/> 注册一个代表该插件的节点并持有
/// <see cref="RegistrationHandle"/>；卸载时逆序 Dispose，让「装了什么就撤什么」由注册表自己兑现，
/// 而不是各插件自建一份平行的装卸表。</para>
/// <para>注册节点本身不预设消费点 —— 架构正确性优先：注册表是节点的唯一真相，
/// 消费点将来读哪类节点由 profile 决定，桥接不与任何具体消费点耦合。</para>
/// </summary>
[Register(typeof(PluginNodeBridge), ServiceLifetime.Singleton)]
public sealed class PluginNodeBridge {
    private readonly INodeRegistry _registry;
    private readonly ILogger<PluginNodeBridge>? _logger;
    // 不可变快照 + CAS（JCC4008：不用 lock）。同一插件重复加载按 LIFO 回滚，重载也不漏撤。
    private Dictionary<string, RegistrationHandle[]> _handles = new(StringComparer.Ordinal);

    /// <summary>构造桥接。</summary>
    /// <param name="registry">节点注册表（G1 Register 的唯一入口）。</param>
    /// <param name="logger">日志（可选）。</param>
    public PluginNodeBridge(INodeRegistry registry, ILogger<PluginNodeBridge>? logger = null) {
        _registry = registry;
        _logger = logger;
    }

    private static bool DebugLog => Environment.GetEnvironmentVariable("JCC_DEBUGLOG") == "1";

    /// <summary>插件加载：注册插件节点并保存回滚句柄。</summary>
    /// <param name="pluginName">插件名（事件参数）。</param>
    public void OnPluginLoaded(string pluginName) {
        if (string.IsNullOrWhiteSpace(pluginName)) return;
        var nodeId = "plugin:" + pluginName;
        var metadata = new NodeMetadata(
            Version: "plugin",
            Dependencies: [], Capabilities: [], RequiredCapabilities: [],
            Priority: 0, EnabledWhenFeatureFlag: null);
        // 内联注册 + 登记：句柄所有权直接流入 TrackHandle 的快照（JCC9305 不留局部 handle 变量）。
        TrackHandle(pluginName, _registry.Register(nodeId, (_, meta) => new PluginNode(nodeId, pluginName, meta ?? metadata)));
        if (DebugLog)
            Console.Error.WriteLine($"[NODE-BRIDGE] register plugin='{pluginName}' node='{nodeId}'");
    }

    /// <summary>按插件名登记句柄到不可变快照（句柄所有权由调用方转移进来，供卸载时 LIFO 回滚）。</summary>
    /// <param name="pluginName">插件名。</param>
    /// <param name="handle">注册句柄（所有权转入本对象的快照）。</param>
    private void TrackHandle(string pluginName, RegistrationHandle handle) {
        while (true) {
            var current = Volatile.Read(ref _handles);
            var next = new Dictionary<string, RegistrationHandle[]>(current, StringComparer.Ordinal) {
                [pluginName] = [.. current.GetValueOrDefault(pluginName) ?? [], handle]
            };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _handles, next, current), current)) return;
        }
    }

    /// <summary>插件卸载：LIFO 回滚该插件的全部注册句柄。</summary>
    /// <param name="pluginName">插件名（事件参数）。</param>
    public void OnPluginUnloading(string pluginName) {
        if (string.IsNullOrWhiteSpace(pluginName)) return;
        RegistrationHandle[] handles;
        while (true) {
            var current = Volatile.Read(ref _handles);
            if (!current.TryGetValue(pluginName, out var stack)) return;
            handles = [.. stack.AsEnumerable().Reverse()];   // LIFO：后注册先回滚（JCC9306 用 foreach 遍历）
            var next = new Dictionary<string, RegistrationHandle[]>(current, StringComparer.Ordinal);
            next.Remove(pluginName);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _handles, next, current), current)) break;
        }
        foreach (var handle in handles)
            handle.Dispose();
        if (DebugLog)
            Console.Error.WriteLine($"[NODE-BRIDGE] unregister plugin='{pluginName}' handles={handles.Length}");
    }

    /// <summary>当前在册的插件节点句柄总数（诊断用）。</summary>
    public int TrackedCount => Volatile.Read(ref _handles).Values.Sum(stack => stack.Length);

    /// <summary>插件节点的运行期表示 — INode 最小实现，仅暴露身份与元数据。</summary>
    private sealed class PluginNode : INode {
        /// <summary>节点 ID。</summary>
        public string NodeId { get; }
        /// <summary>节点元数据（Version=plugin，标识来自运行期注册）。</summary>
        public NodeMetadata Metadata { get; }
        /// <summary>生命周期状态。</summary>
        public NodeState State { get; } = NodeState.Active;
        /// <summary>来源插件名。</summary>
        public string PluginName { get; }

        /// <summary>构造插件节点。</summary>
        /// <param name="nodeId">节点 ID。</param>
        /// <param name="pluginName">插件名。</param>
        /// <param name="metadata">元数据。</param>
        internal PluginNode(string nodeId, string pluginName, NodeMetadata metadata) {
            NodeId = nodeId;
            PluginName = pluginName;
            Metadata = metadata;
        }
    }
}
