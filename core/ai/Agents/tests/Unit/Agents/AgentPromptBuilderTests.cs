namespace Core.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;
using Moq;

public sealed class AgentPromptBuilderTests
{
    [Fact]
    public async Task BuildSystemPromptAsync_WithMcpServers_InjectsServerNames()
    {
        var builder = CreateBuilder();
        var promptContext = new AgentPromptContext
        {
            McpServers = ["filesystem", "git"],
        };

        var result = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null, promptContext);

        result.Should().Contain("filesystem");
        result.Should().Contain("git");
        result.Should().Contain("MCP");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithAvailableSkills_InjectsSkillNames()
    {
        var builder = CreateBuilder();
        var promptContext = new AgentPromptContext
        {
            AvailableSkills = ["commit", "verify"],
        };

        var result = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null, promptContext);

        result.Should().Contain("commit");
        result.Should().Contain("verify");
        result.Should().Contain("技能");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithSettingsSummary_InjectsSummary()
    {
        var builder = CreateBuilder();
        var promptContext = new AgentPromptContext
        {
            SettingsSummary = "权限模式: auto, 模型: gpt-4o",
        };

        var result = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null, promptContext);

        result.Should().Contain("权限模式: auto");
        result.Should().Contain("配置");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithNullPromptContext_BehavesAsBaseOverload()
    {
        var builder = CreateBuilder();

        var resultWithContext = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null, null);
        var resultWithoutContext = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null);

        resultWithContext.Should().Be(resultWithoutContext);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithCriticalSystemReminder_NotInSystemPrompt()
    {
        // CriticalSystemReminder 改为每轮注入消息流(对齐 TS 原版),不在 system prompt 中
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            SystemPrompt = "You are a code agent.",
            CriticalSystemReminder = "<critical>STAY FOCUSED</critical>",
        };
        var builder = CreateBuilder(definition);

        var result = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null);

        result.Should().NotContain("<critical>STAY FOCUSED</critical>");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithoutCriticalSystemReminder_OmitsReminder()
    {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            SystemPrompt = "You are a code agent.",
        };
        var builder = CreateBuilder(definition);

        var result = await builder.BuildSystemPromptAsync(
            ExecutorVariant.Code.ToValue(), "test task", null);

        result.Should().NotContain("critical");
    }

    private static AgentPromptBuilder CreateBuilder(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition = null)
    {
        var definitionProviderMock = new Mock<IAgentDefinitionProvider>();
        definitionProviderMock
            .Setup(x => x.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        definitionProviderMock
            .Setup(x => x.GetAgentDefinitionAsync(It.IsAny<AgentRole>(), It.IsAny<ExecutorVariant?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var contextAccessorMock = new Mock<ISubAgentContextAccessor>();

        return new AgentPromptBuilder(
            definitionProviderMock.Object,
            contextAccessorMock.Object);
    }
}
