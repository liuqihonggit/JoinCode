namespace Core.Agents;


/// <summary>
/// Coordinator 模式工具集限制测试
/// 验证 JCC_COORDINATOR_MODE 启用时 Coordinator Profile 工具集限制为 [Agent, SendMessage, TaskStop]
/// </summary>
public sealed class CoordinatorModeToolRestrictionTests
{
    [Fact]
    public void GetProfile_CoordinatorModeEnabled_RestrictsToolsToAgentSendMessageTaskStop()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "1");
        try
        {
            var registry = new AgentRoleProfileRegistry();
            var profile = registry.GetProfile(AgentRole.Coordinator);

            profile.Should().NotBeNull();
            profile!.AllowedTools.Should().NotBeNull();
            profile.AllowedTools.Should().Contain(AgentToolNameConstants.Agent);
            profile.AllowedTools.Should().Contain(AgentToolNameConstants.AgentSendMessage);
            profile.AllowedTools.Should().Contain(TaskToolNameConstants.TaskStop);
            profile.AllowedTools.Should().HaveCount(3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [Fact]
    public void GetProfile_CoordinatorModeDisabled_AllowsAllTools()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        var registry = new AgentRoleProfileRegistry();
        var profile = registry.GetProfile(AgentRole.Coordinator);

        profile.Should().NotBeNull();
        profile!.AllowedTools.Should().BeEmpty();
    }
}
