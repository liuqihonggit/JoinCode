
namespace Core.Goal.Tests;

public sealed class GoalRetryPolicyTests
{
    [Theory]
    [InlineData(0.7, RetryDecision.Accept)]
    [InlineData(0.8, RetryDecision.Accept)]
    [InlineData(1.0, RetryDecision.Accept)]
    public void Decide_HighScore_Should_Accept(double score, RetryDecision expected)
    {
        var decision = GoalRetryPolicy.Decide(score, 0);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(0.29, RetryDecision.Abandon)]
    [InlineData(0.1, RetryDecision.Abandon)]
    [InlineData(0.0, RetryDecision.Abandon)]
    public void Decide_LowScore_Should_Abandon(double score, RetryDecision expected)
    {
        var decision = GoalRetryPolicy.Decide(score, 0);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(0.3, 0, RetryDecision.RetryWithPatch)]
    [InlineData(0.5, 0, RetryDecision.RetryWithPatch)]
    [InlineData(0.69, 0, RetryDecision.RetryWithPatch)]
    [InlineData(0.4, 1, RetryDecision.RetryWithPatch)]
    public void Decide_MediumScore_BelowMaxRetries_Should_RetryWithPatch(double score, int retries, RetryDecision expected)
    {
        var decision = GoalRetryPolicy.Decide(score, retries);
        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(0.3, 2, RetryDecision.Abandon)]
    [InlineData(0.5, 3, RetryDecision.Abandon)]
    [InlineData(0.6, 2, RetryDecision.Abandon)]
    public void Decide_MediumScore_AtMaxRetries_Should_Abandon(double score, int retries, RetryDecision expected)
    {
        var decision = GoalRetryPolicy.Decide(score, retries);
        Assert.Equal(expected, decision);
    }

    [Fact]
    public void Decide_Boundary_07_Should_Accept()
    {
        Assert.Equal(RetryDecision.Accept, GoalRetryPolicy.Decide(0.7, 0));
    }

    [Fact]
    public void Decide_Boundary_Below07_Should_Retry()
    {
        Assert.Equal(RetryDecision.RetryWithPatch, GoalRetryPolicy.Decide(0.69, 0));
    }

    [Fact]
    public void Decide_Boundary_03_Should_Retry()
    {
        Assert.Equal(RetryDecision.RetryWithPatch, GoalRetryPolicy.Decide(0.3, 0));
    }

    [Fact]
    public void Decide_Boundary_Below03_Should_Abandon()
    {
        Assert.Equal(RetryDecision.Abandon, GoalRetryPolicy.Decide(0.29, 0));
    }
}
