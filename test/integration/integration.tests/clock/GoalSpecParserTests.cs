
namespace Integration.Tests.Clock;

public sealed class GoalSpecParserTests
{
    [Fact]
    public void TryParse_ValidJson_Should_Return_GoalSpec()
    {
        var json = "{\"outcome\":\"降低p95延迟\",\"verification\":\"npm test\",\"constraints\":\"不改auth\",\"boundaries\":\"src/\",\"iterationLog\":\"EXPERIMENTS.md\",\"failureCircuit\":\"3次失败停止\"}";

        var result = GoalSpecParser.TryParse(json);

        Assert.NotNull(result);
        Assert.Equal("降低p95延迟", result!.Outcome);
        Assert.Equal("npm test", result.Verification);
        Assert.Equal("不改auth", result.Constraints);
        Assert.Equal("src/", result.Boundaries);
        Assert.Equal("EXPERIMENTS.md", result.IterationLog);
        Assert.Equal("3次失败停止", result.FailureCircuit);
    }

    [Fact]
    public void TryParse_JsonInCodeBlock_Should_Extract()
    {
        var llmOutput = "好的，收集完成：\n```json\n{\"outcome\":\"目标\",\"verification\":\"验证\",\"constraints\":\"约束\",\"boundaries\":\"边界\",\"iterationLog\":\"记录\",\"failureCircuit\":\"熔断\"}\n```";

        var result = GoalSpecParser.TryParse(llmOutput);

        Assert.NotNull(result);
        Assert.Equal("目标", result!.Outcome);
        Assert.Equal("验证", result.Verification);
    }

    [Fact]
    public void TryParse_NullInput_Should_Return_Null()
    {
        var result = GoalSpecParser.TryParse(null);

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_EmptyInput_Should_Return_Null()
    {
        var result = GoalSpecParser.TryParse("");

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_PartialJson_Should_Return_GoalSpec_With_Default_Empty_Fields()
    {
        var json = "{\"outcome\":\"只有目标\"}";

        var result = GoalSpecParser.TryParse(json);

        Assert.NotNull(result);
        Assert.Equal("只有目标", result!.Outcome);
        Assert.Equal(string.Empty, result.Verification);
    }
}
