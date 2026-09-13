namespace Brain.Tests.Unit.Planning;

public sealed class NoOpPlanDetectorTests
{
    [Theory]
    [InlineData("no changes needed")]
    [InlineData("No changes are needed")]
    [InlineData("no action required")]
    [InlineData("nothing to change")]
    [InlineData("already handled")]
    [InlineData("already implemented")]
    [InlineData("already resolved")]
    [InlineData("[no_changes]")]
    [InlineData("无需改动")]
    [InlineData("无需修改")]
    [InlineData("已经解决")]
    public void IsNoOpPlan_WithNoOpPhrases_ReturnsTrue(string plan)
    {
        Assert.True(NoOpPlanDetector.IsNoOpPlan(plan));
    }

    [Theory]
    [InlineData("I need to add a new feature")]
    [InlineData("Let me update the configuration")]
    [InlineData("We should fix the bug")]
    [InlineData("新增一个功能")]
    [InlineData("修复这个问题")]
    [InlineData("重构代码结构")]
    [InlineData("已经实现")]
    public void IsNoOpPlan_WithActionPhrases_ReturnsFalse(string plan)
    {
        Assert.False(NoOpPlanDetector.IsNoOpPlan(plan));
    }

    [Fact]
    public void IsNoOpPlan_WithEmptyString_ReturnsFalse()
    {
        Assert.False(NoOpPlanDetector.IsNoOpPlan(""));
    }

    [Fact]
    public void IsNoOpPlan_WithWhitespace_ReturnsFalse()
    {
        Assert.False(NoOpPlanDetector.IsNoOpPlan("   "));
    }

    [Fact]
    public void IsNoOpPlan_WithNegatedNoOp_ReturnsFalse()
    {
        Assert.False(NoOpPlanDetector.IsNoOpPlan("This is not no changes needed"));
    }

    [Fact]
    public void IsNoOpPlan_WithMixedContent_ReturnsFalse()
    {
        Assert.False(NoOpPlanDetector.IsNoOpPlan("no changes needed, but we should add tests"));
    }

    [Fact]
    public void IsNoOpPlan_WithLongerContext_ReturnsTrue()
    {
        Assert.True(NoOpPlanDetector.IsNoOpPlan("After reviewing the code, no changes needed. The implementation is correct."));
    }
}
