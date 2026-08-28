namespace Infrastructure.Network.Downloader.StateMachine;

/// <summary>
/// 下载状态机 — 转换表 + 守卫 + 事件枚举（ADR 0040 企业级状态机）
/// <para>行为流程：获取当前状态 → 查表 → 守卫判定 → 执行动作 → 转移</para>
/// <para>转换表显式表达所有合法转换，非法 (FromState, Event) 组合返回 NoRule</para>
/// <para>每个操作有精确前置状态,非法操作返回 Success=false + Error=[DOWN001]</para>
/// </summary>
internal sealed class DownloadStateMachine
{
    private static readonly FrozenDictionary<TransitionKey<DownloadState, DownloadOperation>, TransitionRule<DownloadState>> Table = CreateTable();
    private readonly Fsm<DownloadState, DownloadOperation> _fsm;

    public DownloadStateMachine()
    {
        _fsm = new Fsm<DownloadState, DownloadOperation>(Table, DownloadState.Idle);
    }

    /// <summary>当前状态(线程安全读取)</summary>
    public DownloadState State => _fsm.CurrentState;

    /// <summary>尝试启动:仅 Idle → Downloading</summary>
    public DownloadStateTransition TryStart() => Trigger(DownloadOperation.Start);

    /// <summary>尝试暂停:仅 Downloading → Paused</summary>
    public DownloadStateTransition TryPause() => Trigger(DownloadOperation.Pause);

    /// <summary>尝试继续:仅 Paused → Downloading</summary>
    public DownloadStateTransition TryResume() => Trigger(DownloadOperation.Resume);

    /// <summary>尝试进入合并:仅 Downloading → Merging</summary>
    public DownloadStateTransition TryEnterMerging() => Trigger(DownloadOperation.EnterMerging);

    /// <summary>尝试完成合并:仅 Merging → Completed</summary>
    public DownloadStateTransition TryComplete() => Trigger(DownloadOperation.Complete);

    /// <summary>尝试取消:任意非终态 → Cancelled</summary>
    public DownloadStateTransition TryCancel() => Trigger(DownloadOperation.Cancel);

    /// <summary>尝试失败:仅 Downloading/Paused/Merging → Failed</summary>
    public DownloadStateTransition TryFail() => Trigger(DownloadOperation.Fail);

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
    internal void ForceSet(DownloadState state) => _fsm.ForceSet(state);

    private DownloadStateTransition Trigger(DownloadOperation op)
    {
        var result = _fsm.Trigger(op);
        if (result.Transitioned)
            return new DownloadStateTransition(true, result.FromState, result.ToState, null);

        var error = $"[DOWN001] 非法操作 {op} 从 {result.FromState} 状态";
        return new DownloadStateTransition(false, result.FromState, result.FromState, error);
    }

    private static FrozenDictionary<TransitionKey<DownloadState, DownloadOperation>, TransitionRule<DownloadState>> CreateTable()
    {
        return new Dictionary<TransitionKey<DownloadState, DownloadOperation>, TransitionRule<DownloadState>>
        {
            [new(DownloadState.Idle, DownloadOperation.Start)] = new(DownloadState.Downloading),
            [new(DownloadState.Downloading, DownloadOperation.Pause)] = new(DownloadState.Paused),
            [new(DownloadState.Paused, DownloadOperation.Resume)] = new(DownloadState.Downloading),
            [new(DownloadState.Downloading, DownloadOperation.EnterMerging)] = new(DownloadState.Merging),
            [new(DownloadState.Merging, DownloadOperation.Complete)] = new(DownloadState.Completed),

            [new(DownloadState.Idle, DownloadOperation.Cancel)] = new(DownloadState.Cancelled),
            [new(DownloadState.Downloading, DownloadOperation.Cancel)] = new(DownloadState.Cancelled),
            [new(DownloadState.Paused, DownloadOperation.Cancel)] = new(DownloadState.Cancelled),
            [new(DownloadState.Merging, DownloadOperation.Cancel)] = new(DownloadState.Cancelled),

            [new(DownloadState.Downloading, DownloadOperation.Fail)] = new(DownloadState.Failed),
            [new(DownloadState.Paused, DownloadOperation.Fail)] = new(DownloadState.Failed),
            [new(DownloadState.Merging, DownloadOperation.Fail)] = new(DownloadState.Failed),
        }.ToFrozenDictionary();
    }
}
