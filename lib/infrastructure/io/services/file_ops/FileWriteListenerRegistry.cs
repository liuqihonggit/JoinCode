namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件写入监听器注册中心的线程安全实现
/// </summary>
[Register(typeof(IFileWriteListenerRegistry), ServiceLifetime.Singleton)]
public sealed class FileWriteListenerRegistry : IFileWriteListenerRegistry
{
    private readonly ThreadSafeListenerList<IFileWriteListener> _listeners = new();

    /// <summary>
    /// 注册一个文件写入监听器，返回可释放的取消注册句柄
    /// </summary>
    /// <param name="listener">要注册的监听器</param>
    /// <returns>释放即取消注册的句柄</returns>
    public IDisposable Register(IFileWriteListener listener) => _listeners.Register(listener);

    /// <summary>
    /// 通知所有已注册监听器文件已被写入
    /// </summary>
    /// <param name="e">文件写入事件参数</param>
    public void Notify(FileWriteEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _listeners.Notify(l => l.OnFileWrite(e));
    }

    /// <summary>
    /// 获取当前已注册的监听器数量
    /// </summary>
    public int ListenerCount => _listeners.Count;
}
