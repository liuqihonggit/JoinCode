namespace JoinCode.Abs.Tests.Agents;

/// <summary>
/// @提及语法解析器公共化后的契约测试（自 JoinCode.Entry 内部类迁入，
/// CLI ReplLoopStep 与 GUI 消息路由共用同一实现）。
/// </summary>
public class SubAgentMentionParserTests
{
    [Fact]
    public void Parse_ValidMention_ShouldSplitNameAndMessage()
    {
        var parsed = SubAgentMentionParser.Parse("@explore 帮我查一下README");

        parsed.Should().NotBeNull();
        parsed!.Value.AgentName.Should().Be("explore");
        parsed.Value.Message.Should().Be("帮我查一下README");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("普通消息")]
    [InlineData("@")]
    [InlineData("@explore")]
    [InlineData("@   消息")]
    [InlineData("@explore   ")]
    public void Parse_InvalidInputs_ShouldReturnNull(string? input)
    {
        SubAgentMentionParser.Parse(input!).Should().BeNull();
    }

    [Fact]
    public void FindAgent_Priority_DisplayName_ThenDescription_ThenIdPrefix()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-ccc", Description = "通用任务" },
            new RunningAgentInfo { Id = "agent-bbb", Description = "搜索专家", DisplayName = "seeker" },
            new RunningAgentInfo { Id = "agent-aaa", Description = "调研任务" },
        };

        // DisplayName 精确
        SubAgentMentionParser.FindAgent("seeker", agents)!.Id.Should().Be("agent-bbb");
        // Description 精确
        SubAgentMentionParser.FindAgent("调研任务", agents)!.Id.Should().Be("agent-aaa");
        // Id 前缀
        SubAgentMentionParser.FindAgent("agent-c", agents)!.Id.Should().Be("agent-ccc");
        // 未命中
        SubAgentMentionParser.FindAgent("nope", agents).Should().BeNull();
    }
}
