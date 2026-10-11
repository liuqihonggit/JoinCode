namespace Core.Utils;

/// <summary>
/// 测试探针 — 比 Inbox 更强大的测试工具,支持 ExpectMsg 类型检查 + Reply 回复(Akka TestKit 对齐)。
/// <para>实现 <see cref="IActorTell{TCommand}"/> 接口,Actor 可通过 Sender.Tell 回复给 TestProbe。</para>
/// <para>ExpectMsgAsync 等待特定类型消息,类型不匹配抛 <see cref="InvalidOperationException"/>。</para>
/// <para>Reply 回复最近收到消息的发送者(需发送者实现 <see cref="IActorTell{TCommand}"/>)。</para>
/// </summary>
public sealed class TestProbe : IActorTell<object>, IAsyncDisposable {
    private readonly Channel<(object Message, object? Sender)> _channel = Channel.CreateUnbounded<(object, object?)>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false
    });
    private int _disposed;
    private (object Message, object? Sender) _lastReceived;

    /// <summary>
    /// 接收消息 — Actor 回复时调用,记录消息和发送者。
    /// </summary>
    /// <param name="msg">回复消息</param>
    /// <param name="sender">发送者(Actor 回复时传入自身引用)</param>
    public void Tell(object msg, object? sender = null) {
        if (Volatile.Read(ref _disposed) != 0) return;
        _channel.Writer.TryWrite((msg, sender));
    }

    /// <summary>TrySend — TestProbe 总是接受消息(无界 Channel)</summary>
    public bool TrySend(object msg, object? sender = null) {
        Tell(msg, sender);
        return true;
    }

    /// <summary>TryTell — TrySend 语义别名</summary>
    public bool TryTell(object msg, object? sender = null) => TrySend(msg, sender);

    /// <summary>
    /// 发消息给 Actor — sender 设为 this,Actor 在 Handle 中通过 Sender 获取 TestProbe 引用回复。
    /// </summary>
    /// <typeparam name="TCommand">Actor 命令类型</typeparam>
    /// <param name="actor">目标 Actor</param>
    /// <param name="message">消息</param>
    public void Send<TCommand>(IActor<TCommand> actor, TCommand message) {
        actor.Tell(message, this);
    }

    /// <summary>
    /// 等待接收类型 T 的消息 — 超时抛 <see cref="TimeoutException"/>,类型不匹配抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <typeparam name="T">期望消息类型</typeparam>
    /// <param name="timeout">超时</param>
    /// <returns>接收到的消息</returns>
    public async Task<T> ExpectMsgAsync<T>(TimeSpan timeout) {
        var (msg, sender) = await ReceiveInternalAsync(timeout).ConfigureAwait(false);
        _lastReceived = (msg, sender);
        if (msg is not T typed) throw new InvalidOperationException($"期望消息类型 {typeof(T).Name},实际收到 {msg.GetType().Name}");
        return typed;
    }

    /// <summary>
    /// 等待接收特定消息 — 超时抛 <see cref="TimeoutException"/>,不匹配抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <typeparam name="T">期望消息类型</typeparam>
    /// <param name="expected">期望的消息值</param>
    /// <param name="timeout">超时</param>
    public async Task ExpectMsgAsync<T>(T expected, TimeSpan timeout) {
        var actual = await ExpectMsgAsync<T>(timeout).ConfigureAwait(false);
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException($"期望消息 {expected},实际收到 {actual}");
    }

    /// <summary>
    /// 回复最近收到消息的发送者 — 发送者需实现 <see cref="IActorTell{TCommand}"/>。
    /// </summary>
    /// <param name="msg">回复消息</param>
    public void Reply(object msg) {
        if (_lastReceived.Sender is IActorTell<object> target) {
            target.Tell(msg, this);
        }
    }

    /// <summary>
    /// 等待满足条件的消息 — 跳过不满足的消息。
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="predicate">过滤谓词</param>
    /// <param name="timeout">超时</param>
    /// <returns>满足条件的消息</returns>
    public async Task<T> FishForMessageAsync<T>(Func<T, bool> predicate, TimeSpan timeout) {
        ArgumentNullException.ThrowIfNull(predicate);
        using var cts = new CancellationTokenSource(timeout);
        try {
            while (true) {
                var (msg, sender) = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                _lastReceived = (msg, sender);
                if (msg is T typed && predicate(typed)) return typed;
            }
        } catch (OperationCanceledException) {
            throw new TimeoutException($"TestProbe.FishForMessage 超时 {timeout.TotalMilliseconds:F0}ms");
        }
    }

    private async Task<(object Message, object? Sender)> ReceiveInternalAsync(TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        try {
            return await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw new TimeoutException($"TestProbe.ExpectMsg 超时 {timeout.TotalMilliseconds:F0}ms,未收到消息");
        }
    }

    /// <summary>释放 TestProbe — 完成通道</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
