namespace JoinCode.Transport.Bridge;

/// <summary>
/// 批次刷新事件参数
/// </summary>
public sealed class BatchFlushedEventArgs<T> : EventArgs
{
    public IReadOnlyList<T> Items { get; }

    public BatchFlushedEventArgs(IReadOnlyList<T> items)
    {
        Items = items;
    }
}

/// <summary>
/// 刷新门控接口 — 批量收集条目并定期或满批时刷新
/// </summary>
public interface IFlushGate<T> : IAsyncDisposable
{
    /// <summary>批次刷新事件</summary>
    event EventHandler<BatchFlushedEventArgs<T>>? BatchFlushed;

    /// <summary>启动定时刷新循环</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止定时刷新循环，并刷新剩余条目</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>添加条目到当前批次</summary>
    Task AddAsync(T item, CancellationToken ct = default);

    /// <summary>手动触发刷新</summary>
    Task FlushAsync(CancellationToken ct = default);
}
