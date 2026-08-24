namespace JoinCode.Cli;

/// <summary>
/// Onboarding 步骤转换规则 — 集中定义 OnboardingStep 所有合法转换
/// <para>原 OnboardingFlowController 分散赋值 _currentStep,现统一提取为转换表</para>
/// <para>线性流程: Welcome→ApiKey→Security→TerminalSetup→Complete,支持前进和后退</para>
/// </summary>
public static class OnboardingStateTransitions
{
    private static readonly FrozenDictionary<OnboardingStep, FrozenSet<OnboardingStep>> Transitions =
        new Dictionary<OnboardingStep, FrozenSet<OnboardingStep>>
        {
            [OnboardingStep.Welcome] = new HashSet<OnboardingStep>
            {
                OnboardingStep.ApiKey
            }.ToFrozenSet(),

            [OnboardingStep.ApiKey] = new HashSet<OnboardingStep>
            {
                OnboardingStep.Welcome,
                OnboardingStep.Security
            }.ToFrozenSet(),

            [OnboardingStep.Security] = new HashSet<OnboardingStep>
            {
                OnboardingStep.ApiKey,
                OnboardingStep.TerminalSetup
            }.ToFrozenSet(),

            [OnboardingStep.TerminalSetup] = new HashSet<OnboardingStep>
            {
                OnboardingStep.Security,
                OnboardingStep.Complete
            }.ToFrozenSet(),

            [OnboardingStep.Complete] = new HashSet<OnboardingStep>
            {
                OnboardingStep.TerminalSetup
            }.ToFrozenSet()
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自-环合法
    /// </summary>
    public static bool CanTransitionTo(OnboardingStep current, OnboardingStep target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Complete 为终态
    /// </summary>
    public static bool IsTerminal(OnboardingStep state) => state == OnboardingStep.Complete;
}
