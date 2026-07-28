namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件读取事件参数。
/// 对齐 TS FileReadTool: fileReadListeners — 通知监听器文件被读取的事件数据。
/// </summary>
public sealed record FileReadEventArgs
{
    /// <summary>
    /// 被读取文件的已解析绝对路径。
    /// 对齐 TS: resolvedFilePath（非用户输入的原始路径）。
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 文件的完整文本内容。
    /// 对齐 TS: content。
    /// </summary>
    public required string Content { get; init; }
}

/// <summary>
/// 文件读取监听器接口。
/// 对齐 TS FileReadTool: type FileReadListener = (filePath: string, content: string) => void
/// </summary>
public interface IFileReadListener
{
    /// <summary>
    /// 当文件被成功读取时调用。
    /// 仅在文本文件读取成功后触发，PDF/Notebook/图像等特殊文件不触发。
    /// </summary>
    void OnFileRead(FileReadEventArgs e);
}

/// <summary>
/// 文件读取监听器注册中心。
/// 对齐 TS FileReadTool: registerFileReadListener / fileReadListeners
/// 支持注册/取消订阅，线程安全。
/// </summary>
public interface IFileReadListenerRegistry
{
    /// <summary>
    /// 注册文件读取监听器，返回取消订阅的 token。
    /// 对齐 TS: registerFileReadListener(listener) => unsubscribe 函数
    /// </summary>
    IDisposable Register(IFileReadListener listener);

    /// <summary>
    /// 通知所有已注册的监听器文件被读取。
    /// 对齐 TS: for (const listener of fileReadListeners.slice()) { listener(resolvedFilePath, content) }
    /// 使用快照遍历，防止回调中取消订阅导致跳过后续监听器。
    /// </summary>
    void Notify(FileReadEventArgs e);
}
