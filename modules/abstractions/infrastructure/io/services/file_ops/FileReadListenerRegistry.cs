namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件读取监听器注册中心的线程安全实现。
/// 对齐 TS FileReadTool: registerFileReadListener / fileReadListeners
/// </summary>
public sealed class FileReadListenerRegistry : IFileReadListenerRegistry {
    private readonly ThreadSafeListenerList<IFileReadListener> _listeners = new();

    /// <summary>
    /// 注册一个文件读取监听器，返回可释放的取消注册句柄
    /// </summary>
    /// <param name="listener">要注册的监听器</param>
    /// <returns>释放即取消注册的句柄</returns>
    public IDisposable Register(IFileReadListener listener) => _listeners.Register(listener);

    /// <summary>
    /// 通知所有已注册监听器文件已被读取
    /// </summary>
    /// <param name="e">文件读取事件参数</param>
    public void Notify(FileReadEventArgs e) {
        ArgumentNullException.ThrowIfNull(e);
        _listeners.Notify(l => l.OnFileRead(e));
    }
}