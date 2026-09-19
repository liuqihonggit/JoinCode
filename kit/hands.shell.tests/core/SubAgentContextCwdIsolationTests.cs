namespace Hands.Shell.Tests;

/// <summary>
/// SubAgentContext cwd 隔离单元测试 — 验证 AsyncLocal CwdOverride 机制
/// 缺陷: shell 链路未读 GetEffectiveCwd,导致跨 worktree cwd 污染(TASK002)
/// </summary>
public sealed class SubAgentContextCwdIsolationTests
{
    [Fact]
    public void GetEffectiveCwd_WithCwdOverride_ReturnsOverride()
    {
        var expectedCwd = Path.GetTempPath();
        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
            CwdOverride = expectedCwd,
        };

        using (context.EnterScopeWithCwd(expectedCwd))
        {
            SubAgentContext.GetEffectiveCwd().Should().Be(expectedCwd);
        }
    }

    [Fact]
    public void GetEffectiveCwd_WithoutContext_ReturnsFallback()
    {
        var fallback = "/tmp/fallback";
        SubAgentContext.Current.Should().BeNull();
        SubAgentContext.GetEffectiveCwd(fallback).Should().Be(fallback);
    }

    [Fact]
    public void GetEffectiveCwd_WithoutOverride_ReturnsFallback()
    {
        var fallback = "/tmp/fallback";
        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
        };

        using (context.EnterScope())
        {
            SubAgentContext.GetEffectiveCwd(fallback).Should().Be(fallback);
        }
    }

    [Fact]
    public void GetEffectiveCwd_ScopeExited_ReturnsFallback()
    {
        var overrideCwd = Path.GetTempPath();
        var fallback = "/tmp/fallback";
        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
        };

        (context.EnterScopeWithCwd(overrideCwd) as IDisposable)?.Dispose();

        SubAgentContext.Current.Should().BeNull();
        SubAgentContext.GetEffectiveCwd(fallback).Should().Be(fallback);
    }

    [Fact]
    public async Task GetEffectiveCwd_AsyncFlow_PreservesCwdOverrideAcrossAwait()
    {
        var expectedCwd = Path.GetTempPath();
        var context = new SubAgentContext
        {
            AgentId = "agent-test",
            Role = AgentRole.Executor,
            Task = "test",
        };

        using (context.EnterScopeWithCwd(expectedCwd))
        {
            await Task.Yield();
            SubAgentContext.GetEffectiveCwd().Should().Be(expectedCwd);
        }
    }
}
