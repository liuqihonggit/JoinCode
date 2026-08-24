namespace Core.Agents.Doctor;

/// <summary>
/// 病人进程状态转换规则 — 集中定义 PatientState 所有合法转换
/// <para>原 PatientHandle 各方法内联直接赋值无校验，现统一提取为转换表</para>
/// <para>NotStarted 仅可转 Running，Running 可转 Completed/Failed/Hung/Killed，其余为终态</para>
/// </summary>
public static class PatientStateTransitions
{
    private static readonly FrozenDictionary<PatientState, FrozenSet<PatientState>> Transitions =
        new Dictionary<PatientState, FrozenSet<PatientState>>
        {
            [PatientState.NotStarted] = new HashSet<PatientState>
            {
                PatientState.Running
            }.ToFrozenSet(),

            [PatientState.Running] = new HashSet<PatientState>
            {
                PatientState.Completed,
                PatientState.Failed,
                PatientState.Hung,
                PatientState.Killed
            }.ToFrozenSet(),

            [PatientState.Completed] = FrozenSet<PatientState>.Empty,
            [PatientState.Failed] = FrozenSet<PatientState>.Empty,
            [PatientState.Hung] = FrozenSet<PatientState>.Empty,
            [PatientState.Killed] = FrozenSet<PatientState>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PatientState current, PatientState target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Completed/Failed/Hung/Killed 为终态
    /// </summary>
    public static bool IsTerminal(PatientState state) =>
        state is PatientState.Completed or PatientState.Failed or PatientState.Hung or PatientState.Killed;
}
