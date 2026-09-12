namespace Core.Utils;

/// <summary>
/// 监督指令 — 子 Actor 失败时父 Actor 的处理决策。
/// </summary>
public enum SupervisorDirective
{
    /// <summary>恢复正常,继续运行(失败是暂时的)</summary>
    Resume,

    /// <summary>重启子 Actor(Dispose 旧实例 + 创建新实例)</summary>
    Restart,

    /// <summary>停止子 Actor,不再重启</summary>
    Stop,

    /// <summary>升级给父 Actor 处理(本 Actor 无法处理)</summary>
    Escalate
}

/// <summary>
/// 监督策略 — 决定子 Actor 失败时如何处理。
/// <para>包含最大重启次数、时间窗口、决策函数。</para>
/// <para>预定义:OneForOne(只重启失败的)、AllForOne(重启所有)、Escalate(向上抛)。</para>
/// </summary>
public sealed record SupervisorStrategy(
    int MaxRestarts,
    TimeSpan Within,
    Func<Exception, SupervisorDirective> Decider)
{
    /// <summary>OneForOne — 只重启失败的子 Actor,最多 3 次/分钟</summary>
    public static readonly SupervisorStrategy OneForOne = new(
        3, TimeSpan.FromMinutes(1),
        static ex => ex is OperationCanceledException ? SupervisorDirective.Stop : SupervisorDirective.Restart);

    /// <summary>AllForOne — 重启所有子 Actor(一个失败影响全部),最多 3 次/分钟</summary>
    public static readonly SupervisorStrategy AllForOne = new(
        3, TimeSpan.FromMinutes(1),
        static ex => ex is OperationCanceledException ? SupervisorDirective.Stop : SupervisorDirective.Restart);

    /// <summary>Escalate — 向上抛给父 Actor 处理</summary>
    public static readonly SupervisorStrategy Escalate = new(
        0, TimeSpan.MaxValue,
        static _ => SupervisorDirective.Escalate);
}

/// <summary>
/// 子 Actor 状态。
/// </summary>
public enum ChildActorState
{
    /// <summary>运行中</summary>
    Running,

    /// <summary>重启中</summary>
    Restarting,

    /// <summary>已停止</summary>
    Stopped,

    /// <summary>失败(超过最大重启次数)</summary>
    Failed
}

/// <summary>
/// 子 Actor 句柄 — 父 Actor 持有,只管理生命周期(启动/停止/重启),不代发消息。
/// <para>消息由外部直接调用子 Actor.SendAsync,保持类型安全。</para>
/// <para>父 Actor 通过 <see cref="SupervisedActor{TCommand}.OnChildFailureAsync"/> 决定如何处理失败。</para>
/// </summary>
public sealed class ChildActorHandle : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<IAsyncDisposable>> _factory;
    private readonly SupervisorStrategy _strategy;
    private readonly Func<ChildActorHandle, Exception, CancellationToken, ValueTask> _onFailure;
    private IAsyncDisposable? _instance;
    private int _restartCount;
    private readonly List<DateTimeOffset> _restartTimes = new();
    private readonly object _restartLock = new();

    /// <summary>子 Actor 唯一标识</summary>
    public string Id { get; }

    /// <summary>当前状态</summary>
    public ChildActorState State { get; private set; } = ChildActorState.Running;

    /// <summary>重启次数</summary>
    public int RestartCount => Volatile.Read(ref _restartCount);

    /// <summary>子 Actor 实例 — 外部可获取强类型引用发消息</summary>
    public IAsyncDisposable? Instance => _instance;

    /// <summary>监督策略</summary>
    public SupervisorStrategy Strategy => _strategy;

    internal ChildActorHandle(
        string id,
        Func<CancellationToken, ValueTask<IAsyncDisposable>> factory,
        SupervisorStrategy strategy,
        Func<ChildActorHandle, Exception, CancellationToken, ValueTask> onFailure)
    {
        Id = id;
        _factory = factory;
        _strategy = strategy;
        _onFailure = onFailure;
    }

    /// <summary>启动子 Actor</summary>
    internal async ValueTask StartAsync(CancellationToken ct)
    {
        _instance = await _factory(ct).ConfigureAwait(false);
        State = ChildActorState.Running;
    }

    /// <summary>子 Actor 失败时调用 — 根据策略决定处理方式</summary>
    internal async ValueTask HandleFailureAsync(Exception ex, CancellationToken ct)
    {
        var directive = _strategy.Decider(ex);
        switch (directive)
        {
            case SupervisorDirective.Resume:
                State = ChildActorState.Running;
                break;
            case SupervisorDirective.Restart:
                await RestartAsync(ct).ConfigureAwait(false);
                break;
            case SupervisorDirective.Stop:
                await StopAsync().ConfigureAwait(false);
                State = ChildActorState.Failed;
                break;
            case SupervisorDirective.Escalate:
                await _onFailure(this, ex, ct).ConfigureAwait(false);
                break;
        }
    }

    private bool TryRecordRestart()
    {
        lock (_restartLock)
        {
            var now = DateTimeOffset.UtcNow;
            _restartTimes.RemoveAll(t => now - t > _strategy.Within);
            if (_restartTimes.Count >= _strategy.MaxRestarts)
                return false;
            _restartTimes.Add(now);
            Interlocked.Increment(ref _restartCount);
            return true;
        }
    }

    private async ValueTask RestartAsync(CancellationToken ct)
    {
        if (!TryRecordRestart())
        {
            State = ChildActorState.Failed;
            return;
        }
        State = ChildActorState.Restarting;

        if (_instance is not null)
        {
            try { await _instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[ChildActor:{Id}] Dispose 旧实例异常忽略: {ex.Message}"); }
        }

        _instance = await _factory(ct).ConfigureAwait(false);
        State = ChildActorState.Running;
    }

    /// <summary>停止子 Actor</summary>
    public async ValueTask StopAsync()
    {
        if (_instance is not null)
        {
            try { await _instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[ChildActor:{Id}] Stop 异常忽略: {ex.Message}"); }
            _instance = null;
        }
        State = ChildActorState.Stopped;
    }

    /// <summary>释放 — 等同于 StopAsync</summary>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

/// <summary>
/// 监督事件 — 子 Actor 生命周期事件,通过 OutputAsync 流输出。
/// </summary>
public sealed record SupervisorEvent(string ChildId, ChildActorState State, string? Message = null);

/// <summary>
/// 监督 Actor — 继承 ActorBase 获得消息处理 + 输出流,新增树形父子关系 + 监督策略。
/// <para>父 Actor 在 Consumer 线程内管理子 Actor 生命周期,串行无锁。</para>
/// <para>子类必须实现 <see cref="OnChildFailureAsync"/> — 决定子 Actor 失败时如何处理(Rust 风格穷尽处理)。</para>
/// <para>子 Actor 生命周期事件通过 OutputAsync 流输出。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public abstract class SupervisedActor<TCommand> : ActorBase<TCommand, SupervisorEvent>
{
    private readonly ConcurrentDictionary<string, ChildActorHandle> _children = new(StringComparer.Ordinal);

    /// <summary>构造监督 Actor — 无背压(无界通道)</summary>
    protected SupervisedActor() : base() { }

    /// <summary>构造监督 Actor — 有界通道</summary>
    protected SupervisedActor(int? boundedCapacity, BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        : base(boundedCapacity, fullMode) { }

    /// <summary>构造监督 Actor — 完整背压配置</summary>
    protected SupervisedActor(ActorBackpressure? backpressure) : base(backpressure) { }

    /// <summary>
    /// 子 Actor 失败时如何处理 — 子类必须实现(Rust 风格穷尽处理)。
    /// <para>在父 Actor 的 Consumer 线程内调用,串行无锁。</para>
    /// <para>常见处理:记录日志 + 决定重启/停止/升级 + 通知外部。</para>
    /// </summary>
    /// <param name="child">失败的子 Actor 句柄</param>
    /// <param name="ex">失败异常</param>
    /// <param name="ct">取消令牌</param>
    protected abstract ValueTask OnChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct);

    /// <summary>
    /// 注册并启动子 Actor — 返回句柄,后续可用 handle.Instance 获取强类型引用发消息。
    /// </summary>
    /// <param name="childId">子 Actor 唯一标识</param>
    /// <param name="factory">子 Actor 创建工厂(重启时调用)</param>
    /// <param name="strategy">监督策略</param>
    /// <returns>子 Actor 句柄</returns>
    protected async ValueTask<ChildActorHandle> SpawnChildAsync(
        string childId,
        Func<CancellationToken, ValueTask<IAsyncDisposable>> factory,
        SupervisorStrategy strategy)
    {
        var handle = new ChildActorHandle(childId, factory, strategy, ReportChildFailureAsync);
        _children[childId] = handle;
        await handle.StartAsync(CancellationToken.None).ConfigureAwait(false);
        TryPublish(new SupervisorEvent(childId, ChildActorState.Running));
        return handle;
    }

    /// <summary>获取所有子 Actor 句柄</summary>
    protected IReadOnlyCollection<ChildActorHandle> GetChildren() => _children.Values.ToArray();

    /// <summary>按 ID 获取子 Actor 句柄</summary>
    protected ChildActorHandle? GetChild(string childId) =>
        _children.TryGetValue(childId, out var handle) ? handle : null;

    /// <summary>停止所有子 Actor — 父 Actor Dispose 时级联</summary>
    protected async ValueTask StopAllChildrenAsync()
    {
        foreach (var child in _children.Values)
        {
            await child.StopAsync().ConfigureAwait(false);
            TryPublish(new SupervisorEvent(child.Id, ChildActorState.Stopped));
        }
    }

    /// <summary>子 Actor 失败回调 — 转发给子类实现的 OnChildFailureAsync</summary>
    private async ValueTask ReportChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct)
    {
        await OnChildFailureAsync(child, ex, ct).ConfigureAwait(false);
        TryPublish(new SupervisorEvent(child.Id, child.State, ex.Message));
    }

    /// <summary>Dispose 时级联停止所有子 Actor</summary>
    public override async ValueTask DisposeAsync()
    {
        await StopAllChildrenAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
