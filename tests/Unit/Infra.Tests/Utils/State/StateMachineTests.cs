namespace Infra.Tests.Utils.State;

using System.Collections.Frozen;
using JoinCode.Abstractions.Clock;

public enum TestState
{
    Idle,
    Running,
    Paused,
    Completed,
    Failed
}

public class StateMachineTests
{
    private static StateMachine<TestState> CreateStateMachine(TestState initialState = TestState.Idle)
    {
        var transitions = new Dictionary<TestState, FrozenSet<TestState>>
        {
            [TestState.Idle] = new HashSet<TestState> { TestState.Running }.ToFrozenSet(),
            [TestState.Running] = new HashSet<TestState> { TestState.Paused, TestState.Completed, TestState.Failed }.ToFrozenSet(),
            [TestState.Paused] = new HashSet<TestState> { TestState.Running, TestState.Failed }.ToFrozenSet(),
            [TestState.Completed] = new HashSet<TestState> { TestState.Idle }.ToFrozenSet(),
            [TestState.Failed] = new HashSet<TestState> { TestState.Idle }.ToFrozenSet()
        }.ToFrozenDictionary();

        return new StateMachine<TestState>(transitions, initialState);
    }

    [Fact]
    public void CurrentState_Default_ShouldBeInitial()
    {
        var sm = CreateStateMachine();
        sm.CurrentState.Should().Be(TestState.Idle);
    }

    [Fact]
    public void CurrentState_CustomInitial_ShouldBeSet()
    {
        var sm = CreateStateMachine(TestState.Running);
        sm.CurrentState.Should().Be(TestState.Running);
    }

    [Fact]
    public void CanTransitionTo_ValidTransition_ShouldReturnTrue()
    {
        var sm = CreateStateMachine();
        sm.CanTransitionTo(TestState.Idle, TestState.Running).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_InvalidTransition_ShouldReturnFalse()
    {
        var sm = CreateStateMachine();
        sm.CanTransitionTo(TestState.Idle, TestState.Completed).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_SameState_ShouldReturnTrue()
    {
        var sm = CreateStateMachine();
        sm.CanTransitionTo(TestState.Idle, TestState.Idle).Should().BeTrue();
        sm.CanTransitionTo(TestState.Running, TestState.Running).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_SingleArg_Valid_ShouldReturnTrue()
    {
        var sm = CreateStateMachine();
        sm.CanTransitionTo(TestState.Running).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_SingleArg_Invalid_ShouldReturnFalse()
    {
        var sm = CreateStateMachine();
        sm.CanTransitionTo(TestState.Completed).Should().BeFalse();
    }

    [Fact]
    public void TransitionTo_ValidTransition_ShouldSucceed()
    {
        var sm = CreateStateMachine();
        sm.TransitionTo(TestState.Running);
        sm.CurrentState.Should().Be(TestState.Running);
    }

    [Fact]
    public void TransitionTo_InvalidTransition_ShouldThrow()
    {
        var sm = CreateStateMachine();
        var act = () => sm.TransitionTo(TestState.Completed);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Idle*Completed*");
    }

    [Fact]
    public void TryTransitionTo_ValidTransition_ShouldReturnTrue()
    {
        var sm = CreateStateMachine();
        sm.TryTransitionTo(TestState.Running).Should().BeTrue();
        sm.CurrentState.Should().Be(TestState.Running);
    }

    [Fact]
    public void TryTransitionTo_InvalidTransition_ShouldReturnFalse()
    {
        var sm = CreateStateMachine();
        sm.TryTransitionTo(TestState.Completed).Should().BeFalse();
        sm.CurrentState.Should().Be(TestState.Idle);
    }

    [Fact]
    public void TryTransitionTo_SameState_ShouldReturnTrue()
    {
        var sm = CreateStateMachine();
        sm.TryTransitionTo(TestState.Idle).Should().BeTrue();
    }

    [Fact]
    public void ForceTransitionTo_ShouldTransitionWithoutValidation()
    {
        var sm = CreateStateMachine();
        sm.ForceTransitionTo(TestState.Completed);
        sm.CurrentState.Should().Be(TestState.Completed);
    }

    [Fact]
    public void Reset_ToInitialState_ShouldSucceed()
    {
        var sm = CreateStateMachine();
        sm.TransitionTo(TestState.Running);
        sm.Reset(TestState.Idle);
        sm.CurrentState.Should().Be(TestState.Idle);
    }

    [Fact]
    public void Reset_WhenAlreadyAtInitial_ShouldNotFireEvent()
    {
        var sm = CreateStateMachine();
        var eventFired = false;
        sm.StateChanged += (_, _) => eventFired = true;
        sm.Reset(TestState.Idle);
        eventFired.Should().BeFalse();
    }

    [Fact]
    public void Reset_ShouldFireEvent()
    {
        var sm = CreateStateMachine();
        sm.TransitionTo(TestState.Running);
        StateChangedEventArgs<TestState>? capturedArgs = null;
        sm.StateChanged += (_, args) => capturedArgs = args;
        sm.Reset(TestState.Idle);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(TestState.Running);
        capturedArgs.NewState.Should().Be(TestState.Idle);
    }

    [Fact]
    public void StateChanged_ShouldFireOnTransitionTo()
    {
        var sm = CreateStateMachine();
        StateChangedEventArgs<TestState>? capturedArgs = null;
        sm.StateChanged += (_, args) => capturedArgs = args;
        sm.TransitionTo(TestState.Running);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(TestState.Idle);
        capturedArgs.NewState.Should().Be(TestState.Running);
        capturedArgs.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StateChanged_ShouldNotFireOnFailedTryTransitionTo()
    {
        var sm = CreateStateMachine();
        var eventFired = false;
        sm.StateChanged += (_, _) => eventFired = true;
        sm.TryTransitionTo(TestState.Completed);
        eventFired.Should().BeFalse();
    }

    [Fact]
    public void StateChanged_ShouldFireOnForceTransitionTo()
    {
        var sm = CreateStateMachine();
        StateChangedEventArgs<TestState>? capturedArgs = null;
        sm.StateChanged += (_, args) => capturedArgs = args;
        sm.ForceTransitionTo(TestState.Failed);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(TestState.Idle);
        capturedArgs.NewState.Should().Be(TestState.Failed);
    }

    [Fact]
    public void GetValidNextStates_ShouldReturnValidStates()
    {
        var sm = CreateStateMachine();
        var nextStates = sm.GetValidNextStates();
        nextStates.Should().Contain(TestState.Running);
        nextStates.Should().HaveCount(1);
    }

    [Fact]
    public void GetValidNextStates_FromTerminalState_ShouldReturnEmpty()
    {
        var sm = CreateStateMachine();
        sm.TransitionTo(TestState.Running);
        sm.TransitionTo(TestState.Completed);
        var nextStates = sm.GetValidNextStates();
        nextStates.Should().Contain(TestState.Idle);
    }

    [Fact]
    public void MultipleTransitions_ShouldWorkCorrectly()
    {
        var sm = CreateStateMachine();
        var states = new List<TestState>();
        sm.StateChanged += (_, args) => states.Add(args.NewState);

        sm.TransitionTo(TestState.Running);
        sm.TransitionTo(TestState.Paused);
        sm.TransitionTo(TestState.Running);
        sm.TransitionTo(TestState.Completed);

        sm.CurrentState.Should().Be(TestState.Completed);
        states.Should().Equal(new[] { TestState.Running, TestState.Paused, TestState.Running, TestState.Completed });
    }

    [Fact]
    public void StateChanged_WithClockService_ShouldUseClockTimestamp()
    {
        var fakeTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var mockClock = new Mock<IClockService>();
        mockClock.Setup(c => c.GetUtcNow()).Returns(fakeTime);

        var transitions = new Dictionary<TestState, FrozenSet<TestState>>
        {
            [TestState.Idle] = new HashSet<TestState> { TestState.Running }.ToFrozenSet()
        }.ToFrozenDictionary();

        var sm = new StateMachine<TestState>(transitions, TestState.Idle, mockClock.Object);
        StateChangedEventArgs<TestState>? capturedArgs = null;
        sm.StateChanged += (_, args) => capturedArgs = args;

        sm.TransitionTo(TestState.Running);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Timestamp.Should().Be(fakeTime);
    }

    [Fact]
    public void IsTerminalState_WithTerminalStates_ShouldReturnTrue()
    {
        var transitions = new Dictionary<TestState, FrozenSet<TestState>>
        {
            [TestState.Idle] = new HashSet<TestState> { TestState.Running }.ToFrozenSet(),
            [TestState.Running] = new HashSet<TestState> { TestState.Completed, TestState.Failed }.ToFrozenSet(),
            [TestState.Completed] = new HashSet<TestState>().ToFrozenSet(),
            [TestState.Failed] = new HashSet<TestState>().ToFrozenSet()
        }.ToFrozenDictionary();

        var terminalStates = new HashSet<TestState> { TestState.Completed, TestState.Failed }.ToFrozenSet();
        var sm = new StateMachine<TestState>(transitions, TestState.Idle, terminalStates);

        sm.IsTerminalState().Should().BeFalse();
        sm.TransitionTo(TestState.Running);
        sm.IsTerminalState().Should().BeFalse();
        sm.TransitionTo(TestState.Completed);
        sm.IsTerminalState().Should().BeTrue();
    }

    [Fact]
    public void IsTerminalState_WithoutTerminalStates_ShouldInferFromEmptyTransitions()
    {
        var transitions = new Dictionary<TestState, FrozenSet<TestState>>
        {
            [TestState.Idle] = new HashSet<TestState> { TestState.Running }.ToFrozenSet(),
            [TestState.Running] = new HashSet<TestState> { TestState.Failed }.ToFrozenSet(),
            [TestState.Failed] = new HashSet<TestState>().ToFrozenSet()
        }.ToFrozenDictionary();

        var sm = new StateMachine<TestState>(transitions, TestState.Idle);
        sm.IsTerminalState().Should().BeFalse();
        sm.TransitionTo(TestState.Running);
        sm.TransitionTo(TestState.Failed);
        sm.IsTerminalState().Should().BeTrue();
    }

    [Fact]
    public void TransitionFailed_ShouldFireOnInvalidTransition()
    {
        var sm = CreateStateMachine();
        TransitionFailedEventArgs<TestState>? failedArgs = null;
        sm.TransitionFailed += (_, args) => failedArgs = args;

        var act = () => sm.TransitionTo(TestState.Completed);
        act.Should().Throw<InvalidOperationException>();

        failedArgs.Should().NotBeNull();
        failedArgs!.FromState.Should().Be(TestState.Idle);
        failedArgs.ToState.Should().Be(TestState.Completed);
    }

    [Fact]
    public void TransitionFailed_ShouldNotFireOnValidTransition()
    {
        var sm = CreateStateMachine();
        var failedFired = false;
        sm.TransitionFailed += (_, _) => failedFired = true;

        sm.TransitionTo(TestState.Running);
        failedFired.Should().BeFalse();
    }
}
