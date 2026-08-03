namespace Core.Tests.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

public class BootstrapAgentTests
{
    [Fact]
    public void ParseJudgment_ValidJson_ReturnsCorrectValues()
    {
        var json = """{"needsFix": true, "targetFile": "Foo.cs", "priority": "high", "4reasoning": "Loop detected"}""";

        var result = BootstrapAgent.ParseJudgment(json);

        Assert.True(result.NeedsFix);
        Assert.Equal("Foo.cs", result.TargetFile);
        Assert.Equal("high", result.Priority);
    }

    [Fact]
    public void ParseJudgment_NeedsFixFalse_ReturnsFalse()
    {
        var json = """{"needsFix": false, "targetFile": null, "priority": "low", "reasoning": "No fix needed"}""";

        var result = BootstrapAgent.ParseJudgment(json);

        Assert.False(result.NeedsFix);
    }

    [Fact]
    public void ParseJudgment_NoJson_ReturnsNeedsFixFalse()
    {
        var result = BootstrapAgent.ParseJudgment("No JSON here, just text");

        Assert.False(result.NeedsFix);
    }

    [Fact]
    public void ParseJudgment_InvalidJson_ReturnsNeedsFixFalse()
    {
        var result = BootstrapAgent.ParseJudgment("{invalid json}");

        Assert.False(result.NeedsFix);
    }

    [Fact]
    public void ParseJudgment_JsonWithSurroundingText_ExtractsJson()
    {
        var response = "Here is my analysis:\n```json\n{\"needsFix\": true, \"targetFile\": \"Bar.cs\", \"priority\": \"medium\", \"reasoning\": \"Fix needed\"}\n```\nDone.";

        var result = BootstrapAgent.ParseJudgment(response);

        Assert.True(result.NeedsFix);
        Assert.Equal("Bar.cs", result.TargetFile);
    }

    [Fact]
    public void ParseJudgment_JsonWithTrailingComma_ShouldParse()
    {
        var json = """{"needsFix": true, "targetFile": "Foo.cs", "priority": "high", "reasoning": "Loop",}""";

        var result = BootstrapAgent.ParseJudgment(json);

        Assert.True(result.NeedsFix);
        Assert.Equal("Foo.cs", result.TargetFile);
    }

    [Fact]
    public void ParseJudgment_JsonWithCaseInsensitive_ShouldParse()
    {
        var json = """{"NeedsFix": true, "TargetFile": "Foo.cs", "Priority": "high", "Reasoning": "Loop"}""";

        var result = BootstrapAgent.ParseJudgment(json);

        Assert.True(result.NeedsFix);
        Assert.Equal("Foo.cs", result.TargetFile);
    }

    [Fact]
    public void ParseJudgment_JsonInCodeBlockWithTrailingComma_ShouldParse()
    {
        var response = "```json\n{\"needsFix\": true, \"targetFile\": \"Bar.cs\",}\n```";

        var result = BootstrapAgent.ParseJudgment(response);

        Assert.True(result.NeedsFix);
        Assert.Equal("Bar.cs", result.TargetFile);
    }
}
