
namespace Integration.Tests.Clock;

public sealed class GoalSpecPromptBuilderTests
{
    [Fact]
    public void Build_NoHint_NoConstraints_Should_Contain_All_Six_Fields()
    {
        var prompt = GoalSpecPromptBuilder.Build();

        Assert.Contains("目标 (Outcome)", prompt);
        Assert.Contains("验证方式 (Verification)", prompt);
        Assert.Contains("硬性约束 (Constraints)", prompt);
        Assert.Contains("工作边界 (Boundaries)", prompt);
        Assert.Contains("迭代与记录 (IterationLog)", prompt);
        Assert.Contains("失败熔断 (FailureCircuit)", prompt);
    }

    [Fact]
    public void Build_Should_Contain_Json_Schema_With_All_Keys()
    {
        var prompt = GoalSpecPromptBuilder.Build();

        Assert.Contains("\"outcome\"", prompt);
        Assert.Contains("\"verification\"", prompt);
        Assert.Contains("\"constraints\"", prompt);
        Assert.Contains("\"boundaries\"", prompt);
        Assert.Contains("\"iterationLog\"", prompt);
        Assert.Contains("\"failureCircuit\"", prompt);
    }

    [Fact]
    public void Build_WithHint_Should_Contain_Hint_Section()
    {
        var prompt = GoalSpecPromptBuilder.Build("降低 p95 延迟");

        Assert.Contains("降低 p95 延迟", prompt);
        Assert.Contains("用户初始目标提示", prompt);
    }

    [Fact]
    public void Build_WithConstraints_Should_Contain_Preset_Constraints()
    {
        var prompt = GoalSpecPromptBuilder.Build(null, ["不修改公共API", "覆盖率>80%"]);

        Assert.Contains("不修改公共API", prompt);
        Assert.Contains("覆盖率>80%", prompt);
        Assert.Contains("预填约束", prompt);
    }

    [Fact]
    public void Build_EmptyHint_And_EmptyConstraints_Should_Not_Contain_Optional_Sections()
    {
        var prompt = GoalSpecPromptBuilder.Build("", []);

        Assert.DoesNotContain("用户初始目标提示", prompt);
        Assert.DoesNotContain("预填约束", prompt);
    }

    [Fact]
    public void Build_Should_Contain_Execution_Flow_Instructions()
    {
        var prompt = GoalSpecPromptBuilder.Build();

        Assert.Contains("逐个向用户询问", prompt);
        Assert.Contains("输出 JSON", prompt);
        Assert.Contains("开始自主工作", prompt);
    }

    [Fact]
    public void Build_Should_Contain_GoalSpec_Keyword()
    {
        var prompt = GoalSpecPromptBuilder.Build();

        Assert.Contains("GoalSpec", prompt);
    }
}
