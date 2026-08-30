namespace Core.Agents;


public sealed class SubAgentOptionsInitialPromptTests
{
    [Fact]
    public void InitialPrompt_CanBeSet()
    {
        var options = new SubAgentOptions
        {
            InitialPrompt = "/setup",
        };

        options.InitialPrompt.Should().Be("/setup");
    }

    [Fact]
    public void InitialPrompt_DefaultNull()
    {
        var options = new SubAgentOptions();

        options.InitialPrompt.Should().BeNull();
    }
}
