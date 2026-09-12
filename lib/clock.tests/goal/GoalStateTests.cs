
namespace Core.Goal.Tests;

public sealed class GoalStateTests
{
    [Fact]
    public void Default_Values_Should_Be_Correct()
    {
        var state = new GoalState();

        Assert.Equal(string.Empty, state.GoalId);
        Assert.Equal(string.Empty, state.Objective);
        Assert.Equal(GoalStatus.Pursuing, state.Status);
        Assert.Empty(state.Constraints);
        Assert.Null(state.TokenBudget);
        Assert.Equal(0, state.TokensUsed);
        Assert.Equal(0, state.TurnsCompleted);
        Assert.Null(state.PausedAt);
        Assert.Null(state.AchievedAt);
        Assert.Null(state.LastEvaluation);
    }

    [Fact]
    public void Elapsed_Should_Calculate_For_Active_Goal()
    {
        var state = new GoalState
        {
            GoalId = "g1",
            Objective = "test",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var elapsed = state.Elapsed;
        Assert.True(elapsed >= TimeSpan.FromMinutes(5));
        Assert.True(elapsed < TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void Elapsed_Should_Calculate_For_Achieved_Goal()
    {
        var created = DateTime.UtcNow.AddMinutes(-10);
        var achieved = DateTime.UtcNow.AddMinutes(-3);
        var state = new GoalState
        {
            GoalId = "g1",
            Objective = "test",
            CreatedAt = created,
            AchievedAt = achieved
        };

        Assert.Equal(achieved - created, state.Elapsed);
    }

    [Fact]
    public void Status_Should_Be_Mutable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Equal(GoalStatus.Pursuing, state.Status);

        state.Status = GoalStatus.Paused;
        Assert.Equal(GoalStatus.Paused, state.Status);

        state.Status = GoalStatus.Achieved;
        Assert.Equal(GoalStatus.Achieved, state.Status);

        state.Status = GoalStatus.Unmet;
        Assert.Equal(GoalStatus.Unmet, state.Status);

        state.Status = GoalStatus.BudgetLimited;
        Assert.Equal(GoalStatus.BudgetLimited, state.Status);
    }

    [Fact]
    public void TokensUsed_Should_Be_Mutable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Equal(0, state.TokensUsed);

        state.TokensUsed = 500;
        Assert.Equal(500, state.TokensUsed);
    }

    [Fact]
    public void TurnsCompleted_Should_Be_Mutable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Equal(0, state.TurnsCompleted);

        state.TurnsCompleted = 3;
        Assert.Equal(3, state.TurnsCompleted);
    }

    [Fact]
    public void LastEvaluation_Should_Be_Settable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Null(state.LastEvaluation);

        state.LastEvaluation = GoalEvaluationResult.Completed("目标已完成");
        Assert.NotNull(state.LastEvaluation);
        Assert.True(state.LastEvaluation.IsCompleted);
        Assert.Equal("目标已完成", state.LastEvaluation.Reason);
    }

    [Fact]
    public void PausedAt_Should_Be_Settable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Null(state.PausedAt);

        var pausedAt = DateTime.UtcNow;
        state.PausedAt = pausedAt;
        Assert.Equal(pausedAt, state.PausedAt);
    }

    [Fact]
    public void Constraints_Should_Be_Initialized()
    {
        var state = new GoalState
        {
            GoalId = "g1",
            Objective = "test",
            Constraints = ["不修改公共API", "测试覆盖率达到80%"]
        };

        Assert.Equal(2, state.Constraints.Count);
        Assert.Equal("不修改公共API", state.Constraints[0]);
        Assert.Equal("测试覆盖率达到80%", state.Constraints[1]);
    }

    [Fact]
    public void TokenBudget_Should_Be_Null_By_Default()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test" };

        Assert.Null(state.TokenBudget);
    }

    [Fact]
    public void TokenBudget_Should_Be_Settable()
    {
        var state = new GoalState { GoalId = "g1", Objective = "test", TokenBudget = 50000 };

        Assert.Equal(50000, state.TokenBudget);
    }
}

public sealed class GoalEvaluationResultTests
{
    [Fact]
    public void Completed_Should_Return_IsCompleted_True()
    {
        var result = GoalEvaluationResult.Completed("所有测试通过");

        Assert.True(result.IsCompleted);
        Assert.Equal("所有测试通过", result.Reason);
    }

    [Fact]
    public void NotCompleted_Should_Return_IsCompleted_False()
    {
        var result = GoalEvaluationResult.NotCompleted("仍有未完成的工作");

        Assert.False(result.IsCompleted);
        Assert.Equal("仍有未完成的工作", result.Reason);
    }

    [Fact]
    public void Completed_And_NotCompleted_Should_Be_Different()
    {
        var completed = GoalEvaluationResult.Completed("done");
        var notCompleted = GoalEvaluationResult.NotCompleted("not done");

        Assert.NotEqual(completed, notCompleted);
    }

    [Fact]
    public void Same_Values_Should_Be_Equal()
    {
        var a = GoalEvaluationResult.Completed("reason");
        var b = GoalEvaluationResult.Completed("reason");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}

public sealed class GoalStatusTests
{
    [Theory]
    [InlineData(GoalStatus.Pursuing)]
    [InlineData(GoalStatus.Paused)]
    [InlineData(GoalStatus.Achieved)]
    [InlineData(GoalStatus.Unmet)]
    [InlineData(GoalStatus.BudgetLimited)]
    public void All_Statuses_Should_Exist(GoalStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void GoalStatus_Should_Have_5_Values()
    {
        Assert.Equal(5, Enum.GetValues<GoalStatus>().Length);
    }
}
