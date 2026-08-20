namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件写入监听器注册中心的线程安全实现
/// </summary>
[Register(typeof(IFileWriteListenerRegistry))]
public sealed class FileWriteListenerRegistry : IFileWriteListenerRegistry
{
    private readonly ThreadSafeListenerList<IFileWriteListener> _listeners = new();

    public IDisposable Register(IFileWriteListener listener) => _listeners.Register(listener);

    public void Notify(FileWriteEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _listeners.Notify(l => l.OnFileWrite(e));
    }
}
