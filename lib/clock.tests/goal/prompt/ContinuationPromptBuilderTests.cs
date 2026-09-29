
namespace Core.Goal.Tests;

public sealed class ContinuationPromptBuilderTests {
    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Objective() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现用户注册功能", [], 100, null, "仍有未完成的工作");

        Assert.Contains("实现用户注册功能", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Constraints() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", ["不修改公共API", "测试覆盖率>80%"], 100, null, "继续");

        Assert.Contains("不修改公共API", prompt);
        Assert.Contains("测试覆盖率>80%", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithNoConstraints_Should_Show_None() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "继续");

        Assert.Contains("无", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithBudget_Should_Show_Budget_Info() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 500, 1000, "继续");

        Assert.Contains("1000", prompt);
        Assert.Contains("500", prompt);
        Assert.Contains("500", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_WithoutBudget_Should_Show_Tokens_Used() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 300, null, "继续");

        Assert.Contains("300", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Evaluator_Reason() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "测试尚未通过，需要修复");

        Assert.Contains("测试尚未通过，需要修复", prompt);
    }

    [Fact]
    public void BuildContinuationPrompt_Should_Contain_Completion_Audit() {
        var prompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            "实现功能", [], 100, null, "继续");

        Assert.Contains("completion audit", prompt);
        Assert.Contains("proxy signals", prompt);
        Assert.Contains("uncertainty", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Objective() {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("实现功能", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Budget_Info() {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("5000", prompt);
        Assert.Contains("10000", prompt);
        Assert.Contains("120", prompt);
    }

    [Fact]
    public void BuildBudgetLimitPrompt_Should_Contain_Wrap_Up_Instruction() {
        var prompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
            "实现功能", 5000, 10000, 120);

        Assert.Contains("budget_limited", prompt);
        Assert.Contains("Wrap up", prompt);
    }

    [Fact]
    public void BuildStagnationAlertPrompt_Should_Contain_Objective() {
        var prompt = ContinuationPromptBuilder.BuildStagnationAlertPrompt(
            "实现用户注册", 3600, 3);

        Assert.Contains("实现用户注册", prompt);
    }

    [Fact]
    public void BuildStagnationAlertPrompt_Should_Contain_Stagnation_Alert() {
        var prompt = ContinuationPromptBuilder.BuildStagnationAlertPrompt(
            "实现功能", 3600, 3);

        Assert.Contains("STAGNATION ALERT", prompt);
        Assert.Contains("autonomous action", prompt);
    }

    [Fact]
    public void BuildStagnationAlertPrompt_Should_Contain_Duration_And_Turns() {
        var prompt = ContinuationPromptBuilder.BuildStagnationAlertPrompt(
            "实现功能", 5400, 5);

        Assert.Contains("1h30m", prompt);
        Assert.Contains("5 turn", prompt);
    }

    [Fact]
    public void BuildStagnationAlertPrompt_Should_Contain_Action_Directives() {
        var prompt = ContinuationPromptBuilder.BuildStagnationAlertPrompt(
            "实现功能", 3600, 2);

        Assert.Contains("Do NOT wait", prompt);
        Assert.Contains("EXECUTE", prompt);
        Assert.Contains("Prove progress through action", prompt);
    }

    [Fact]
    public void BuildConstraintsText_Empty_Should_Return_None_Marker() {
        var text = ContinuationPromptBuilder.BuildConstraintsText([]);

        Assert.Equal("无", text);
    }

    [Fact]
    public void BuildConstraintsText_Null_Should_Throw_ArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() => ContinuationPromptBuilder.BuildConstraintsText(null!));
    }

    [Fact]
    public void BuildConstraintsText_Single_Should_Prefix_With_Dash() {
        var text = ContinuationPromptBuilder.BuildConstraintsText(["只读模式"]);

        Assert.Equal("- 只读模式", text);
    }

    [Fact]
    public void BuildConstraintsText_Multiple_Should_Join_With_Newline() {
        var text = ContinuationPromptBuilder.BuildConstraintsText(["A", "B", "C"]);

        Assert.Equal("- A\n- B\n- C", text);
    }

    [Fact]
    public void BuildBudgetLines_NullBudget_Should_Show_Tokens_Used_Only() {
        var lines = ContinuationPromptBuilder.BuildBudgetLines(300, null);

        Assert.Equal("- Tokens used: 300", lines);
        Assert.DoesNotContain("budget", lines);
    }

    [Fact]
    public void BuildBudgetLines_WithBudget_Should_Show_All_Three_Lines() {
        var lines = ContinuationPromptBuilder.BuildBudgetLines(300, 1000);

        Assert.Contains("- Token budget: 1000", lines);
        Assert.Contains("- Budget utilization: 300 / 1000", lines);
        Assert.Contains("- Tokens remaining: 700", lines);
    }

    [Fact]
    public void BuildBudgetLines_OverBudget_Should_Clamp_Remaining_To_Zero() {
        var lines = ContinuationPromptBuilder.BuildBudgetLines(1500, 1000);

        Assert.Contains("- Tokens remaining: 0", lines);
    }

    [Fact]
    public void BuildBudgetLines_ExactBudget_Should_Show_Zero_Remaining() {
        var lines = ContinuationPromptBuilder.BuildBudgetLines(1000, 1000);

        Assert.Contains("- Tokens remaining: 0", lines);
    }
}