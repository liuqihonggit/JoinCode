namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 能力节点 — 一个节点 = 引擎的一项可选业务能力（待办/任务/定时/目标/记忆目录/Dream/代码索引）。
/// <para>
/// TASK100 S4：这些能力此前只体现为「<c>EngineServiceContext</c> 里有个属性、某个插件被 new 了一次」，
/// 既无法按环境裁剪（headless 也想关 todo/dream），也不在 <c>--dump-nodes</c> 视野内。
/// </para>
/// <para>
/// capability 契约的语义是「存在即提供」：节点被 profile 声明 → 对应服务会被解析、对应插件会被加载；
/// 没声明 → 该属性为 <see langword="null"/>、插件不加载。与服务型的 <c>provider</c> 不同，
/// 能力之间不互斥，可以任意组合。
/// </para>
/// </summary>
public interface ICapabilityNode : INode {
    /// <summary>能力摘要（用于诊断与文档，不参与装配）。</summary>
    string Summary { get; }
}

/// <summary>
/// 能力节点清单 — 节点 ID 与 profile 消费点 <c>capabilities</c> 的唯一命名来源。
/// <para>
/// 只登记「真有消费点」的能力：没有消费方的能力是死声明，宁可先不做（vision 就是这种：
/// 当前没有 <c>IVisionService</c> 之类的可选服务被引擎上下文消费，登记它只会造一条假节点）。
/// </para>
/// </summary>
public static class CapabilityNodes {
    /// <summary>能力在 profile 中的消费点 ID（contract: capability）。</summary>
    public const string ConsumerId = "capabilities";

    /// <summary>待办能力节点 ID。</summary>
    public const string Todo = "todo";

    /// <summary>任务能力节点 ID。</summary>
    public const string Task = "task";

    /// <summary>定时任务能力节点 ID。</summary>
    public const string Cron = "cron";

    /// <summary>目标引擎能力节点 ID。</summary>
    public const string Goal = "goal";

    /// <summary>记忆目录能力节点 ID。</summary>
    public const string Memdir = "memdir";

    /// <summary>Dream 后台整理能力节点 ID。</summary>
    public const string Dream = "dream";

    /// <summary>代码索引能力节点 ID。</summary>
    public const string CodeIndex = "code-index";

    /// <summary>全部能力节点 ID（声明顺序无关，仅用于诊断与遍历）。</summary>
    public static IReadOnlyList<string> AllNodeIds { get; } = [Todo, Task, Cron, Goal, Memdir, Dream, CodeIndex];

    /// <summary>全部能力节点实例。</summary>
    public static IReadOnlyList<ICapabilityNode> All { get; } = [
        new TodoCapabilityNode(),
        new TaskCapabilityNode(),
        new CronCapabilityNode(),
        new GoalCapabilityNode(),
        new MemdirCapabilityNode(),
        new DreamCapabilityNode(),
        new CodeIndexCapabilityNode()
    ];

    /// <summary>
    /// 判断某能力在当前 profile 是否启用（语义见 <see cref="ConsumerDeclaration"/>）。
    /// </summary>
    /// <param name="profile">已解析的 profile；<see langword="null"/> 时视为全开。</param>
    /// <param name="capabilityId">能力节点 ID。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public static bool IsEnabled(ResolvedProfile? profile, string capabilityId) =>
        ConsumerDeclaration.IsEnabled(profile, ConsumerId, capabilityId);
}

/// <summary>
/// 运行期能力视图 — 消费方（引擎上下文、插件装配）只拿这一个入口判断启停。
/// <para>
/// 每次判断都读 <see cref="IProfileManager.Current"/>：profile 支持运行期切换（DSG049），
/// 在这里快照会把切换变成「下次启动才生效」。没有 <see cref="IProfileManager"/> 的轻量宿主
/// （如若干诊断命令）走 <see cref="AllEnabled"/>，保持历史行为不变。
/// </para>
/// </summary>
[Register(typeof(CapabilitySet), ServiceLifetime.Singleton)]
public sealed class CapabilitySet {
    private readonly IProfileManager? _profiles;

    /// <summary>
    /// 构造能力视图。
    /// </summary>
    /// <param name="profiles">Profile 管理器；为 <see langword="null"/> 时视为全开。</param>
    public CapabilitySet(IProfileManager? profiles = null) => _profiles = profiles;

    /// <summary>全开视图 — 没有任何 profile 信息时的历史行为。</summary>
    public static CapabilitySet AllEnabled { get; } = new();

    /// <summary>
    /// 判断某能力是否启用。
    /// </summary>
    /// <param name="capabilityId">能力节点 ID（用 <see cref="CapabilityNodes"/> 的常量）。</param>
    /// <returns>启用返回 <see langword="true"/>。</returns>
    public bool IsEnabled(string capabilityId) => CapabilityNodes.IsEnabled(_profiles?.Current, capabilityId);

    /// <summary>
    /// 生成一行诊断文本（<c>[CAPS] todo=on dream=off</c>）——「配置到底生效了没」不该靠猜。
    /// </summary>
    /// <returns>诊断文本。</returns>
    public string Describe() => string.Join(' ', CapabilityNodes.AllNodeIds.Select(id => $"{id}={(IsEnabled(id) ? "on" : "off")}"));
}

/// <summary>待办能力节点。</summary>
[Register(typeof(TodoCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class TodoCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Todo;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "待办事项增删改查与进度跟踪（ITodoService）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>任务能力节点。</summary>
[Register(typeof(TaskCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class TaskCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Task;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "任务创建、查询与状态流转（ITaskService）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>定时任务能力节点。</summary>
[Register(typeof(CronCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class CronCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Cron;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "cron 定时任务存储与 CronGoalBridge（ICronTaskStore）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>目标引擎能力节点。</summary>
[Register(typeof(GoalCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class GoalCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Goal;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "目标分解与执行调度（IGoalEngine / IGoalRegistry）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>记忆目录能力节点。</summary>
[Register(typeof(MemdirCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class MemdirCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Memdir;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "记忆目录与上下文压缩（IMemoryManagementService）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>Dream 后台整理能力节点。</summary>
[Register(typeof(DreamCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class DreamCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.Dream;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "Dream 后台整理任务（DreamPlugin / IDreamTaskRegistry）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}

/// <summary>代码索引能力节点。</summary>
[Register(typeof(CodeIndexCapabilityNode), ServiceLifetime.Singleton)]
[Register(typeof(ICapabilityNode), ServiceLifetime.Singleton)]
[NodeFactory(CapabilityNodes.ConsumerId, Id)]
public sealed class CodeIndexCapabilityNode : ICapabilityNode {
    /// <summary>节点 ID。</summary>
    public const string Id = CapabilityNodes.CodeIndex;

    /// <inheritdoc />
    public string NodeId => Id;

    /// <inheritdoc />
    public string Summary => "代码索引（CSharpLanguagePlugin / CodeIndexService）";

    /// <inheritdoc />
    public NodeMetadata Metadata => NodeMetadata.Unspecified;

    /// <inheritdoc />
    public NodeState State => NodeState.Active;
}
