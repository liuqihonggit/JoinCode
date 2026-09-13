namespace Core.Query.Transitions;

/// <summary>
/// 查询状态枚举 — 描述查询生命周期的各状态
/// </summary>
public enum QueryState
{
    /// <summary>
    /// 空闲
    /// </summary>
    [EnumValue("idle")] Idle,

    /// <summary>
    /// 初始化中
    /// </summary>
    [EnumValue("initializing")] Initializing,

    /// <summary>
    /// 运行中
    /// </summary>
    [EnumValue("running")] Running,

    /// <summary>
    /// 等待工具
    /// </summary>
    [EnumValue("waitingForTool")] WaitingForTool,

    /// <summary>
    /// 执行工具中
    /// </summary>
    [EnumValue("executingTool")] ExecutingTool,

    /// <summary>
    /// 压缩中
    /// </summary>
    [EnumValue("compacting")] Compacting,

    /// <summary>
    /// 停止中
    /// </summary>
    [EnumValue("stopping")] Stopping,

    /// <summary>
    /// 已完成
    /// </summary>
    [EnumValue("completed")] Completed,

    /// <summary>
    /// 已失败
    /// </summary>
    [EnumValue("failed")] Failed,

    /// <summary>
    /// 已取消
    /// </summary>
    [EnumValue("cancelled")] Cancelled
}

/// <summary>
/// 查询状态转换器接口 — 基于状态机管理查询状态流转
/// </summary>
public interface IQueryStateTransitions
{
    /// <summary>
    /// 当前状态
    /// </summary>
    QueryState CurrentState { get; }

    /// <summary>
    /// 检查是否可以从指定状态转换到目标状态
    /// </summary>
    /// <param name="from">起始状态</param>
    /// <param name="to">目标状态</param>
    /// <returns>允许转换返回 true，否则返回 false</returns>
    bool CanTransitionTo(QueryState from, QueryState to);

    /// <summary>
    /// 转换到目标状态 — 不允许时抛出异常
    /// </summary>
    /// <param name="target">目标状态</param>
    void TransitionTo(QueryState target);

    /// <summary>
    /// 重置到 Idle 状态
    /// </summary>
    void Reset();

    /// <summary>
    /// 状态变更事件
    /// </summary>
    event EventHandler<StateChangedEventArgs<QueryState>>? StateChanged;
}

/// <summary>
/// 查询状态转换器实现 — 基于状态机 + 转换表管理状态流转
/// </summary>
[Register(typeof(IQueryStateTransitions), ServiceLifetime.Singleton)]
public sealed partial class QueryStateTransitions : ServiceEntity, IQueryStateTransitions
{
    private static readonly FrozenDictionary<QueryState, FrozenSet<QueryState>> TransitionTable = CreateTransitionTable();

    private readonly StateMachine<QueryState> _stateMachine;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入遥测服务和时钟服务（均可选）
    /// </summary>
    /// <param name="telemetryService">遥测服务</param>
    /// <param name="clock">时钟服务</param>
    public QueryStateTransitions(ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _telemetryService = telemetryService;
        _stateMachine = new StateMachine<QueryState>(TransitionTable, QueryState.Idle, clock);
        _stateMachine.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 当前状态
    /// </summary>
    public QueryState CurrentState => _stateMachine.CurrentState;

    /// <summary>
    /// 状态变更事件
    /// </summary>
    public event EventHandler<StateChangedEventArgs<QueryState>>? StateChanged;

    /// <summary>
    /// 检查是否可以从指定状态转换到目标状态
    /// </summary>
    /// <param name="from">起始状态</param>
    /// <param name="to">目标状态</param>
    /// <returns>允许转换返回 true，否则返回 false</returns>
    public bool CanTransitionTo(QueryState from, QueryState to) => _stateMachine.CanTransitionTo(from, to);

    /// <summary>
    /// 转换到目标状态 — 不允许时抛出异常
    /// </summary>
    /// <param name="target">目标状态</param>
    public void TransitionTo(QueryState target) => _stateMachine.TransitionTo(target);

    /// <summary>
    /// 重置到 Idle 状态
    /// </summary>
    public void Reset() => _stateMachine.Reset(QueryState.Idle);

    private void OnStateChanged(object? sender, StateChangedEventArgs<QueryState> e)
    {
        StateChanged?.Invoke(this, e);

        RecordTransitionMetrics(e.OldState, e.NewState);
    }

    private void RecordTransitionMetrics(QueryState from, QueryState to)
        => _telemetryService?.RecordCount("query.state.transition.count", new() { ["from"] = from.ToString(), ["to"] = to.ToString() }, "count", "Query state transition count");

    private static FrozenDictionary<QueryState, FrozenSet<QueryState>> CreateTransitionTable()
    {
        var builder = new Dictionary<QueryState, FrozenSet<QueryState>>
        {
            [QueryState.Idle] = new HashSet<QueryState> { QueryState.Initializing }.ToFrozenSet(),
            [QueryState.Initializing] = new HashSet<QueryState> { QueryState.Running, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Running] = new HashSet<QueryState> { QueryState.WaitingForTool, QueryState.Compacting, QueryState.Stopping, QueryState.Completed, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.WaitingForTool] = new HashSet<QueryState> { QueryState.ExecutingTool, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.ExecutingTool] = new HashSet<QueryState> { QueryState.Running, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Compacting] = new HashSet<QueryState> { QueryState.Running, QueryState.Stopping, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Stopping] = new HashSet<QueryState> { QueryState.Completed, QueryState.Failed, QueryState.Cancelled }.ToFrozenSet(),
            [QueryState.Completed] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet(),
            [QueryState.Failed] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet(),
            [QueryState.Cancelled] = new HashSet<QueryState> { QueryState.Idle }.ToFrozenSet()
        };

        return builder.ToFrozenDictionary();
    }
}
