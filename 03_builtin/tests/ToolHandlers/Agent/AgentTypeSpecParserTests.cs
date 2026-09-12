namespace Tools.Handlers;

public sealed class AgentTypeSpecParserTests
{
    [Fact]
    public void Parse_SingleType_ReturnsPrimaryOnly()
    {
        var (primary, allowed) = AgentTypeSpecParser.Parse("worker");
        primary.Should().Be("worker");
        allowed.Should().BeNull();
    }

    [Fact]
    public void Parse_CommaSeparated_ReturnsAllTypes()
    {
        var (primary, allowed) = AgentTypeSpecParser.Parse("worker,researcher");
        primary.Should().Be("worker");
        allowed.Should().NotBeNull();
        allowed.Should().Contain("worker");
        allowed.Should().Contain("researcher");
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var (primary, allowed) = AgentTypeSpecParser.Parse(null);
        primary.Should().BeEmpty();
        allowed.Should().BeNull();
    }

    [Fact]
    public void IsAllowed_WhenNullAllowed_ReturnsTrue()
    {
        AgentTypeSpecParser.IsAllowed("worker", null).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_WhenTypeInList_ReturnsTrue()
    {
        AgentTypeSpecParser.IsAllowed("worker", ["worker", "researcher"]).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_WhenTypeNotInList_ReturnsFalse()
    {
        AgentTypeSpecParser.IsAllowed("explorer", ["worker", "researcher"]).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_CaseInsensitive()
    {
        AgentTypeSpecParser.IsAllowed("WORKER", ["worker", "researcher"]).Should().BeTrue();
    }
}
