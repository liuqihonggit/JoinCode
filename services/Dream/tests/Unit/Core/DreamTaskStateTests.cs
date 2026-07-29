
namespace Dream.Tests;

/// <summary>
/// 做梦任务状态测试
/// </summary>
public sealed class DreamTaskStateTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var state = new DreamTaskState
        {
            Id = "d12345678",
            Description = "test",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = 5,
            PriorMtime = 12345678
        };

        // Assert
        Assert.Equal("d12345678", state.Id);
        Assert.Equal(DreamTaskStatus.Running, state.Status);
        Assert.Equal(DreamPhase.Starting, state.Phase);
        Assert.Equal(5, state.SessionsReviewing);
        Assert.Equal(12345678, state.PriorMtime);
        Assert.Empty(state.FilesTouched);
        Assert.Empty(state.Turns);
        Assert.False(state.IsTerminal);
    }

    [Fact]
    public void AddTurn_WithEmptyTouchedPaths_ShouldNotChangePhase()
    {
        // Arrange
        var state = CreateTestState();
        var turn = new DreamTurn { Text = "test", ToolUseCount = 0 };

        // Act
        state.AddTurn(turn, Array.Empty<string>());

        // Assert
        Assert.Equal(DreamPhase.Starting, state.Phase);
        Assert.Single(state.Turns);
    }

    [Fact]
    public void AddTurn_WithTouchedPaths_ShouldChangePhaseToUpdating()
    {
        // Arrange
        var state = CreateTestState();
        var turn = new DreamTurn { Text = "test", ToolUseCount = 1 };

        // Act
        state.AddTurn(turn, new[] { "file1.md", "file2.md" });

        // Assert
        Assert.Equal(DreamPhase.Updating, state.Phase);
        Assert.Equal(2, state.FilesTouched.Count);
    }

    [Fact]
    public void AddTurn_ShouldDeduplicateFilePaths()
    {
        // Arrange
        var state = CreateTestState();
        var turn1 = new DreamTurn { Text = "turn1", ToolUseCount = 1 };
        var turn2 = new DreamTurn { Text = "turn2", ToolUseCount = 1 };

        // Act
        state.AddTurn(turn1, new[] { "file1.md", "file2.md" });
        state.AddTurn(turn2, new[] { "file2.md", "file3.md" }); // file2.md 重复

        // Assert
        Assert.Equal(3, state.FilesTouched.Count);
        Assert.Contains("file1.md", state.FilesTouched);
        Assert.Contains("file2.md", state.FilesTouched);
        Assert.Contains("file3.md", state.FilesTouched);
    }

    [Fact]
    public void AddTurn_ShouldLimitMaxTurns()
    {
        // Arrange
        var state = CreateTestState();

        // Act - 添加35个回合（超过最大30个）
        for (var i = 0; i < 35; i++)
        {
            state.AddTurn(new DreamTurn { Text = $"turn{i}", ToolUseCount = 0 }, Array.Empty<string>());
        }

        // Assert
        Assert.Equal(30, state.Turns.Count);
        Assert.Equal("turn5", state.Turns[0].Text); // 最早的5个被移除
        Assert.Equal("turn34", state.Turns[29].Text);
    }

    [Fact]
    public void Complete_ShouldSetStatusAndEndTime()
    {
        // Arrange
        var state = CreateTestState();
        var beforeComplete = DateTime.UtcNow;

        // Act
        state.Complete();

        // Assert
        Assert.Equal(DreamTaskStatus.Completed, state.Status);
        Assert.True(state.Notified);
        Assert.NotNull(state.EndTime);
        Assert.True(state.EndTime >= beforeComplete);
        Assert.True(state.IsTerminal);
    }

    [Fact]
    public void Fail_ShouldSetStatusAndEndTime()
    {
        // Arrange
        var state = CreateTestState();

        // Act
        state.Fail();

        // Assert
        Assert.Equal(DreamTaskStatus.Failed, state.Status);
        Assert.True(state.Notified);
        Assert.NotNull(state.EndTime);
        Assert.True(state.IsTerminal);
    }

    [Fact]
    public void Kill_ShouldSetStatusAndCancelToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var state = new DreamTaskState
        {
            Id = "d12345678",
            Description = "test",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = 5,
            PriorMtime = 12345678,
            AbortController = cts
        };

        // Act
        state.Kill();

        // Assert
        Assert.Equal(DreamTaskStatus.Killed, state.Status);
        Assert.True(state.Notified);
        Assert.NotNull(state.EndTime);
        Assert.True(cts.Token.IsCancellationRequested);
        Assert.True(state.IsTerminal);
    }

    [Fact]
    public void IsTerminal_ShouldReturnFalseForRunning()
    {
        // Arrange
        var state = CreateTestState();

        // Assert
        Assert.False(state.IsTerminal);
    }

    [Theory]
    [InlineData(DreamTaskStatus.Completed)]
    [InlineData(DreamTaskStatus.Failed)]
    [InlineData(DreamTaskStatus.Killed)]
    public void IsTerminal_ShouldReturnTrueForTerminalStates(DreamTaskStatus status)
    {
        // Arrange
        var state = CreateTestState();
        state.Status = status;

        // Assert
        Assert.True(state.IsTerminal);
    }

    private static DreamTaskState CreateTestState() => new()
    {
        Id = TaskIdGenerator.GenerateTaskId(TaskType.Dream),
        Description = "test",
        StartTime = DateTime.UtcNow,
        SessionsReviewing = 5,
        PriorMtime = 0
    };
}
