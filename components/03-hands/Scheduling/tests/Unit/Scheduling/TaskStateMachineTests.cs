
namespace Core.Tests.Scheduling;

public class TaskStateMachineTests
{
    [Fact]
    public void Constructor_WithDefaultState_ShouldSetPending()
    {
        // Arrange & Act
        var stateMachine = new TaskStateMachine();

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Pending);
    }

    [Theory]
    [InlineData(TaskState.Pending)]
    [InlineData(TaskState.InProgress)]
    [InlineData(TaskState.Completed)]
    public void Constructor_WithSpecificState_ShouldSetThatState(TaskState initialState)
    {
        // Arrange & Act
        var stateMachine = new TaskStateMachine(initialState);

        // Assert
        stateMachine.CurrentState.Should().Be(initialState);
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.InProgress)]
    [InlineData(TaskState.Pending, TaskState.WaitingForDependencies)]
    [InlineData(TaskState.Pending, TaskState.Cancelled)]
    [InlineData(TaskState.WaitingForDependencies, TaskState.InProgress)]
    [InlineData(TaskState.WaitingForDependencies, TaskState.Cancelled)]
    [InlineData(TaskState.InProgress, TaskState.Paused)]
    [InlineData(TaskState.InProgress, TaskState.Completed)]
    [InlineData(TaskState.InProgress, TaskState.Failed)]
    [InlineData(TaskState.InProgress, TaskState.Stopped)]
    [InlineData(TaskState.Paused, TaskState.InProgress)]
    [InlineData(TaskState.Paused, TaskState.Cancelled)]
    public void TryTransitionTo_WithValidTransition_ShouldReturnTrue(TaskState fromState, TaskState toState)
    {
        // Arrange
        var stateMachine = new TaskStateMachine(fromState);

        // Act
        var result = stateMachine.TryTransitionTo(toState);

        // Assert
        result.Should().BeTrue();
        stateMachine.CurrentState.Should().Be(toState);
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.Completed)]
    [InlineData(TaskState.Pending, TaskState.Failed)]
    [InlineData(TaskState.InProgress, TaskState.Pending)]
    [InlineData(TaskState.Completed, TaskState.InProgress)]
    [InlineData(TaskState.Failed, TaskState.Pending)]
    [InlineData(TaskState.Cancelled, TaskState.InProgress)]
    public void TryTransitionTo_WithInvalidTransition_ShouldReturnFalse(TaskState fromState, TaskState toState)
    {
        // Arrange
        var stateMachine = new TaskStateMachine(fromState);

        // Act
        var result = stateMachine.TryTransitionTo(toState);

        // Assert
        result.Should().BeFalse();
        stateMachine.CurrentState.Should().Be(fromState);
    }

    [Fact]
    public void TryTransitionTo_SameState_ShouldReturnTrue()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.InProgress);

        // Act
        var result = stateMachine.TryTransitionTo(TaskState.InProgress);

        // Assert
        result.Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.InProgress);
    }

    [Fact]
    public void ForceTransitionTo_ShouldTransitionWithoutValidation()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act
        stateMachine.ForceTransitionTo(TaskState.Completed);

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Completed);
    }

    [Fact]
    public void StateChanged_ShouldTriggerEvent()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);
        TaskStateChangedEventArgs? capturedArgs = null;
        stateMachine.StateChanged += (sender, args) => capturedArgs = args;

        // Act
        stateMachine.TryTransitionTo(TaskState.InProgress);

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.PreviousState.Should().Be(TaskState.Pending);
        capturedArgs.NewState.Should().Be(TaskState.InProgress);
    }

    [Fact]
    public void StateChanged_WhenTransitionFails_ShouldNotTriggerEvent()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Completed);
        var eventTriggered = false;
        stateMachine.StateChanged += (sender, args) => eventTriggered = true;

        // Act
        stateMachine.TryTransitionTo(TaskState.InProgress);

        // Assert
        eventTriggered.Should().BeFalse();
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.InProgress, true)]
    [InlineData(TaskState.Pending, TaskState.Completed, false)]
    [InlineData(TaskState.InProgress, TaskState.Completed, true)]
    public void CanTransitionTo_ShouldReturnExpectedResult(TaskState fromState, TaskState toState, bool expected)
    {
        // Arrange
        var stateMachine = new TaskStateMachine(fromState);

        // Act
        var result = stateMachine.CanTransitionTo(toState);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GetValidNextStates_FromPending_ShouldReturnValidStates()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act
        var validStates = stateMachine.GetValidNextStates();

        // Assert
        validStates.Should().Contain(TaskState.WaitingForDependencies);
        validStates.Should().Contain(TaskState.InProgress);
        validStates.Should().Contain(TaskState.Cancelled);
        validStates.Should().HaveCount(3);
    }

    [Fact]
    public void GetValidNextStates_FromCompleted_ShouldReturnEmpty()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Completed);

        // Act
        var validStates = stateMachine.GetValidNextStates();

        // Assert
        validStates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TaskState.Completed, true)]
    [InlineData(TaskState.Failed, true)]
    [InlineData(TaskState.Cancelled, true)]
    [InlineData(TaskState.Stopped, true)]
    [InlineData(TaskState.Pending, false)]
    [InlineData(TaskState.InProgress, false)]
    [InlineData(TaskState.Paused, false)]
    public void IsTerminalState_ShouldReturnExpectedResult(TaskState state, bool expected)
    {
        // Arrange
        var stateMachine = new TaskStateMachine(state);

        // Act
        var result = stateMachine.IsTerminalState();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(TaskState.Pending, true)]
    [InlineData(TaskState.WaitingForDependencies, true)]
    [InlineData(TaskState.InProgress, false)]
    [InlineData(TaskState.Completed, false)]
    public void CanExecute_ShouldReturnExpectedResult(TaskState state, bool expected)
    {
        // Arrange
        var stateMachine = new TaskStateMachine(state);

        // Act
        var result = stateMachine.CanExecute();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void MultipleTransitions_ShouldWorkCorrectly()
    {
        // Arrange
        var stateMachine = new TaskStateMachine();
        var states = new List<TaskState>();
        stateMachine.StateChanged += (sender, args) => states.Add(args.NewState);

        // Act
        stateMachine.TryTransitionTo(TaskState.WaitingForDependencies);
        stateMachine.TryTransitionTo(TaskState.InProgress);
        stateMachine.TryTransitionTo(TaskState.Paused);
        stateMachine.TryTransitionTo(TaskState.InProgress);
        stateMachine.TryTransitionTo(TaskState.Completed);

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Completed);
        states.Should().Equal(new[]
        {
            TaskState.WaitingForDependencies,
            TaskState.InProgress,
            TaskState.Paused,
            TaskState.InProgress,
            TaskState.Completed
        });
    }

    [Fact]
    public void ComplexWorkflow_FromPendingToCompleted()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act & Assert - 完整工作流
        stateMachine.TryTransitionTo(TaskState.WaitingForDependencies).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.WaitingForDependencies);

        stateMachine.TryTransitionTo(TaskState.InProgress).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.InProgress);

        stateMachine.TryTransitionTo(TaskState.Paused).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Paused);

        stateMachine.TryTransitionTo(TaskState.InProgress).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.InProgress);

        stateMachine.TryTransitionTo(TaskState.Completed).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Completed);

        // 终态不能再转换
        stateMachine.TryTransitionTo(TaskState.InProgress).Should().BeFalse();
        stateMachine.CurrentState.Should().Be(TaskState.Completed);
    }

    [Fact]
    public void ComplexWorkflow_FromPendingToFailed()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act
        stateMachine.TryTransitionTo(TaskState.InProgress).Should().BeTrue();
        stateMachine.TryTransitionTo(TaskState.Failed).Should().BeTrue();

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Failed);
        stateMachine.IsTerminalState().Should().BeTrue();
    }

    [Fact]
    public void ComplexWorkflow_Cancellation()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act - 从 Pending 取消
        stateMachine.TryTransitionTo(TaskState.Cancelled).Should().BeTrue();

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Cancelled);
        stateMachine.IsTerminalState().Should().BeTrue();
    }

    [Fact]
    public void ComplexWorkflow_CancellationFromPaused()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);
        stateMachine.TryTransitionTo(TaskState.InProgress).Should().BeTrue();
        stateMachine.TryTransitionTo(TaskState.Paused).Should().BeTrue();

        // Act
        stateMachine.TryTransitionTo(TaskState.Cancelled).Should().BeTrue();

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Cancelled);
    }

    [Fact]
    public void StateChangedEventArgs_ShouldContainCorrectData()
    {
        // Arrange
        var previousState = TaskState.Pending;
        var newState = TaskState.InProgress;
        var beforeTime = DateTime.UtcNow.AddMilliseconds(-10);

        // Act
        var args = new TaskStateChangedEventArgs(previousState, newState);
        var afterTime = DateTime.UtcNow.AddMilliseconds(10);

        // Assert
        args.PreviousState.Should().Be(previousState);
        args.NewState.Should().Be(newState);
        args.ChangedAt.Should().BeOnOrAfter(beforeTime);
        args.ChangedAt.Should().BeOnOrBefore(afterTime);
    }
}
