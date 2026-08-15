namespace Host.Tests.Entry;

/// <summary>
/// SubAgentMentionParser 单元测试 — @agentName 语法解析与子代理匹配
/// </summary>
public class SubAgentMentionParserTests
{
    [Fact]
    public void Parse_ValidAtMention_ReturnsAgentNameAndMessage()
    {
        var result = JoinCode.Entry.SubAgentMentionParser.Parse("@explorer 查找所有TODO");
        Assert.NotNull(result);
        Assert.Equal("explorer", result.Value.AgentName);
        Assert.Equal("查找所有TODO", result.Value.Message);
    }

    [Fact]
    public void Parse_AtMentionWithExtraSpaces_PreservesMessageContent()
    {
        var result = JoinCode.Entry.SubAgentMentionParser.Parse("@agent  hello   world  ");
        Assert.NotNull(result);
        Assert.Equal("agent", result.Value.AgentName);
        Assert.Equal("hello   world", result.Value.Message);
    }

    [Fact]
    public void Parse_NonAtInput_ReturnsNull()
    {
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("hello world"));
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("/help"));
    }

    [Fact]
    public void Parse_AtWithoutMessage_ReturnsNull()
    {
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("@agent"));
    }

    [Fact]
    public void Parse_AtWithEmptyMessage_ReturnsNull()
    {
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("@agent "));
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("@agent   "));
    }

    [Fact]
    public void Parse_EmptyOrNullInput_ReturnsNull()
    {
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse(""));
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.Parse("   "));
    }

    [Fact]
    public void FindAgent_MatchesByDisplayName_CaseInsensitive()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-001", Description = "explore task", DisplayName = "explorer" },
            new RunningAgentInfo { Id = "agent-002", Description = "plan task", DisplayName = "planner" }
        };
        var match = JoinCode.Entry.SubAgentMentionParser.FindAgent("EXPLORER", agents);
        Assert.NotNull(match);
        Assert.Equal("agent-001", match.Id);
    }

    [Fact]
    public void FindAgent_MatchesByDescription_WhenDisplayNameNotFound()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-001", Description = "explore task", DisplayName = null }
        };
        var match = JoinCode.Entry.SubAgentMentionParser.FindAgent("explore task", agents);
        Assert.NotNull(match);
        Assert.Equal("agent-001", match.Id);
    }

    [Fact]
    public void FindAgent_MatchesByIdPrefix_WhenNameNotFound()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-abc123", Description = "task", DisplayName = null }
        };
        var match = JoinCode.Entry.SubAgentMentionParser.FindAgent("agent-abc", agents);
        Assert.NotNull(match);
        Assert.Equal("agent-abc123", match.Id);
    }

    [Fact]
    public void FindAgent_NoMatch_ReturnsNull()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-001", Description = "task", DisplayName = "explorer" }
        };
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.FindAgent("nonexistent", agents));
    }

    [Fact]
    public void FindAgent_EmptyList_ReturnsNull()
    {
        Assert.Null(JoinCode.Entry.SubAgentMentionParser.FindAgent("anything", Array.Empty<RunningAgentInfo>()));
    }

    [Fact]
    public void FindAgent_DisplayNameTakesPrecedenceOverDescription()
    {
        var agents = new[]
        {
            new RunningAgentInfo { Id = "agent-001", Description = "name", DisplayName = "name" },
            new RunningAgentInfo { Id = "agent-002", Description = "name", DisplayName = "other" }
        };
        var match = JoinCode.Entry.SubAgentMentionParser.FindAgent("name", agents);
        Assert.NotNull(match);
        Assert.Equal("agent-001", match.Id);
    }
}
