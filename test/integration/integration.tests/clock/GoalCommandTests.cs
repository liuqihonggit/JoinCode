
namespace Integration.Tests.Clock;

public sealed class GoalCommandTests
{
    [Fact]
    public void ParseGoalArgs_SimpleObjective_Should_Return_Objective()
    {
        var result = GoalCommand.ParseGoalArgs("实现用户注册功能");

        Assert.Equal("实现用户注册功能", result.Objective);
        Assert.Empty(result.Constraints);
        Assert.Null(result.TokenBudget);
        Assert.Null(result.CronExpression);
        Assert.False(result.IsCron);
    }

    [Fact]
    public void ParseGoalArgs_WithConstraint_Should_Parse()
    {
        var result = GoalCommand.ParseGoalArgs("--constraint '不修改公共API' 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Single(result.Constraints);
        Assert.Equal("不修改公共API", result.Constraints[0]);
    }

    [Fact]
    public void ParseGoalArgs_WithMultipleConstraints_Should_Parse()
    {
        var result = GoalCommand.ParseGoalArgs("--constraint '不修改公共API' --constraint '测试覆盖率>80%' 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Equal(2, result.Constraints.Count);
        Assert.Equal("不修改公共API", result.Constraints[0]);
        Assert.Equal("测试覆盖率>80%", result.Constraints[1]);
    }

    [Fact]
    public void ParseGoalArgs_WithBudget_Should_Parse()
    {
        var result = GoalCommand.ParseGoalArgs("--budget 50000 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Equal(50000, result.TokenBudget);
    }

    [Fact]
    public void ParseGoalArgs_WithConstraintAndBudget_Should_Parse()
    {
        var result = GoalCommand.ParseGoalArgs("--constraint '不修改API' --budget 100000 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Single(result.Constraints);
        Assert.Equal(100000, result.TokenBudget);
    }

    [Fact]
    public void ParseGoalArgs_CronFlag_Should_Return_CronMode()
    {
        var result = GoalCommand.ParseGoalArgs("--cron */5 * * * * 每五分钟检查代码");

        Assert.True(result.IsCron);
        Assert.Equal("*/5 * * * *", result.CronExpression);
        Assert.Equal("每五分钟检查代码", result.Objective);
    }

    [Fact]
    public void ParseGoalArgs_ShortCronFlag_Should_Return_CronMode()
    {
        var result = GoalCommand.ParseGoalArgs("-c 0 * * * * 每小时执行任务");

        Assert.True(result.IsCron);
        Assert.Equal("0 * * * *", result.CronExpression);
        Assert.Equal("每小时执行任务", result.Objective);
    }

    [Fact]
    public void ParseGoalArgs_CronWithoutDescription_Should_Return_EmptyObjective()
    {
        var result = GoalCommand.ParseGoalArgs("--cron */5 * * * *");

        Assert.True(result.IsCron);
        Assert.Equal("*/5 * * * *", result.CronExpression);
        Assert.Equal(string.Empty, result.Objective);
    }

    [Fact]
    public void ParseGoalArgs_CronWithTooFewTokens_Should_Return_NullCron()
    {
        var result = GoalCommand.ParseGoalArgs("--cron */5 * *");

        Assert.True(result.IsCron);
        Assert.Null(result.CronExpression);
    }

    [Fact]
    public void ParseGoalArgs_ConstraintWithDoubleQuotes_Should_Parse()
    {
        var result = GoalCommand.ParseGoalArgs("--constraint \"不修改公共API\" 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Single(result.Constraints);
        Assert.Equal("不修改公共API", result.Constraints[0]);
    }

    [Fact]
    public void ParseGoalArgs_ConstraintWithoutQuotes_Should_ParseSingleWord()
    {
        var result = GoalCommand.ParseGoalArgs("--constraint safe 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Single(result.Constraints);
        Assert.Equal("safe", result.Constraints[0]);
    }

    [Fact]
    public void ParseGoalArgs_BudgetWithInvalidValue_Should_Ignore()
    {
        var result = GoalCommand.ParseGoalArgs("--budget abc 实现功能");

        Assert.Equal("实现功能", result.Objective);
        Assert.Null(result.TokenBudget);
    }
}
