namespace Core.Utils;

/// <summary>
/// 测试用消息收件箱 — 发消息给 Actor + 接收 Actor 回复(Akka Inbox 对齐)。
/// <para>非 Actor 轻测试代码与 Actor 交互的桥梁:Send 发消息给 Actor,Tell 接收 Actor 回复,ReceiveAsync 读取回复。</para>
/// <para>Actor 在 Handle 中通过 <c>((Inbox)Sender).Tell(reply)</c> 回复,或直接调 <c>inbox.Tell(reply)</c>。</para>
/// <para>线程安全:内部用 Channel 缓冲消息,多线程 Tell 安全。</para>
/// </summary>
public sealed class Inbox : IAsyncDisposable {
    private readonly Channel<object> _channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false
    });
    private int _disposed;

    /// <summary>
    /// 接收消息 — Actor 回复时调用,消息入内部 Channel。
    /// <para>Actor 在 Handle 中通过 <c>((Inbox)Sender).Tell(reply)</c> 回复。</para>
    /// </summary>
    /// <param name="message">回复消息</param>
    public void Tell(object message) {
        if (Volatile.Read(ref _disposed) != 0) return;
        _channel.Writer.TryWrite(message);
    }

    /// <summary>
    /// 发消息给 Actor — sender 设为 this,Actor 在 Handle 中通过 Sender 获取 Inbox 引用回复。
    /// </summary>
    /// <typeparam name="TCommand">Actor 命令类型</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="message">消息</param>
    public void Send<TCommand>(IActor<TCommand> actor, TCommand message) {
        actor.Tell(message, this);
    }

    /// <summary>
    /// 等待接收一条消息 — 超时抛 <see cref="TimeoutException"/>。
    /// </summary>
    /// <param name="timeout">超时</param>
    /// <returns>接收到的消息</returns>
    /// <exception cref="TimeoutException">超时未收到消息</exception>
    public async Task<object> ReceiveAsync(TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        try {
            return await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw new TimeoutException($"Inbox.Receive 超时 {timeout.TotalMilliseconds:F0}ms,未收到消息");
        }
    }

    /// <summary>
    /// 等待接收满足条件的消息 — 跳过不满足的消息,超时抛 <see cref="TimeoutException"/>。
    /// </summary>
    /// <param name="predicate">消息过滤谓词</param>
    /// <param name="timeout">超时</param>
    /// <returns>满足条件的消息</returns>
    /// <exception cref="TimeoutException">超时未收到满足条件的消息</exception>
    public async Task<object> ReceiveWhereAsync(Func<object, bool> predicate, TimeSpan timeout) {
        ArgumentNullException.ThrowIfNull(predicate);
        using var cts = new CancellationTokenSource(timeout);
        try {
            while (true) {
                var msg = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                if (predicate(msg)) return msg;
            }
        } catch (OperationCanceledException) {
            throw new TimeoutException($"Inbox.ReceiveWhere 超时 {timeout.TotalMilliseconds:F0}ms,未收到满足条件的消息");
        }
    }

    /// <summary>释放 Inbox — 完成通道,后续 Tell 静默忽略。</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
