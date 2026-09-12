namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件写入事件参数 — 文件被修改/创建/删除时触发
/// </summary>
public sealed record FileWriteEventArgs
{
    /// <summary>
    /// 被修改文件的已解析绝对路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 写入操作类型（write/edit/delete/insert/delete_lines/batch_edit/apply_patch）
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// 执行写入的 Agent ID（从 ISubAgentContextAccessor 获取）
    /// </summary>
    public required string AgentId { get; init; }
}

/// <summary>
/// 文件写入监听器接口 — 文件被修改/创建/删除时通知
/// </summary>
public interface IFileWriteListener
{
    /// <summary>
    /// 当文件被成功写入/编辑/删除时调用
    /// </summary>
    void OnFileWrite(FileWriteEventArgs e);
}

/// <summary>
/// 文件写入监听器注册中心 — 线程安全，支持注册/取消订阅
/// </summary>
public interface IFileWriteListenerRegistry : IRegistry
{
    /// <summary>
    /// 注册文件写入监听器，返回取消订阅的 token
    /// </summary>
    IDisposable Register(IFileWriteListener listener);

    /// <summary>
    /// 通知所有已注册的监听器文件被写入
    /// </summary>
    void Notify(FileWriteEventArgs e);
}
