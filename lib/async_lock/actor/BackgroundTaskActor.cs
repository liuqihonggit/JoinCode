namespace Core.Utils;

/// <summary>
/// 后台任务命令 — 包含任务名和任务工厂委托。
/// </summary>
/// <param name="TaskName">任务名称(用于日志和事件标识)</param>
/// <param name="TaskFactory">任务工厂委托 — 接收取消令牌,返回异步任务</param>
public sealed record BackgroundTaskCommand(string TaskName, Func<CancellationToken, Task> TaskFactory);

/// <summary>
/// 后台任务事件 — 任务完成或失败时发布到输出通道。
/// </summary>
/// <param name="TaskName">任务名称</param>
/// <param name="Success">是否成功</param>
/// <param name="Error">失败异常(成功时为 null)</param>
public sealed record BackgroundTaskEvent(string TaskName, bool Success, Exception? Error);

/// <summary>
/// 后台任务失败事件参数。
/// </summary>
public sealed class BackgroundTaskFailedEventArgs : EventArgs {
    /// <summary>任务名称</summary>
    public string TaskName { get; }

    /// <summary>失败异常</summary>
    public Exception Exception { get; }

    /// <summary>构造后台任务失败事件参数</summary>
    public BackgroundTaskFailedEventArgs(string taskName, Exception ex) {
        TaskName = taskName;
        Exception = ex;
    }
}

/// <summary>
/// 后台任务监督 Actor — 接收后台任务命令,执行任务,异常不吞没。
/// <para>替代 fire-and-forget + WaitAsync(10s) 模式,异常可观测、可监督。</para>
/// <para>通过 <see cref="ActorBase{TCommand,TOut}.SendAsync"/> 发送命令(Tell 模式,射后不理)。</para>
/// <para>任务失败时通过 <see cref="TaskFailed"/> 事件通知外部,不吞没异常。</para>
/// <para>有界输入通道(默认 256),通道满时背压等待,防止任务堆积 OOM。</para>
/// <para>parallel 模式:任务在 ThreadPool 并行执行,不阻塞命令队列(适合长任务如后台代理运行)。</para>
/// <para>串行模式(默认):任务在 Consumer 线程串行执行,有背压控制(适合短任务如保存/加载/检查)。</para>
/// </summary>
public sealed class BackgroundTaskActor : ActorBase<BackgroundTaskCommand, BackgroundTaskEvent> {
    private readonly ILogger? _logger;
    private readonly bool _parallel;

    /// <summary>后台任务失败事件 — 异常不吞没,外部可订阅处理</summary>
    public event EventHandler<BackgroundTaskFailedEventArgs>? TaskFailed;

    /// <summary>
    /// 构造后台任务监督 Actor — 有界输入通道(默认 256),防止任务堆积。
    /// </summary>
    /// <param name="logger">日志器(可选)</param>
    /// <param name="boundedCapacity">有界输入通道容量(默认 256)</param>
    /// <param name="parallel">是否并行执行任务(默认 false 串行)。并行模式:任务在 ThreadPool 执行,不阻塞命令队列</param>
    public BackgroundTaskActor(ILogger? logger = null, int boundedCapacity = 256, bool parallel = false)
        : base(boundedCapacity, BoundedChannelFullMode.Wait) {
        _logger = logger;
        _parallel = parallel;
    }

    /// <summary>
    /// 处理后台任务命令 — 执行任务工厂,异常不吞没。
    /// <para>并行模式:Task.Run 启动,不阻塞命令队列;串行模式:直接 await,阻塞队列直到完成。</para>
    /// </summary>
    protected override async ValueTask HandleAsync(BackgroundTaskCommand command, CancellationToken ct) {
        if (_parallel) {
            _ = Task.Run(() => ExecuteTaskAsync(command, ct), CancellationToken.None);
        } else {
            await ExecuteTaskAsync(command, ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteTaskAsync(BackgroundTaskCommand command, CancellationToken ct) {
        try {
            await command.TaskFactory(ct).ConfigureAwait(false);
            _logger?.LogDebug("[BackgroundTaskActor] 后台任务完成: {TaskName}", command.TaskName);
        } catch (OperationCanceledException) {
            // 取消是正常行为,不记录
        } catch (Exception ex) {
            _logger?.LogError(ex, "[BackgroundTaskActor] 后台任务失败: {TaskName}", command.TaskName);
            TaskFailed?.Invoke(this, new BackgroundTaskFailedEventArgs(command.TaskName, ex));
            TryPublish(new BackgroundTaskEvent(command.TaskName, false, ex));
        }
    }

    /// <summary>
    /// Consumer 循环异常回调 — 记录日志,不吞没。
    /// </summary>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogError(ex, "[BackgroundTaskActor] Consumer 循环异常");
    }
}
