namespace JoinCode.Gui.Hosting;

/// <summary>
/// 单写者串行邮箱：所有访问共享状态的操作进入同一有界命令队列，由唯一消费者按序执行。
/// 用业务串行化替代锁 — 状态只被消费者线程读写，生产者以完成信号取回结果。
/// 有界队列满时生产者异步等待腾空（背压），不丢弃操作。
/// </summary>
internal sealed class DshSerialMailbox : IAsyncDisposable {
    private readonly Channel<Func<Task>> _commands;
    private readonly Task _loop;
    private int _disposed;

    /// <summary>创建邮箱并启动唯一消费者</summary>
    /// <param name="capacity">命令队列容量（有界背压）</param>
    internal DshSerialMailbox(int capacity = 512) {
        _commands = Channel.CreateBounded<Func<Task>>(
            new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>异步执行一个独占操作并取回结果</summary>
    /// <param name="operation">在消费者线程执行的操作</param>
    internal async Task<T> RunAsync<T>(Func<Task<T>> operation) {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(async () => {
            try {
                completion.TrySetResult(await operation().ConfigureAwait(false));
            } catch (OperationCanceledException ex) {
                completion.TrySetCanceled(ex.CancellationToken);
            } catch (Exception ex) {
                completion.TrySetException(ex);
            }
        }).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>异步执行一个独占操作</summary>
    /// <param name="operation">在消费者线程执行的操作</param>
    internal Task RunAsync(Func<Task> operation) =>
        RunAsync(async () => {
            await operation().ConfigureAwait(false);
            return true;
        });

    /// <summary>同步执行一个独占操作并取回结果（调用方为非消费者线程）</summary>
    /// <param name="operation">在消费者线程执行的操作</param>
    internal T Run<T>(Func<T> operation) => RunAsync(() => Task.FromResult(operation())).GetAwaiter().GetResult();  // style-allow: sync-block — 串行邮箱同步门面: UI 线程按序执行不可 await

    /// <summary>同步执行一个独占操作（调用方为非消费者线程）</summary>
    /// <param name="operation">在消费者线程执行的操作</param>
    internal void Run(Action operation) => RunAsync(() => { operation(); return Task.FromResult(true); }).GetAwaiter().GetResult();  // style-allow: sync-block — 串行邮箱同步门面: UI 线程按序执行不可 await

    private async Task EnqueueAsync(Func<Task> command) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        try {
            await _commands.Writer.WriteAsync(command).ConfigureAwait(false);
        } catch (ChannelClosedException) {
            throw new ObjectDisposedException(nameof(DshSerialMailbox));
        }
    }

    private async Task LoopAsync() {
        await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false)) {
            try {
                await command().ConfigureAwait(false);
            } catch (Exception ex) {
                GuiDiag.Log($"[DSH-MAILBOX] command failed: {ex.Message}");
            }
        }
    }

    /// <summary>停止接收并排空已有命令</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }
        _commands.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }
}
