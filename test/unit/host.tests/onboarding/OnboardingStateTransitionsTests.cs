namespace Host.Tests.Onboarding;

/// <summary>
/// OnboardingStateTransitions 单元测试 — 验证 Onboarding 步骤转换规则正确性
/// </summary>
public sealed class OnboardingStateTransitionsTests
{
    [Fact]
    public void CanTransitionTo_ShouldAllowForwardFlow()
    {
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Welcome, OnboardingStep.ApiKey).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.ApiKey, OnboardingStep.Security).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Security, OnboardingStep.TerminalSetup).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.TerminalSetup, OnboardingStep.Complete).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowBackwardFlow()
    {
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.ApiKey, OnboardingStep.Welcome).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Security, OnboardingStep.ApiKey).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.TerminalSetup, OnboardingStep.Security).Should().BeTrue();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Complete, OnboardingStep.TerminalSetup).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenySkipSteps()
    {
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Welcome, OnboardingStep.Security).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Welcome, OnboardingStep.TerminalSetup).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Welcome, OnboardingStep.Complete).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.ApiKey, OnboardingStep.TerminalSetup).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.ApiKey, OnboardingStep.Complete).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyCompleteToWelcome()
    {
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Complete, OnboardingStep.Welcome).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Complete, OnboardingStep.ApiKey).Should().BeFalse();
        OnboardingStateTransitions.CanTransitionTo(OnboardingStep.Complete, OnboardingStep.Security).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<OnboardingStep>())
        {
            OnboardingStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }

    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForComplete()
    {
        OnboardingStateTransitions.IsTerminal(OnboardingStep.Complete).Should().BeTrue();
        OnboardingStateTransitions.IsTerminal(OnboardingStep.Welcome).Should().BeFalse();
        OnboardingStateTransitions.IsTerminal(OnboardingStep.ApiKey).Should().BeFalse();
        OnboardingStateTransitions.IsTerminal(OnboardingStep.Security).Should().BeFalse();
        OnboardingStateTransitions.IsTerminal(OnboardingStep.TerminalSetup).Should().BeFalse();
    }
}
