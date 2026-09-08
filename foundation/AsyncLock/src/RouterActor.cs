namespace Core.Utils;

/// <summary>
/// 路由策略接口 — 决定消息分发到哪个 Worker。
/// <para>可替换实现:轮询、最少负载、一致性哈希等。</para>
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
public interface IRouterStrategy<TMessage>
{
    /// <summary>选择目标 Worker 索引</summary>
    /// <param name="workerCount">当前 Worker 数量</param>
    /// <param name="message">待路由消息</param>
    /// <returns>Worker 索引(0 到 workerCount-1)</returns>
    int Select(int workerCount, TMessage message);
}

/// <summary>
/// 轮询路由策略 — 依次分发,均匀负载。
/// </summary>
public sealed class RoundRobinStrategy<TMessage> : IRouterStrategy<TMessage>
{
    private int _index;

    public int Select(int workerCount, TMessage message)
    {
        if (workerCount <= 0) return 0;
        return Interlocked.Increment(ref _index) % workerCount;
    }
}

/// <summary>
/// 随机路由策略 — 随机选择 Worker,避免轮询的集中性。
/// </summary>
public sealed class RandomRouteStrategy<TMessage> : IRouterStrategy<TMessage>
{
    private readonly Random _random = new();

    public int Select(int workerCount, TMessage message)
    {
        if (workerCount <= 0) return 0;
        lock (_random)
        {
            return _random.Next(workerCount);
        }
    }
}

/// <summary>
/// 路由事件 — 消息路由到 Worker 的事件,通过 OutputAsync 流输出。
/// </summary>
public sealed record RouterEvent<TMessage>(string WorkerId, TMessage Message, int WorkerIndex);

/// <summary>
/// Router 命令标记接口 — 所有 Router 命令的基类。
/// </summary>
public interface IRouterCommand;

/// <summary>
/// 路由命令 — 内部记录消息和投递函数。
/// </summary>
internal sealed record RouteCommand<TMessage>(TMessage Message, Func<TMessage, IAsyncDisposable, ValueTask> Deliver) : IRouterCommand;

/// <summary>
/// Router Actor — 多 Worker 负载分发 + 轻量监督。
/// <para>直接继承 ActorBase 获得消息处理 + 输出流,自行管理子 Actor 生命周期。</para>
/// <para>Worker 崩溃时调用 onWorkerFailure 回调,子类可重写自定义处理。</para>
/// <para>消息通过 RouteAsync 投递,由 <see cref="IRouterStrategy{TMessage}"/> 决定分发目标。</para>
/// <para>路由事件通过 OutputAsync 流输出。</para>
/// </summary>
/// <typeparam name="TMessage">路由消息类型</typeparam>
public class RouterActor<TMessage> : ActorBase<IRouterCommand, RouterEvent<TMessage>>
{
    private readonly IRouterStrategy<TMessage> _strategy;
    private readonly Action<ChildActorHandle, Exception>? _onWorkerFailure;
    private readonly ConcurrentDictionary<string, ChildActorHandle> _children = new(StringComparer.Ordinal);

    /// <summary>
    /// 构造 Router Actor — 使用 Router 背压配置(容量 1000 + 10s 超时)。
    /// </summary>
    /// <param name="strategy">路由策略(null=轮询)</param>
    /// <param name="onWorkerFailure">Worker 失败回调(可选)</param>
    protected RouterActor(
        IRouterStrategy<TMessage>? strategy = null,
        Action<ChildActorHandle, Exception>? onWorkerFailure = null)
        : base(ActorBackpressure.Router)
    {
        _strategy = strategy ?? new RoundRobinStrategy<TMessage>();
        _onWorkerFailure = onWorkerFailure;
    }

    /// <summary>
    /// 添加 Worker — 返回句柄,后续可用 handle.Instance 获取强类型引用。
    /// </summary>
    /// <param name="workerId">Worker 唯一标识</param>
    /// <param name="workerFactory">Worker 创建工厂(重启时调用)</param>
    public async ValueTask<ChildActorHandle> AddWorkerAsync(
        string workerId,
        Func<CancellationToken, ValueTask<IAsyncDisposable>> workerFactory)
    {
        var handle = new ChildActorHandle(
            workerId, workerFactory,
            SupervisorStrategy.OneForOne,
            ReportChildFailureAsync);
        _children[workerId] = handle;
        await handle.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return handle;
    }

    /// <summary>
    /// 路由消息到 Worker — 由路由策略决定目标 Worker,然后调用 deliver 投递。
    /// <para>同步投递版本 — deliver 内部应使用 TrySend 等同步方法,不可阻塞。</para>
    /// </summary>
    /// <param name="message">待路由消息</param>
    /// <param name="deliver">投递函数:接收消息和 Worker 实例,由调用方强类型发送</param>
    public ValueTask RouteAsync(TMessage message, Action<TMessage, IAsyncDisposable> deliver)
    {
        return SendAsync(new RouteCommand<TMessage>(message, (msg, worker) =>
        {
            deliver(msg, worker);
            return ValueTask.CompletedTask;
        }));
    }

    /// <summary>
    /// 路由消息到 Worker — 异步投递版本,deliver 可 await 背压等待。
    /// <para>适用于编译等需要背压控制的场景:deliver 内部用 SendAsync 异步投递。</para>
    /// </summary>
    /// <param name="message">待路由消息</param>
    /// <param name="deliver">异步投递函数:可 await Worker.SendAsync 等待背压</param>
    public ValueTask RouteAsync(TMessage message, Func<TMessage, IAsyncDisposable, ValueTask> deliver)
    {
        return SendAsync(new RouteCommand<TMessage>(message, deliver));
    }

    /// <summary>当前 Worker 数量</summary>
    public int WorkerCount => _children.Count;

    /// <summary>Consumer 线程内处理路由命令</summary>
    protected override async ValueTask HandleAsync(IRouterCommand command, CancellationToken ct)
    {
        if (command is RouteCommand<TMessage>(var msg, var deliver))
        {
            var children = GetChildren();
            if (children.Count == 0) return;

            var idx = _strategy.Select(children.Count, msg);
            idx = Math.Clamp(idx, 0, children.Count - 1);

            var workers = children.ToArray();
            var worker = workers[idx];
            if (worker.Instance is not null)
            {
                await deliver(msg, worker.Instance).ConfigureAwait(false);
                TryPublish(new RouterEvent<TMessage>(worker.Id, msg, idx));
            }
        }
    }

    /// <summary>子 Worker 失败处理 — 调用可选回调,子类可重写自定义</summary>
    private async ValueTask ReportChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct)
    {
        _onWorkerFailure?.Invoke(child, ex);
        await child.HandleFailureAsync(ex, ct).ConfigureAwait(false);
    }

    /// <summary>获取所有子 Actor 句柄</summary>
    protected IReadOnlyCollection<ChildActorHandle> GetChildren() => _children.Values.ToArray();

    /// <summary>Dispose 时级联停止所有子 Actor</summary>
    public override async ValueTask DisposeAsync()
    {
        foreach (var child in _children.Values)
        {
            try { await child.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[RouterActor:{Id}] Stop child {child.Id} 异常忽略: {ex.Message}"); }
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
