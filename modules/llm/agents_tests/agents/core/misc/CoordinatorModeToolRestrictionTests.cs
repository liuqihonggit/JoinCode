// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
﻿namespace Core.Agents;


/// <summary>
/// Coordinator 模式工具集限制测试
/// 验证 JCC_COORDINATOR_MODE 启用时 Coordinator Profile 工具集限制为 [Agent, SendMessage, TaskStop]
/// </summary>
public sealed class CoordinatorModeToolRestrictionTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task GetProfile_CoordinatorModeEnabled_RestrictsToolsToAgentSendMessageTaskStop() {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "1");
        try {
            await using var registry = new AgentRoleProfileRegistry();
            registry.RegisterBuiltInProfiles();
            var profile = registry.GetProfile(AgentRole.Coordinator);

            profile.Should().NotBeNull();
            profile!.AllowedTools.Should().NotBeNull();
            profile.AllowedTools.Should().Contain(AgentToolNameEnumConstants.Agent);
            profile.AllowedTools.Should().Contain(AgentToolNameEnumConstants.AgentSendMessage);
            profile.AllowedTools.Should().Contain(TaskToolNameEnumConstants.TaskStop);
            profile.AllowedTools.Should().HaveCount(3);
        } finally {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task GetProfile_CoordinatorModeDisabled_AllowsAllTools() {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();
        var profile = registry.GetProfile(AgentRole.Coordinator);

        profile.Should().NotBeNull();
        profile!.AllowedTools.Should().BeEmpty();
    }
}