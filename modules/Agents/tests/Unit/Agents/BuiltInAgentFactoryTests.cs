
namespace Core.Tests.Agents;

public class BuiltInAgentFactoryTests
{
    private readonly IChatClient _kernel;
    private readonly IFileSystem _fs;
    private readonly BuiltInAgentFactory _factory;

    public BuiltInAgentFactoryTests()
    {
        _kernel = JoinCode.Llm.DependencyInjection.ServiceRegistration.CreateEmptyKernel();
        _fs = new IO.FileSystem.PhysicalFileSystem();

        _factory = new BuiltInAgentFactory(
            _kernel,
            _fs,
            JoinCode.Abstractions.Clock.SystemClockService.Instance);
    }

    [Theory]
    [InlineData(BuiltInAgentType.Plan, typeof(PlanAgent))]
    [InlineData(BuiltInAgentType.Explore, typeof(ExploreAgent))]
    [InlineData(BuiltInAgentType.Verification, typeof(VerificationAgent))]
    [InlineData(BuiltInAgentType.GeneralPurpose, typeof(GeneralPurposeAgent))]
    [InlineData(BuiltInAgentType.ClaudeCodeGuide, typeof(ClaudeCodeGuideAgent))]
    public void CreateAgent_WithValidType_ReturnsCorrectAgentType(BuiltInAgentType agentType, Type expectedType)
    {
        var agent = _factory.CreateAgent(agentType);

        Assert.IsType(expectedType, agent);
    }

    [Fact]
    public void CreateAgent_AllAgentsImplementIBuiltInAgent()
    {
        var agentTypes = _factory.GetAvailableAgentTypes()
            .Where(t => t != BuiltInAgentType.ContextCompression);

        foreach (var agentType in agentTypes)
        {
            var agent = _factory.CreateAgent(agentType);
            Assert.IsAssignableFrom<IBuiltInAgent>(agent);
        }
    }

    [Theory]
    [InlineData(BuiltInAgentType.Plan, "PlanAgent", "制定清晰、可执行的任务计划")]
    [InlineData(BuiltInAgentType.Explore, "ExploreAgent", "探索代码库结构")]
    [InlineData(BuiltInAgentType.Verification, "VerificationAgent", "验证代码的正确性")]
    [InlineData(BuiltInAgentType.GeneralPurpose, "GeneralPurposeAgent", "处理各种通用任务")]
    [InlineData(BuiltInAgentType.ClaudeCodeGuide, "ClaudeCodeGuideAgent", "帮助用户了解和使用 Claude Code")]
    public void GetAgentConfig_ReturnsCorrectConfig(BuiltInAgentType agentType, string expectedName, string expectedDescription)
    {
        var config = _factory.GetAgentConfig(agentType);

        Assert.NotNull(config);
        Assert.Equal(expectedName, config.Name);
        Assert.Contains(expectedDescription, config.Description);
        Assert.Equal(agentType, config.AgentType);
        Assert.NotNull(config.SystemPrompt);
    }

    [Fact]
    public void GetAvailableAgentTypes_ReturnsAllSixTypes()
    {
        var types = _factory.GetAvailableAgentTypes().ToList();

        Assert.Equal(6, types.Count);
        Assert.Contains(BuiltInAgentType.Plan, types);
        Assert.Contains(BuiltInAgentType.Explore, types);
        Assert.Contains(BuiltInAgentType.Verification, types);
        Assert.Contains(BuiltInAgentType.GeneralPurpose, types);
        Assert.Contains(BuiltInAgentType.ClaudeCodeGuide, types);
        Assert.Contains(BuiltInAgentType.ContextCompression, types);
    }

    [Fact]
    public void CreateAgent_WithInvalidType_ThrowsArgumentException()
    {
        var invalidType = (BuiltInAgentType)999;

        Assert.Throws<ArgumentException>(() => _factory.CreateAgent(invalidType));
    }

    [Fact]
    public void CreatedAgents_HaveCorrectProperties()
    {
        var agentTypes = _factory.GetAvailableAgentTypes()
            .Where(t => t != BuiltInAgentType.ContextCompression);

        foreach (var agentType in agentTypes)
        {
            var agent = _factory.CreateAgent(agentType);
            var config = _factory.GetAgentConfig(agentType);

            Assert.NotNull(agent);
            Assert.Equal(config!.Name, agent.Name);
            Assert.Equal(config.Description, agent.Description);
            Assert.Equal(agentType, agent.AgentType);
            Assert.Equal(config.SystemPrompt, agent.SystemPrompt);
        }
    }
}
