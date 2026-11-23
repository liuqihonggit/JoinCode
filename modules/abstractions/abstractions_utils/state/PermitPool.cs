namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 许可池 — Channel 容量令牌实现并发限流，支持热重载容量，无 OS 锁原语。
/// <para>每个许可对应容量通道中的一枚令牌；获取即读取令牌，释放即归还令牌。</para>
/// <para>热重载用新通道整体替换（CAS），在途许可归还到获取时捕获的旧通道。</para>
/// </summary>
public sealed class PermitPool : IDisposable {
    private Channel<bool> _permits;
    private int _capacity;
    private int _disposed;

    /// <summary>
    /// 构造许可池
    /// </summary>
    /// <param name="capacity">并发许可数（最小 1）</param>
    public PermitPool(int capacity) {
        _capacity = Math.Max(1, capacity);
        _permits = CreatePermitChannel(_capacity);
    }

    /// <summary>当前并发许可上限</summary>
    public int Capacity => Volatile.Read(ref _capacity);

    /// <summary>
    /// 异步获取一个许可 — 取消抛 <see cref="OperationCanceledException"/>。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>释放器，Dispose 时归还许可</returns>
    public async ValueTask<IDisposable> AcquireAsync(CancellationToken ct = default) {
        while (true) {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var channel = Volatile.Read(ref _permits);
            if (await channel.Reader.ReadAsync(ct).ConfigureAwait(false)) {
                return new Lease(channel);
            }
        }
    }

    /// <summary>
    /// 尝试同步获取一个许可 — 不等待，无可用许可立即返回 false。
    /// </summary>
    /// <param name="lease">获取成功时输出释放器，失败时输出 null</param>
    /// <returns>获取成功返回 true，否则返回 false</returns>
    public bool TryAcquire(out IDisposable? lease) {
        while (true) {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var channel = Volatile.Read(ref _permits);
            if (channel.Reader.TryRead(out _)) {
                lease = new Lease(channel);
                return true;
            }

            if (ReferenceEquals(channel, Volatile.Read(ref _permits))) {
                lease = null;
                return false;
            }
        }
    }

    /// <summary>
    /// 热重载许可上限 — 新容量从满额开始，旧池在途许可归还到旧池后丢弃。
    /// </summary>
    /// <param name="capacity">新并发上限（最小 1）</param>
    public void Resize(int capacity) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var newCapacity = Math.Max(1, capacity);
        if (newCapacity == Volatile.Read(ref _capacity)) return;

        var channel = CreatePermitChannel(newCapacity);
        var old = Interlocked.Exchange(ref _permits, channel);
        Volatile.Write(ref _capacity, newCapacity);
        old.Writer.TryComplete();
    }

    /// <summary>
    /// 释放许可池 — 关闭当前通道，等待中的获取者收到关闭信号。
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Read(ref _permits).Writer.TryComplete();
    }

    private static Channel<bool> CreatePermitChannel(int capacity) {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(capacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        for (var i = 0; i < capacity; i++) {
            channel.Writer.TryWrite(true);
        }

        return channel;
    }

    /// <summary>
    /// 许可释放器 — Dispose 时把令牌归还到获取时捕获的通道（幂等）
    /// </summary>
    private sealed class Lease : IDisposable {
        private Channel<bool>? _channel;

        /// <summary>
        /// 构造许可释放器
        /// </summary>
        /// <param name="channel">获取许可时捕获的通道</param>
        public Lease(Channel<bool> channel) {
            _channel = channel;
        }

        /// <summary>
        /// 归还许可 — 仅首次调用生效；池已热重载/释放时旧通道写入会被忽略
        /// </summary>
        public void Dispose() {
            Interlocked.Exchange(ref _channel, null)?.Writer.TryWrite(true);
        }
    }
}
