namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件读取监听器注册中心的线程安全实现。
/// 对齐 TS FileReadTool: registerFileReadListener / fileReadListeners
/// </summary>
public sealed class FileReadListenerRegistry : IFileReadListenerRegistry
{
    private readonly List<IFileReadListener> _listeners = [];
    private readonly object _lock = new();

    public IDisposable Register(IFileReadListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return new UnsubscribeToken(this, listener);
    }

    public void Notify(FileReadEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // 对齐 TS: fileReadListeners.slice() — 快照遍历，防止回调中取消订阅导致跳过后续监听器
        IFileReadListener[] snapshot;
        lock (_lock)
        {
            snapshot = _listeners.ToArray();
        }

        foreach (var listener in snapshot)
        {
            try
            {
                listener.OnFileRead(e);
            }
            catch (Exception ex)
            {
                // 对齐 TS: 监听器异常不影响其他监听器和文件读取流程
                System.Diagnostics.Trace.WriteLine($"FileReadListenerRegistry: listener threw exception: {ex.Message}");
            }
        }
    }

    private void Unsubscribe(IFileReadListener listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    /// <summary>
    /// 取消订阅 token。
    /// 对齐 TS: registerFileReadListener 返回的 unsubscribe 函数。
    /// </summary>
    private sealed class UnsubscribeToken(FileReadListenerRegistry registry, IFileReadListener listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                registry.Unsubscribe(listener);
            }
        }
    }
}
