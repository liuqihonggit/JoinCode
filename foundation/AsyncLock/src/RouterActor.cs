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
/// Router Actor — 多 Worker 负载分发 + 监督。
/// <para>继承 SupervisedActor 获得:父子关系、监督策略、背压、生命周期级联。</para>
/// <para>Worker 崩溃时按 SupervisorStrategy 自动重启(OneForOne 默认)。</para>
    /// <para>消息通过 RouteAsync 投递,由 <see cref="IRouterStrategy{TMessage}"/> 决定分发目标。</para>
/// </summary>
/// <typeparam name="TMessage">路由消息类型</typeparam>
public class RouterActor<TMessage> : SupervisedActor<RouterActor<TMessage>.IRouterCommand>
{
    /// <summary>Router 命令标记接口</summary>
    public interface IRouterCommand;

    private sealed record RouteCommand(TMessage Message, Func<TMessage, IAsyncDisposable, ValueTask> Deliver) : IRouterCommand;

    private readonly IRouterStrategy<TMessage> _strategy;
    private readonly SupervisorStrategy _childStrategy;
    private readonly Action<ChildActorHandle, Exception>? _onWorkerFailure;
    private readonly List<string> _workerIds = new();

    /// <summary>
    /// 构造 Router Actor — 使用 Router 背压配置(容量 1000 + 10s 超时)。
    /// </summary>
    /// <param name="strategy">路由策略(null=轮询)</param>
    /// <param name="childStrategy">子 Worker 监督策略(null=OneForOne)</param>
    /// <param name="onWorkerFailure">Worker 失败回调(可选,Escalate 策略时调用)</param>
    protected RouterActor(
        IRouterStrategy<TMessage>? strategy = null,
        SupervisorStrategy? childStrategy = null,
        Action<ChildActorHandle, Exception>? onWorkerFailure = null)
        : base(ActorBackpressure.Router)
    {
        _strategy = strategy ?? new RoundRobinStrategy<TMessage>();
        _childStrategy = childStrategy ?? SupervisorStrategy.OneForOne;
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
        _workerIds.Add(workerId);
        return await SpawnChildAsync(workerId, workerFactory, _childStrategy).ConfigureAwait(false);
    }

    /// <summary>
    /// 路由消息到 Worker — 由路由策略决定目标 Worker,然后调用 deliver 投递。
    /// <para>同步投递版本 — deliver 内部应使用 TrySend 等同步方法,不可阻塞。</para>
    /// </summary>
    /// <param name="message">待路由消息</param>
    /// <param name="deliver">投递函数:接收消息和 Worker 实例,由调用方强类型发送</param>
    public ValueTask RouteAsync(TMessage message, Action<TMessage, IAsyncDisposable> deliver)
    {
        return SendAsync(new RouteCommand(message, (msg, worker) =>
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
        return SendAsync(new RouteCommand(message, deliver));
    }

    /// <summary>当前 Worker 数量</summary>
    public int WorkerCount => _workerIds.Count;

    /// <summary>Consumer 线程内处理路由命令</summary>
    protected override async ValueTask HandleAsync(IRouterCommand command, CancellationToken ct)
    {
        if (command is RouteCommand(var msg, var deliver))
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
            }
        }
    }

    /// <summary>子 Worker 失败处理 — 调用可选回调,子类可重写自定义</summary>
    protected override ValueTask OnChildFailureAsync(ChildActorHandle child, Exception ex, CancellationToken ct)
    {
        _onWorkerFailure?.Invoke(child, ex);
        return ValueTask.CompletedTask;
    }
}
