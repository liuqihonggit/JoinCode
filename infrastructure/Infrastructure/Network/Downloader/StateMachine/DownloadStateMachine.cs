namespace Infrastructure.Network.Downloader.StateMachine;

/// <summary>
/// 下载状态机 — 按操作校验前置状态,线程安全
/// <para>实现风格遵循 AGENTS.md 规则8:显式枚举 + switch 表达式,不用隐式 if-else + 标志变量</para>
/// <para>线程安全:State 用 volatile 读,转换用 lock(防 Pause 和 Cancel 并发竞争)</para>
/// <para>每个操作有精确前置条件(见 IsAllowed),非法操作返回 Success=false + Error=[DOWN001]</para>
/// </summary>
internal sealed class DownloadStateMachine
{
    private readonly object _lock = new();
    private volatile DownloadState _state = DownloadState.Idle;

    /// <summary>当前状态(线程安全读取)</summary>
    public DownloadState State => _state;

    /// <summary>尝试启动:仅 Idle → Downloading</summary>
    public DownloadStateTransition TryStart() => Transition(DownloadState.Downloading, DownloadOperation.Start);

    /// <summary>尝试暂停:仅 Downloading → Paused</summary>
    public DownloadStateTransition TryPause() => Transition(DownloadState.Paused, DownloadOperation.Pause);

    /// <summary>尝试继续:仅 Paused → Downloading</summary>
    public DownloadStateTransition TryResume() => Transition(DownloadState.Downloading, DownloadOperation.Resume);

    /// <summary>尝试进入合并:仅 Downloading → Merging</summary>
    public DownloadStateTransition TryEnterMerging() => Transition(DownloadState.Merging, DownloadOperation.EnterMerging);

    /// <summary>尝试完成合并:仅 Merging → Completed</summary>
    public DownloadStateTransition TryComplete() => Transition(DownloadState.Completed, DownloadOperation.Complete);

    /// <summary>尝试取消:任意非终态 → Cancelled</summary>
    public DownloadStateTransition TryCancel() => Transition(DownloadState.Cancelled, DownloadOperation.Cancel);

    /// <summary>尝试失败:仅 Downloading/Paused/Merging → Failed</summary>
    public DownloadStateTransition TryFail() => Transition(DownloadState.Failed, DownloadOperation.Fail);

    /// <summary>
    /// 统一转换入口 — 按操作枚举分发到具体方法,消除消费方硬编码字符串
    /// </summary>
    public DownloadStateTransition TryTransition(DownloadOperation op) =>
        op switch
        {
            DownloadOperation.Start => TryStart(),
            DownloadOperation.Pause => TryPause(),
            DownloadOperation.Resume => TryResume(),
            DownloadOperation.EnterMerging => TryEnterMerging(),
            DownloadOperation.Complete => TryComplete(),
            DownloadOperation.Cancel => TryCancel(),
            DownloadOperation.Fail => TryFail(),
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "[DOWN006] 未知下载操作")
        };

    /// <summary>强制设置状态(仅用于测试/恢复场景,跳过校验)</summary>
    internal void ForceSet(DownloadState state)
    {
        lock (_lock)
        {
            _state = state;
        }
    }

    private DownloadStateTransition Transition(DownloadState desired, DownloadOperation op)
    {
        lock (_lock)
        {
            var current = _state;
            if (IsAllowed(current, op))
            {
                _state = desired;
                return new DownloadStateTransition(true, current, desired, null);
            }
            var error = $"[DOWN001] 非法操作 {op} 从 {current} 状态";
            return new DownloadStateTransition(false, current, current, error);
        }
    }

    /// <summary>
    /// 校验操作在当前状态下是否合法 — switch 表达式按操作定义前置条件(符合规则8)
    /// <para>每个操作有精确前置状态,消除 Start/Resume 都转 Downloading 的歧义</para>
    /// </summary>
    private static bool IsAllowed(DownloadState current, DownloadOperation op) =>
        op switch
        {
            DownloadOperation.Start => current == DownloadState.Idle,
            DownloadOperation.Pause => current == DownloadState.Downloading,
            DownloadOperation.Resume => current == DownloadState.Paused,
            DownloadOperation.EnterMerging => current == DownloadState.Downloading,
            DownloadOperation.Complete => current == DownloadState.Merging,
            DownloadOperation.Cancel => current is not (DownloadState.Completed
                or DownloadState.Cancelled
                or DownloadState.Failed),
            DownloadOperation.Fail => current is DownloadState.Downloading
                or DownloadState.Paused
                or DownloadState.Merging,
            _ => false
        };
}
