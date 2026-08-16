namespace JoinCode.Abstractions.Models.Agent;

public sealed class AgentIsolationModeRemoteTests
{
    [Fact]
    public void FromValue_Remote_ReturnsRemote()
    {
        AgentIsolationModeExtensions.FromValue("remote").Should().Be(AgentIsolationMode.Remote);
    }

    [Fact]
    public void ToValue_Remote_ReturnsRemoteString()
    {
        AgentIsolationMode.Remote.ToValue().Should().Be("remote");
    }
}
