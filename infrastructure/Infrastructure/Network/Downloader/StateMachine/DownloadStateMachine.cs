namespace Infrastructure.Network.Downloader.StateMachine;

/// <summary>
/// 下载状态机 — 转换表 + 守卫 + 事件枚举（ADR 0040/0041 源码生成器）
/// <para>转换表由 Fsm.Generator 扫描 [Transition] 特性生成（_fsmTable + FsmDispatchEvent）</para>
/// <para>每事件独立 C# event: OnStart/OnPause/OnResume/OnEnterMerging/OnComplete/OnCancel/OnFail</para>
/// <para>每个操作有精确前置状态,非法操作返回 Success=false + Error=[DOWN001]</para>
/// </summary>
[FsmStateMachine(typeof(DownloadState), typeof(DownloadOperation), DownloadState.Idle)]
[Transition(DownloadState.Idle, DownloadOperation.Start, DownloadState.Downloading)]
[Transition(DownloadState.Downloading, DownloadOperation.Pause, DownloadState.Paused)]
[Transition(DownloadState.Paused, DownloadOperation.Resume, DownloadState.Downloading)]
[Transition(DownloadState.Downloading, DownloadOperation.EnterMerging, DownloadState.Merging)]
[Transition(DownloadState.Merging, DownloadOperation.Complete, DownloadState.Completed)]
[Transition(DownloadState.Idle, DownloadOperation.Cancel, DownloadState.Cancelled)]
[Transition(DownloadState.Downloading, DownloadOperation.Cancel, DownloadState.Cancelled)]
[Transition(DownloadState.Paused, DownloadOperation.Cancel, DownloadState.Cancelled)]
[Transition(DownloadState.Merging, DownloadOperation.Cancel, DownloadState.Cancelled)]
[Transition(DownloadState.Downloading, DownloadOperation.Fail, DownloadState.Failed)]
[Transition(DownloadState.Paused, DownloadOperation.Fail, DownloadState.Failed)]
[Transition(DownloadState.Merging, DownloadOperation.Fail, DownloadState.Failed)]
internal sealed partial class DownloadStateMachine
{
    private readonly Fsm<DownloadState, DownloadOperation> _fsm;

    public DownloadStateMachine()
    {
        _fsm = new Fsm<DownloadState, DownloadOperation>(_fsmSortedKeys, _fsmRules, DownloadState.Idle);
        _fsm.StateChanged += (_, e) => FsmDispatchEvent(e);
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
}
