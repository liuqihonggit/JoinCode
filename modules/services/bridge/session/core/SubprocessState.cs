namespace Core.Bridge;

/// <summary>
/// 子进程状态 — 封装退出 Promise、释放标志、强制杀死标志、首条用户消息标志
/// 从 BridgeSubprocessHandle 提取,纯状态转换无 IO 依赖
/// </summary>
internal sealed class SubprocessState {
    private readonly TaskCompletionSource<BridgeSubprocessStatus> _doneTcs = new();
    private bool _disposed;
    private int _sigkillSent;
    private bool _firstUserMessageSeen;

    /// <summary>进程退出 Promise — 对齐 TS 端 done</summary>
    public Task<BridgeSubprocessStatus> Done => _doneTcs.Task;

    /// <summary>是否已异步释放</summary>
    public bool IsDisposed => _disposed;

    /// <summary>是否已见到首条用户消息</summary>
    public bool FirstUserMessageSeen => _firstUserMessageSeen;

    /// <summary>设置进程退出状态（幂等）</summary>
    public void TrySetDone(BridgeSubprocessStatus status) => _doneTcs.TrySetResult(status);

    /// <summary>标记已见到首条用户消息</summary>
    public void MarkFirstUserMessageSeen() => _firstUserMessageSeen = true;

    /// <summary>原子标记强制杀死已发送 — 返回 true 表示首次调用,false 表示已发送过</summary>
    public bool TryMarkSigkillSent() => Interlocked.Exchange(ref _sigkillSent, 1) == 0;

    /// <summary>标记已释放 — 返回 true 表示首次调用(应继续释放),false 表示已释放(应跳过)</summary>
    public bool MarkDisposed() => !_disposed && (_disposed = true);
}