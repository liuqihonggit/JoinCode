namespace Sync.Tests.Agents;

/// <summary>
/// 子代理类型字符串 → 角色/变体解析 — Agent 工具必须用同一结果构造 SubAgentContext，
/// 否则只读变体会以「空工具域 = 不限制」运行。
/// </summary>
public sealed class AgentSubagentTypeResolverTests {
    [Theory]
    [InlineData("executor:explore", AgentRole.Executor, ExecutorVariant.Explore)]
    [InlineData("executor:search", AgentRole.Executor, ExecutorVariant.Search)]
    [InlineData("executor:plan", AgentRole.Executor, ExecutorVariant.Plan)]
    [InlineData("executor:code", AgentRole.Executor, ExecutorVariant.Code)]
    [InlineData("explore", AgentRole.Executor, ExecutorVariant.Explore)]
    [InlineData("executor:explore,worker", AgentRole.Executor, ExecutorVariant.Explore)]
    public void Resolve_MapsDisplayIdToRoleAndVariant(string subagentType, AgentRole role, ExecutorVariant variant) {
        var identity = AgentSubagentTypeResolver.Resolve(subagentType);

        identity.Role.Should().Be(role);
        identity.Variant.Should().Be(variant);
    }

    [Theory]
    [InlineData("coordinator", AgentRole.Coordinator, null)]
    [InlineData("prosecutor", AgentRole.Prosecutor, null)]
    public void Resolve_MapsRoleOnly(string subagentType, AgentRole role, ExecutorVariant? variant) {
        var identity = AgentSubagentTypeResolver.Resolve(subagentType);

        identity.Role.Should().Be(role);
        identity.Variant.Should().Be(variant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("custom-agent")]
    [InlineData("executor:unknown-variant")]
    public void Resolve_UnknownOrEmpty_FallsBackToPlainExecutor(string? subagentType) {
        var identity = AgentSubagentTypeResolver.Resolve(subagentType);

        identity.Role.Should().Be(AgentRole.Executor);
        identity.Variant.Should().BeNull();
    }
}
