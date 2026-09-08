namespace JoinCode.Cli;

/// <summary>
/// Onboarding 步骤转换规则 — 集中定义 OnboardingStep 所有合法转换
/// <para>原 OnboardingFlowController 分散赋值 _currentStep,现统一提取为转换表</para>
/// <para>线性流程: Welcome→ApiKey→Security→TerminalSetup→Complete,支持前进和后退</para>
/// </summary>
public static class OnboardingStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)OnboardingStep，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;OnboardingStep, FrozenSet&lt;OnboardingStep&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Welcome=0 */ BitMask.Of(OnboardingStep.ApiKey),
        /* ApiKey=1 */ BitMask.Of(OnboardingStep.Welcome, OnboardingStep.Security),
        /* Security=2 */ BitMask.Of(OnboardingStep.ApiKey, OnboardingStep.TerminalSetup),
        /* TerminalSetup=3 */ BitMask.Of(OnboardingStep.Security, OnboardingStep.Complete),
        /* Complete=4 */ BitMask.Of(OnboardingStep.TerminalSetup)
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自-环合法
    /// </summary>
    public static bool CanTransitionTo(OnboardingStep current, OnboardingStep target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Complete 为终态
    /// </summary>
    public static bool IsTerminal(OnboardingStep state) => state == OnboardingStep.Complete;
}
