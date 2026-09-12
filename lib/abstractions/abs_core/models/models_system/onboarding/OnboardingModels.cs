namespace JoinCode.Abstractions.Onboarding;

/// <summary>
/// Onboarding 流程步骤枚举
/// </summary>
public enum OnboardingStep
{
    Welcome,
    ApiKey,
    Security,
    TerminalSetup,
    Complete
}

/// <summary>
/// Onboarding 状态
/// </summary>
public sealed class OnboardingState
{
    /// <summary>
    /// 当前步骤
    /// </summary>
    public OnboardingStep CurrentStep { get; init; } = OnboardingStep.Welcome;

    /// <summary>
    /// 当前步骤索引
    /// </summary>
    public int CurrentStepIndex { get; init; }

    /// <summary>
    /// 总步骤数
    /// </summary>
    public int TotalSteps { get; init; } = 5;
}

/// <summary>
/// Onboarding 状态变更事件参数
/// </summary>
public sealed class OnboardingStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 变更前的步骤
    /// </summary>
    public OnboardingStep PreviousStep { get; init; }

    /// <summary>
    /// 变更后的步骤
    /// </summary>
    public OnboardingStep CurrentStep { get; init; }
}
