namespace Core.Utils;

/// <summary>
/// FSM 状态转换结果 — 包含目标状态和数据(Akka FSM 对齐)。
/// </summary>
/// <typeparam name="TState">状态类型</typeparam>
/// <typeparam name="TData">数据类型</typeparam>
/// <param name="State">目标状态</param>
/// <param name="Data">新数据</param>
public readonly record struct FsmResult<TState, TData>(TState State, TData Data) {
    /// <summary>转换到指定状态,保持当前数据</summary>
    public static FsmResult<TState, TData> GoTo(TState state, TData currentData) => new(state, currentData);

    /// <summary>保持当前状态,更新数据</summary>
    public static FsmResult<TState, TData> Stay(TState currentState, TData data) => new(currentState, data);

    /// <summary>链式更新数据 — GoTo(state).Using(data) 同时设置状态和数据</summary>
    public FsmResult<TState, TData> Using(TData data) => new(State, data);
}

/// <summary>
/// 有限状态机 Actor — 用状态机模型组织行为(Akka FSM 对齐)。
/// <para>状态机由状态(TState) + 数据(TData) + 状态处理函数表组成。</para>
/// <para>消息处理:根据当前状态查找 handler,handler 返回 <see cref="FsmResult{TState, TData}"/>(新状态+新数据)。</para>
/// <para>用法:PreStart 中 <see cref="When"/> 注册 handler + <see cref="StartWith"/> 设初始状态;handler 中 <see cref="GoTo"/>/<see cref="Stay"/> 转换状态。</para>
/// </summary>
/// <typeparam name="TState">状态类型(建议用 enum)</typeparam>
/// <typeparam name="TData">状态数据类型(建议用 record)</typeparam>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <typeparam name="TOut">输出消息类型</typeparam>
public abstract class ActorFsm<TState, TData, TCommand, TOut> : ActorBase<TCommand, TOut>
    where TState : notnull
    where TData : notnull {
    private TState? _currentState;
    private TData? _currentData;
    private readonly Dictionary<TState, Func<TCommand, TData, CancellationToken, FsmResult<TState, TData>>> _handlers = new();

    /// <summary>构造 FSM — 无背压(无界通道)</summary>
    protected ActorFsm() : base() { }

    /// <summary>构造 FSM — 有界通道</summary>
    protected ActorFsm(int? boundedCapacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        : base(boundedCapacity, fullMode) { }

    /// <summary>构造 FSM — 完整背压配置</summary>
    protected ActorFsm(ActorBackpressure? backpressure) : base(backpressure) { }

    /// <summary>当前状态 — 供测试和监控查询</summary>
    protected TState CurrentState => _currentState!;

    /// <summary>当前数据 — 供测试和监控查询</summary>
    protected TData CurrentData => _currentData!;

    /// <summary>
    /// 设初始状态和数据 — 在 PreStart 中调用。
    /// </summary>
    /// <param name="state">初始状态</param>
    /// <param name="data">初始数据</param>
    protected void StartWith(TState state, TData data) {
        _currentState = state;
        _currentData = data;
    }

    /// <summary>
    /// 注册状态处理函数 — 在 PreStart 中调用。
    /// </summary>
    /// <param name="state">状态</param>
    /// <param name="handler">处理函数:接收命令+当前数据,返回状态转换结果</param>
    protected void When(TState state, Func<TCommand, TData, CancellationToken, FsmResult<TState, TData>> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[state] = handler;
    }

    /// <summary>转换到指定状态,保持当前数据 — 在 handler 中调用</summary>
    /// <param name="state">目标状态</param>
    /// <returns>状态转换结果</returns>
    protected FsmResult<TState, TData> GoTo(TState state) => new(state, _currentData!);

    /// <summary>保持当前状态,保持当前数据 — 在 handler 中调用</summary>
    /// <returns>状态转换结果</returns>
    protected FsmResult<TState, TData> Stay() => new(_currentState!, _currentData!);

    /// <summary>保持当前状态,更新数据 — 在 handler 中调用</summary>
    /// <param name="data">新数据</param>
    /// <returns>状态转换结果</returns>
    protected FsmResult<TState, TData> Using(TData data) => new(_currentState!, data);

    /// <summary>FSM 消息处理 — 根据当前状态查找 handler,执行状态转换</summary>
    protected override void Handle(TCommand command, CancellationToken ct) {
        if (_currentState is null) return;
        if (!_handlers.TryGetValue(_currentState, out var handler)) return;
        var result = handler(command, _currentData!, ct);
        _currentState = result.State;
        _currentData = result.Data;
    }
}
