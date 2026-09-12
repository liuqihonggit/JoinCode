namespace Tools.Handlers;

public sealed class AsyncAgentAllowedToolsTests
{
    [Fact]
    public void AsyncAgentAllowedTools_Contains_FileRead()
    {
        AsyncAgentAllowedTools.Tools.Should().Contain(FileToolNameConstants.FileRead);
    }

    [Fact]
    public void AsyncAgentAllowedTools_Contains_Bash()
    {
        AsyncAgentAllowedTools.Tools.Should().Contain(ShellToolNameConstants.Bash);
    }

    [Fact]
    public void AsyncAgentAllowedTools_DoesNotContain_AgentSpawn()
    {
        AsyncAgentAllowedTools.Tools.Should().NotContain(AgentToolNameConstants.AgentSpawn);
    }

    [Fact]
    public void AsyncAgentAllowedTools_DoesNotContain_AskUser()
    {
        AsyncAgentAllowedTools.Tools.Should().NotContain("ask_user");
        AsyncAgentAllowedTools.Tools.Should().NotContain("AskUser");
    }

    [Fact]
    public void AsyncAgentAllowedTools_IsNotEmpty()
    {
        AsyncAgentAllowedTools.Tools.Should().NotBeEmpty();
    }
}
