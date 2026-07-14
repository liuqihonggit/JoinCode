namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件读取监听器注册中心的线程安全实现。
/// 对齐 TS FileReadTool: registerFileReadListener / fileReadListeners
/// </summary>
public sealed class FileReadListenerRegistry : IFileReadListenerRegistry
{
    private readonly ThreadSafeListenerList<IFileReadListener> _listeners = new();

    public IDisposable Register(IFileReadListener listener) => _listeners.Register(listener);

    public void Notify(FileReadEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _listeners.Notify(l => l.OnFileRead(e));
    }
}
