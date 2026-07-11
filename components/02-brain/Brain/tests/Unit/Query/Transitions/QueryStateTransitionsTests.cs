namespace Core.Tests.Query.Transitions;

public class QueryStateTransitionsTests
{
    private readonly QueryStateTransitions _transitions = new();

    [Fact]
    public void CurrentState_Default_ShouldBeIdle()
    {
        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public void TransitionTo_IdleToInitializing_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);

        _transitions.CurrentState.Should().Be(QueryState.Initializing);
    }

    [Fact]
    public void TransitionTo_InitializingToRunning_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);

        _transitions.CurrentState.Should().Be(QueryState.Running);
    }

    [Fact]
    public void TransitionTo_RunningToCompleted_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.Completed);

        _transitions.CurrentState.Should().Be(QueryState.Completed);
    }

    [Fact]
    public void TransitionTo_CompletedToIdle_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.Completed);
        _transitions.TransitionTo(QueryState.Idle);

        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public void TransitionTo_InvalidTransition_ShouldThrowInvalidOperationException()
    {
        var act = () => _transitions.TransitionTo(QueryState.Running);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Idle*Running*");
    }

    [Fact]
    public void TransitionTo_IdleToRunning_ShouldThrowInvalidOperationException()
    {
        var act = () => _transitions.TransitionTo(QueryState.Running);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CanTransitionTo_ValidTransition_ShouldReturnTrue()
    {
        _transitions.CanTransitionTo(QueryState.Idle, QueryState.Initializing).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_InvalidTransition_ShouldReturnFalse()
    {
        _transitions.CanTransitionTo(QueryState.Idle, QueryState.Running).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_SameState_ShouldReturnTrue()
    {
        _transitions.CanTransitionTo(QueryState.Idle, QueryState.Idle).Should().BeTrue();
        _transitions.CanTransitionTo(QueryState.Running, QueryState.Running).Should().BeTrue();
    }

    [Fact]
    public void Reset_FromRunning_ShouldGoBackToIdle()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);

        _transitions.Reset();

        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public void Reset_FromIdle_ShouldStayIdle()
    {
        _transitions.Reset();

        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public void StateChanged_ValidTransition_ShouldFireEvent()
    {
        QueryStateChangedEventArgs? capturedArgs = null;
        _transitions.StateChanged += (_, args) => capturedArgs = args;

        _transitions.TransitionTo(QueryState.Initializing);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(QueryState.Idle);
        capturedArgs.NewState.Should().Be(QueryState.Initializing);
        capturedArgs.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StateChanged_Reset_ShouldFireEvent()
    {
        _transitions.TransitionTo(QueryState.Initializing);

        QueryStateChangedEventArgs? capturedArgs = null;
        _transitions.StateChanged += (_, args) => capturedArgs = args;

        _transitions.Reset();

        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(QueryState.Initializing);
        capturedArgs.NewState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public void StateChanged_ResetFromIdle_ShouldNotFireEvent()
    {
        var eventFired = false;
        _transitions.StateChanged += (_, _) => eventFired = true;

        _transitions.Reset();

        eventFired.Should().BeFalse();
    }

    [Fact]
    public void TransitionTo_RunningToWaitingForTool_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.WaitingForTool);

        _transitions.CurrentState.Should().Be(QueryState.WaitingForTool);
    }

    [Fact]
    public void TransitionTo_WaitingForToolToExecutingTool_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.WaitingForTool);
        _transitions.TransitionTo(QueryState.ExecutingTool);

        _transitions.CurrentState.Should().Be(QueryState.ExecutingTool);
    }

    [Fact]
    public void TransitionTo_ExecutingToolToRunning_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.WaitingForTool);
        _transitions.TransitionTo(QueryState.ExecutingTool);
        _transitions.TransitionTo(QueryState.Running);

        _transitions.CurrentState.Should().Be(QueryState.Running);
    }

    [Fact]
    public void TransitionTo_FailedToIdle_ShouldSucceed()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Failed);
        _transitions.TransitionTo(QueryState.Idle);

        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }
}
