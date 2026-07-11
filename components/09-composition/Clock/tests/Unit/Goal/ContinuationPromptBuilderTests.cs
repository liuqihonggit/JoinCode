
namespace Core.Goal.Tests;

public sealed class ContinuationPromptBuilderTests
{
    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Objective()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现用户注册功能", [], 100, null, "仍有未完成的工作");

        Assert.Contains("实现用户注册功能", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Constraints()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", ["不修改公共API", "测试覆盖率>80%"], 100, null, "继续");

        Assert.Contains("不修改公共API", prompt);
        Assert.Contains("测试覆盖率>80%", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithNoConstraints_Should_Show_None()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "继续");

        Assert.Contains("无", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithBudget_Should_Show_Budget_Info()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 500, 1000, "继续");

        Assert.Contains("1000", prompt);
        Assert.Contains("500", prompt);
        Assert.Contains("500", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithoutBudget_Should_Show_Tokens_Used()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 300, null, "继续");

        Assert.Contains("300", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Evaluator_Reason()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "测试尚未通过，需要修复");

        Assert.Contains("测试尚未通过，需要修复", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Completion_Audit()
    {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "继续");

        Assert.Contains("completion audit", prompt);
        Assert.Contains("proxy signals", prompt);
        Assert.Contains("uncertainty", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Objective()
    {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("实现功能", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Budget_Info()
    {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("5000", prompt);
        Assert.Contains("10000", prompt);
        Assert.Contains("120", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Wrap_Up_Instruction()
    {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("budget_limited", prompt);
        Assert.Contains("Wrap up", prompt);
    }
}
