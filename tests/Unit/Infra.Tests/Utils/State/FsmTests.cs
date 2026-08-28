namespace Infra.Tests.Utils.State;

using System.Collections.Frozen;
using JoinCode.Abstractions.Utils;

public enum FsmTestState
{
    Idle,
    Running,
    Paused,
    Completed,
    Faulted
}

public enum FsmTestEvent
{
    Start,
    Pause,
    Resume,
    Complete,
    Fail,
    Reset
}

public sealed class FsmTestContext : FsmContext
{
    public int ConsecutiveFailures;
    public bool ActionInvoked;
}

public class FsmTests
{
    private static FrozenDictionary<TransitionKey<FsmTestState, FsmTestEvent>, TransitionRule<FsmTestState>> CreateTransitionTable(
        TransitionGuard? completeGuard = null,
        TransitionAction? startAction = null)
    {
        var table = new Dictionary<TransitionKey<FsmTestState, FsmTestEvent>, TransitionRule<FsmTestState>>
        {
            [new(FsmTestState.Idle, FsmTestEvent.Start)] = new(FsmTestState.Running, Action: startAction),
            [new(FsmTestState.Running, FsmTestEvent.Pause)] = new(FsmTestState.Paused),
            [new(FsmTestState.Running, FsmTestEvent.Complete)] = new(FsmTestState.Completed, Guard: completeGuard),
            [new(FsmTestState.Running, FsmTestEvent.Fail)] = new(FsmTestState.Faulted),
            [new(FsmTestState.Paused, FsmTestEvent.Resume)] = new(FsmTestState.Running),
            [new(FsmTestState.Paused, FsmTestEvent.Fail)] = new(FsmTestState.Faulted),
            [new(FsmTestState.Faulted, FsmTestEvent.Reset)] = new(FsmTestState.Idle),
        };

        return table.ToFrozenDictionary();
    }

    [Fact]
    public void CurrentState_ShouldBeInitial()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.CurrentState.Should().Be(FsmTestState.Idle);
    }

    [Fact]
    public void Trigger_ValidEvent_ShouldTransition()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        var result = fsm.Trigger(FsmTestEvent.Start);
        result.Transitioned.Should().BeTrue();
        result.FromState.Should().Be(FsmTestState.Idle);
        result.ToState.Should().Be(FsmTestState.Running);
        result.Event.Should().Be(FsmTestEvent.Start);
        result.Outcome.Should().Be(TransitionOutcome.Transitioned);
        fsm.CurrentState.Should().Be(FsmTestState.Running);
    }

    [Fact]
    public void Trigger_NoRule_ShouldReturnNoRule()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        var result = fsm.Trigger(FsmTestEvent.Pause);
        result.Transitioned.Should().BeFalse();
        result.Outcome.Should().Be(TransitionOutcome.NoRule);
        result.FromState.Should().Be(FsmTestState.Idle);
        result.ToState.Should().Be(FsmTestState.Idle);
        fsm.CurrentState.Should().Be(FsmTestState.Idle);
    }

    [Fact]
    public void Trigger_GuardFailed_ShouldReturnGuardFailed()
    {
        var guard = new TransitionGuard(_ => false);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var result = fsm.Trigger(FsmTestEvent.Complete);
        result.Transitioned.Should().BeFalse();
        result.Outcome.Should().Be(TransitionOutcome.GuardFailed);
        fsm.CurrentState.Should().Be(FsmTestState.Running);
    }

    [Fact]
    public void Trigger_GuardPassed_ShouldTransition()
    {
        var guard = new TransitionGuard(_ => true);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var result = fsm.Trigger(FsmTestEvent.Complete);
        result.Transitioned.Should().BeTrue();
        fsm.CurrentState.Should().Be(FsmTestState.Completed);
    }

    [Fact]
    public void Trigger_GuardWithContext_ShouldEvaluateContext()
    {
        var ctx = new FsmTestContext { ConsecutiveFailures = 10 };
        var guard = new TransitionGuard(c => c is FsmTestContext tc && tc.ConsecutiveFailures >= 5);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var result = fsm.Trigger(FsmTestEvent.Complete, ctx);
        result.Transitioned.Should().BeTrue();
    }

    [Fact]
    public void Trigger_GuardWithContext_Failed_ShouldNotTransition()
    {
        var ctx = new FsmTestContext { ConsecutiveFailures = 2 };
        var guard = new TransitionGuard(c => c is FsmTestContext tc && tc.ConsecutiveFailures >= 5);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var result = fsm.Trigger(FsmTestEvent.Complete, ctx);
        result.Transitioned.Should().BeFalse();
        result.Outcome.Should().Be(TransitionOutcome.GuardFailed);
    }

    [Fact]
    public void Trigger_Action_ShouldInvokeAfterTransition()
    {
        var ctx = new FsmTestContext();
        var action = new TransitionAction(c => ((FsmTestContext)c!).ActionInvoked = true);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(startAction: action), FsmTestState.Idle);
        fsm.Trigger(FsmTestEvent.Start, ctx);
        ctx.ActionInvoked.Should().BeTrue();
    }

    [Fact]
    public void Trigger_FaultedState_ShouldTransitionToFaulted()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Running);
        var result = fsm.Trigger(FsmTestEvent.Fail);
        result.Transitioned.Should().BeTrue();
        result.ToState.Should().Be(FsmTestState.Faulted);
        fsm.CurrentState.Should().Be(FsmTestState.Faulted);
    }

    [Fact]
    public void Trigger_FromFaulted_Reset_ShouldReturnToIdle()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Faulted);
        var result = fsm.Trigger(FsmTestEvent.Reset);
        result.Transitioned.Should().BeTrue();
        fsm.CurrentState.Should().Be(FsmTestState.Idle);
    }

    [Fact]
    public void TryTrigger_Valid_ShouldReturnTrue()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.TryTrigger(FsmTestEvent.Start).Should().BeTrue();
        fsm.CurrentState.Should().Be(FsmTestState.Running);
    }

    [Fact]
    public void TryTrigger_Invalid_ShouldReturnFalse()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.TryTrigger(FsmTestEvent.Pause).Should().BeFalse();
        fsm.CurrentState.Should().Be(FsmTestState.Idle);
    }

    [Fact]
    public void CanTrigger_WithGuard_ShouldEvaluateGuard()
    {
        var guard = new TransitionGuard(_ => false);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        fsm.CanTrigger(FsmTestEvent.Complete).Should().BeFalse();
        fsm.CanTrigger(FsmTestEvent.Pause).Should().BeTrue();
    }

    [Fact]
    public void CanTrigger_NoRule_ShouldReturnFalse()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.CanTrigger(FsmTestEvent.Pause).Should().BeFalse();
    }

    [Fact]
    public void GetAvailableEvents_ShouldReturnGuardedEvents()
    {
        var guard = new TransitionGuard(_ => false);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var events = fsm.GetAvailableEvents();
        events.Should().Contain(FsmTestEvent.Pause);
        events.Should().Contain(FsmTestEvent.Fail);
        events.Should().NotContain(FsmTestEvent.Complete);
    }

    [Fact]
    public void StateChanged_ShouldFireOnTransition()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        TransitionResult<FsmTestState, FsmTestEvent>? captured = null;
        fsm.StateChanged += (_, args) => captured = args;
        fsm.Trigger(FsmTestEvent.Start);
        captured.Should().NotBeNull();
        captured!.FromState.Should().Be(FsmTestState.Idle);
        captured.ToState.Should().Be(FsmTestState.Running);
    }

    [Fact]
    public void StateChanged_ShouldNotFireOnNoRule()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        var fired = false;
        fsm.StateChanged += (_, _) => fired = true;
        fsm.Trigger(FsmTestEvent.Pause);
        fired.Should().BeFalse();
    }

    [Fact]
    public void StateChanged_ShouldNotFireOnGuardFailed()
    {
        var guard = new TransitionGuard(_ => false);
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(completeGuard: guard), FsmTestState.Running);
        var fired = false;
        fsm.StateChanged += (_, _) => fired = true;
        fsm.Trigger(FsmTestEvent.Complete);
        fired.Should().BeFalse();
    }

    [Fact]
    public void ForceSet_ShouldTransitionWithoutValidation()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.ForceSet(FsmTestState.Completed);
        fsm.CurrentState.Should().Be(FsmTestState.Completed);
    }

    [Fact]
    public void ForceSet_SameState_ShouldNotFireEvent()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        var fired = false;
        fsm.StateChanged += (_, _) => fired = true;
        fsm.ForceSet(FsmTestState.Idle);
        fired.Should().BeFalse();
    }

    [Fact]
    public void Reset_ShouldSetStateWithoutEvent()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        var fired = false;
        fsm.StateChanged += (_, _) => fired = true;
        fsm.Reset(FsmTestState.Faulted);
        fsm.CurrentState.Should().Be(FsmTestState.Faulted);
        fired.Should().BeFalse();
    }

    [Fact]
    public void MultipleTransitions_ShouldWorkCorrectly()
    {
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(CreateTransitionTable(), FsmTestState.Idle);
        fsm.Trigger(FsmTestEvent.Start);
        fsm.Trigger(FsmTestEvent.Pause);
        fsm.Trigger(FsmTestEvent.Resume);
        fsm.Trigger(FsmTestEvent.Complete);
        fsm.CurrentState.Should().Be(FsmTestState.Completed);
    }

    [Fact]
    public void TransitionKey_Equality_ShouldWork()
    {
        var k1 = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Start);
        var k2 = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Start);
        var k3 = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Start);
        k1.Should().Be(k2);
        k1.Should().NotBe(k3);
        k1.GetHashCode().Should().Be(k2.GetHashCode());
    }
}
