
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
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.Completed)]
    public void Constructor_WithSpecificState_ShouldSetThatState(TaskState initialState)
    {
        // Arrange & Act
        var stateMachine = new TaskStateMachine(initialState);

        // Assert
        stateMachine.CurrentState.Should().Be(initialState);
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.Running)]
    [InlineData(TaskState.Pending, TaskState.WaitingForDependency)]
    [InlineData(TaskState.Pending, TaskState.Cancelled)]
    [InlineData(TaskState.WaitingForDependency, TaskState.Running)]
    [InlineData(TaskState.WaitingForDependency, TaskState.Cancelled)]
    [InlineData(TaskState.Running, TaskState.Paused)]
    [InlineData(TaskState.Running, TaskState.Completed)]
    [InlineData(TaskState.Running, TaskState.Failed)]
    [InlineData(TaskState.Running, TaskState.Stopped)]
    [InlineData(TaskState.Paused, TaskState.Running)]
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
    [InlineData(TaskState.Running, TaskState.Pending)]
    [InlineData(TaskState.Completed, TaskState.Running)]
    [InlineData(TaskState.Failed, TaskState.Pending)]
    [InlineData(TaskState.Cancelled, TaskState.Running)]
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
        var stateMachine = new TaskStateMachine(TaskState.Running);

        // Act
        var result = stateMachine.TryTransitionTo(TaskState.Running);

        // Assert
        result.Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Running);
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
        StateChangedEventArgs<TaskState>? capturedArgs = null;
        stateMachine.StateChanged += (sender, args) => capturedArgs = args;

        // Act
        stateMachine.TryTransitionTo(TaskState.Running);

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldState.Should().Be(TaskState.Pending);
        capturedArgs.NewState.Should().Be(TaskState.Running);
    }

    [Fact]
    public void StateChanged_WhenTransitionFails_ShouldNotTriggerEvent()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Completed);
        var eventTriggered = false;
        stateMachine.StateChanged += (sender, args) => eventTriggered = true;

        // Act
        stateMachine.TryTransitionTo(TaskState.Running);

        // Assert
        eventTriggered.Should().BeFalse();
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.Running, true)]
    [InlineData(TaskState.Pending, TaskState.Completed, false)]
    [InlineData(TaskState.Running, TaskState.Completed, true)]
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
        validStates.Should().Contain(TaskState.WaitingForDependency);
        validStates.Should().Contain(TaskState.Running);
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
    [InlineData(TaskState.Running, false)]
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
    [InlineData(TaskState.WaitingForDependency, true)]
    [InlineData(TaskState.Running, false)]
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
        stateMachine.TryTransitionTo(TaskState.WaitingForDependency);
        stateMachine.TryTransitionTo(TaskState.Running);
        stateMachine.TryTransitionTo(TaskState.Paused);
        stateMachine.TryTransitionTo(TaskState.Running);
        stateMachine.TryTransitionTo(TaskState.Completed);

        // Assert
        stateMachine.CurrentState.Should().Be(TaskState.Completed);
        states.Should().Equal(new[]
        {
            TaskState.WaitingForDependency,
            TaskState.Running,
            TaskState.Paused,
            TaskState.Running,
            TaskState.Completed
        });
    }

    [Fact]
    public void ComplexWorkflow_FromPendingToCompleted()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act & Assert - 完整工作流
        stateMachine.TryTransitionTo(TaskState.WaitingForDependency).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.WaitingForDependency);

        stateMachine.TryTransitionTo(TaskState.Running).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Running);

        stateMachine.TryTransitionTo(TaskState.Paused).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Paused);

        stateMachine.TryTransitionTo(TaskState.Running).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Running);

        stateMachine.TryTransitionTo(TaskState.Completed).Should().BeTrue();
        stateMachine.CurrentState.Should().Be(TaskState.Completed);

        // 终态不能再转换
        stateMachine.TryTransitionTo(TaskState.Running).Should().BeFalse();
        stateMachine.CurrentState.Should().Be(TaskState.Completed);
    }

    [Fact]
    public void ComplexWorkflow_FromPendingToFailed()
    {
        // Arrange
        var stateMachine = new TaskStateMachine(TaskState.Pending);

        // Act
        stateMachine.TryTransitionTo(TaskState.Running).Should().BeTrue();
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
        stateMachine.TryTransitionTo(TaskState.Running).Should().BeTrue();
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
        var newState = TaskState.Running;
        var beforeTime = DateTime.UtcNow.AddMilliseconds(-10);

        // Act
        var args = new StateChangedEventArgs<TaskState>(previousState, newState);
        var afterTime = DateTime.UtcNow.AddMilliseconds(10);

        // Assert
        args.OldState.Should().Be(previousState);
        args.NewState.Should().Be(newState);
        args.Timestamp.Should().BeOnOrAfter(beforeTime);
        args.Timestamp.Should().BeOnOrBefore(afterTime);
    }
}
