namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 运行时状态值对象 — 执行过程中可变,由 Consumer 线程独占访问,无需锁。
/// <para>从 ForkEntry 提取的可变运行时字段集合,与不可变的 <see cref="ForkIdentity"/> 分离。</para>
/// <para>状态转换经 <see cref="ForkStateTransitions.CanTransitionTo"/> 校验,非法转换抛 InvalidOperationException。</para>
/// <para>所有字段在 Fork 生命周期内可被 Consumer 线程反复读写(状态推进、结果填充、子代理分配、取消令牌替换)。</para>
/// </summary>
internal sealed class ForkRuntime
{
    private ForkState _state = ForkState.Running;

    /// <summary>Fork 状态 — setter 校验转换合法性,非法转换抛 InvalidOperationException</summary>
    public ForkState State
    {
        get => _state;
        set
        {
            if (!ForkStateTransitions.CanTransitionTo(_state, value))
            {
                throw new InvalidOperationException(
                    $"[FORK-ILLEGAL] 非法 Fork 状态转换: {_state} → {value}");
            }
            _state = value;
        }
    }

    /// <summary>Fork 子代理执行结果文本(完成后填充,未完成为 null)</summary>
    public string? Result;

    /// <summary>子代理标识(分配后填充,未分配为 null)</summary>
    public string? AgentId;

    /// <summary>进度跟踪器实例(可选),用于上报子代理执行进度</summary>
    public ProgressTracker? ProgressTracker;

    /// <summary>取消令牌源,用于外部取消该 Fork 子代理的执行</summary>
    public CancellationTokenSource? Cts;
}
