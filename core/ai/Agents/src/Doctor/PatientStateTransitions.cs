namespace Core.Agents.Doctor;

/// <summary>
/// 病人进程状态转换规则 — 集中定义 PatientState 所有合法转换
/// <para>原 PatientHandle 各方法内联直接赋值无校验，现统一提取为转换表</para>
/// <para>NotStarted 仅可转 Running，Running 可转 Completed/Failed/Hung/Killed，其余为终态</para>
/// </summary>
public static class PatientStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)PatientState，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;PatientState, FrozenSet&lt;PatientState&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* NotStarted=0 */ BitMask.Of(PatientState.Running),
        /* Running=1 */ BitMask.Of(PatientState.Completed, PatientState.Failed, PatientState.Hung, PatientState.Killed),
        /* Completed=2 */ 0,
        /* Failed=3 */ 0,
        /* Hung=4 */ 0,
        /* Killed=5 */ 0
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PatientState current, PatientState target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Completed/Failed/Hung/Killed 为终态
    /// </summary>
    public static bool IsTerminal(PatientState state) =>
        state is PatientState.Completed or PatientState.Failed or PatientState.Hung or PatientState.Killed;
}
