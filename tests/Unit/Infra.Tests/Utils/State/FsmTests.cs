namespace Infra.Tests.Utils.State;


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

    [Fact]
    public void TransitionKey_CompareTo_ShouldOrderByFromThenEvent()
    {
        var a = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Start);
        var b = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Pause);
        var c = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Start);

        a.CompareTo(b).Should().BeLessThan(0, "Idle+Start < Idle+Pause (Event 升序)");
        a.CompareTo(c).Should().BeLessThan(0, "Idle+Start < Running+Start (From 升序)");
        b.CompareTo(c).Should().BeLessThan(0, "Idle+Pause < Running+Start (From 优先于 Event)");
        a.CompareTo(a).Should().Be(0, "自反性");
    }

    [Fact]
    public void ArrayConstructor_ShouldWorkSameAsDictionaryConstructor()
    {
        var table = CreateTransitionTable();
        var fsmFromDict = new Fsm<FsmTestState, FsmTestEvent>(table, FsmTestState.Idle);

        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        var sortedKeys = pairs.Select(p => p.Key).ToArray();
        var rules = pairs.Select(p => p.Value).ToArray();
        var fsmFromArray = new Fsm<FsmTestState, FsmTestEvent>(sortedKeys, rules, FsmTestState.Idle);

        fsmFromDict.CurrentState.Should().Be(fsmFromArray.CurrentState);

        var r1 = fsmFromDict.Trigger(FsmTestEvent.Start);
        var r2 = fsmFromArray.Trigger(FsmTestEvent.Start);
        r1.Should().BeEquivalentTo(r2);
        fsmFromDict.CurrentState.Should().Be(fsmFromArray.CurrentState);

        var r3 = fsmFromDict.Trigger(FsmTestEvent.Complete);
        var r4 = fsmFromArray.Trigger(FsmTestEvent.Complete);
        r3.Should().BeEquivalentTo(r4);
    }

    [Fact]
    public void BinarySearch_ShouldFindExistingKey()
    {
        var keys = new[]
        {
            new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Start),
            new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Fail),
            new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Pause),
        };
        Array.Sort(keys);

        var searchKey = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Pause);
        var idx = Array.BinarySearch(keys, searchKey);
        idx.Should().BeGreaterThanOrEqualTo(0, "存在的 key 应找到非负索引");
        keys[idx].Should().Be(searchKey);
    }

    [Fact]
    public void BinarySearch_ShouldReturnNegativeForMissingKey()
    {
        var keys = new[]
        {
            new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Idle, FsmTestEvent.Start),
            new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Running, FsmTestEvent.Pause),
        };
        Array.Sort(keys);

        var missingKey = new TransitionKey<FsmTestState, FsmTestEvent>(FsmTestState.Paused, FsmTestEvent.Start);
        var idx = Array.BinarySearch(keys, missingKey);
        idx.Should().BeLessThan(0, "不存在的 key 应返回负数（按位补码）");
    }

    [Fact]
    public void SortedKeysArray_ShouldBeOrderedByCompareTo()
    {
        var table = CreateTransitionTable();
        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        var sortedKeys = pairs.Select(p => p.Key).ToArray();

        for (var i = 1; i < sortedKeys.Length; i++)
            sortedKeys[i - 1].CompareTo(sortedKeys[i]).Should().BeLessThanOrEqualTo(0,
                "排序数组每相邻元素应非降序");
    }

    [Fact]
    public void ArrayConstructor_WithGuard_ShouldWork()
    {
        var ctx = new FsmTestContext { ConsecutiveFailures = 5 };
        var guard = new TransitionGuard(c => ((FsmTestContext)c!).ConsecutiveFailures >= 3);

        var table = new Dictionary<TransitionKey<FsmTestState, FsmTestEvent>, TransitionRule<FsmTestState>>
        {
            [new(FsmTestState.Running, FsmTestEvent.Complete)] = new(FsmTestState.Completed, guard),
        }.ToFrozenDictionary();

        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(
            pairs.Select(p => p.Key).ToArray(),
            pairs.Select(p => p.Value).ToArray(),
            FsmTestState.Running);

        var result = fsm.Trigger(FsmTestEvent.Complete, ctx);
        result.Transitioned.Should().BeTrue("守卫通过（ConsecutiveFailures=5 >= 3）");
        result.ToState.Should().Be(FsmTestState.Completed);
    }

    [Fact]
    public void ArrayConstructor_WithAction_ShouldInvokeAction()
    {
        var ctx = new FsmTestContext();
        var action = new TransitionAction(c => ((FsmTestContext)c!).ActionInvoked = true);

        var table = new Dictionary<TransitionKey<FsmTestState, FsmTestEvent>, TransitionRule<FsmTestState>>
        {
            [new(FsmTestState.Idle, FsmTestEvent.Start)] = new(FsmTestState.Running, Action: action),
        }.ToFrozenDictionary();

        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(
            pairs.Select(p => p.Key).ToArray(),
            pairs.Select(p => p.Value).ToArray(),
            FsmTestState.Idle);

        fsm.Trigger(FsmTestEvent.Start, ctx);
        ctx.ActionInvoked.Should().BeTrue("Action 应在转换后执行");
    }

    [Fact]
    public void GetAvailableEvents_WithArrayBackend_ShouldReturnGuardedEvents()
    {
        var ctx = new FsmTestContext { ConsecutiveFailures = 1 };
        var guard = new TransitionGuard(c => ((FsmTestContext)c!).ConsecutiveFailures >= 3);

        var table = new Dictionary<TransitionKey<FsmTestState, FsmTestEvent>, TransitionRule<FsmTestState>>
        {
            [new(FsmTestState.Running, FsmTestEvent.Pause)] = new(FsmTestState.Paused),
            [new(FsmTestState.Running, FsmTestEvent.Complete)] = new(FsmTestState.Completed, guard),
            [new(FsmTestState.Running, FsmTestEvent.Fail)] = new(FsmTestState.Faulted),
        }.ToFrozenDictionary();

        var pairs = table.OrderBy(kvp => kvp.Key).ToArray();
        var fsm = new Fsm<FsmTestState, FsmTestEvent>(
            pairs.Select(p => p.Key).ToArray(),
            pairs.Select(p => p.Value).ToArray(),
            FsmTestState.Running);

        var events = fsm.GetAvailableEvents(ctx);
        events.Should().Contain(new[] { FsmTestEvent.Pause, FsmTestEvent.Fail });
        events.Should().NotContain(FsmTestEvent.Complete, "守卫未通过（ConsecutiveFailures=1 < 3）");
    }
}
