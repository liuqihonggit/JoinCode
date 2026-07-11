
namespace Core.Tests.Agents;

public class AgentPromptsTests
{
    [Theory]
    [InlineData(BuiltInAgentType.Plan)]
    [InlineData(BuiltInAgentType.Explore)]
    [InlineData(BuiltInAgentType.Verification)]
    [InlineData(BuiltInAgentType.GeneralPurpose)]
    [InlineData(BuiltInAgentType.ClaudeCodeGuide)]
    public void GetSystemPrompt_ReturnsNonEmptyString(BuiltInAgentType agentType)
    {
        // Act
        var prompt = AgentPrompts.GetSystemPrompt(agentType);

        // Assert
        Assert.NotNull(prompt);
        Assert.NotEmpty(prompt);
        Assert.Contains("##", prompt);
    }

    [Theory]
    [InlineData(BuiltInAgentType.Plan, "计划")]
    [InlineData(BuiltInAgentType.Explore, "探索")]
    [InlineData(BuiltInAgentType.Verification, "验证")]
    [InlineData(BuiltInAgentType.GeneralPurpose, "通用")]
    [InlineData(BuiltInAgentType.ClaudeCodeGuide, "Claude Code")]
    public void SystemPrompts_ContainRelevantKeywords(BuiltInAgentType agentType, string expectedKeyword)
    {
        // Act
        var prompt = AgentPrompts.GetSystemPrompt(agentType);

        // Assert
        Assert.Contains(expectedKeyword, prompt);
    }

    [Fact]
    public void PlanAgentSystemPrompt_ContainsPlanningGuidelines()
    {
        // Act
        var prompt = AgentPrompts.PlanAgentSystemPrompt;

        // Assert
        Assert.Contains("计划制定", prompt);
        Assert.Contains("执行步骤", prompt);
        Assert.Contains("输出格式", prompt);
    }

    [Fact]
    public void ExploreAgentSystemPrompt_ContainsExplorationGuidelines()
    {
        // Act
        var prompt = AgentPrompts.ExploreAgentSystemPrompt;

        // Assert
        Assert.Contains("代码库", prompt);
        Assert.Contains("探索策略", prompt);
        Assert.Contains("关键目录", prompt);
    }

    [Fact]
    public void VerificationAgentSystemPrompt_ContainsVerificationGuidelines()
    {
        // Act
        var prompt = AgentPrompts.VerificationAgentSystemPrompt;

        // Assert
        Assert.Contains("验证", prompt);
        Assert.Contains("代码", prompt);
        Assert.Contains("最佳实践", prompt);
    }

    [Fact]
    public void GeneralPurposeAgentSystemPrompt_ContainsGeneralGuidelines()
    {
        // Act
        var prompt = AgentPrompts.GeneralPurposeAgentSystemPrompt;

        // Assert
        Assert.Contains("通用", prompt);
        Assert.Contains("工作原则", prompt);
        Assert.Contains("能力范围", prompt);
    }

    [Fact]
    public void ClaudeCodeGuideAgentSystemPrompt_ContainsGuideGuidelines()
    {
        // Act
        var prompt = AgentPrompts.ClaudeCodeGuideAgentSystemPrompt;

        // Assert
        Assert.Contains("Claude Code", prompt);
        Assert.Contains("功能介绍", prompt);
        Assert.Contains("使用步骤", prompt);
    }

    [Fact]
    public void AllPrompts_AreInChinese()
    {
        // Arrange
        var prompts = new[]
        {
            AgentPrompts.PlanAgentSystemPrompt,
            AgentPrompts.ExploreAgentSystemPrompt,
            AgentPrompts.VerificationAgentSystemPrompt,
            AgentPrompts.GeneralPurposeAgentSystemPrompt,
            AgentPrompts.ClaudeCodeGuideAgentSystemPrompt
        };

        // Act & Assert
        foreach (var prompt in prompts)
        {
            Assert.NotNull(prompt);
            // 检查是否包含中文字符
            Assert.Matches(@"[\u4e00-\u9fa5]", prompt);
        }
    }
}
