namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// JccChatSession.GetAvailableSubAgentsAsync 测试 — 验证从 IAgentDefinitionProvider 提取
/// 全部代理定义并映射为 SubAgentSummary（Name=DisplayId, Description, DisplayId）。
/// 任务2红测试：JccChatSession 未显式实现时走接口默认方法返回空，与期望非空冲突。
/// </summary>
public class JccChatSessionSubAgentTests {
    /// <summary>注册 IAgentDefinitionProvider 后，GetAvailableSubAgentsAsync 返回其全部代理定义</summary>
    [Fact]
    public async Task GetAvailableSubAgentsAsync_ReturnsFromAgentDefinitionProvider() {
        var mockProvider = new MockAgentDefinitionProvider([
            new() { Role = AgentRole.Coordinator, WhenToUse = "General tasks", Description = "Coordinator agent" },
            new() { Role = AgentRole.Executor, Variant = ExecutorVariant.Code, WhenToUse = "Code editing", Description = "Code agent" },
        ]);
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDefinitionProvider>(mockProvider);
        var sp = services.BuildServiceProvider();
        await using var session = new JccChatSession(sp, null!, new WorkflowConfig {
            Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
        });

        var agents = await session.GetAvailableSubAgentsAsync();

        agents.Should().HaveCount(2);
        agents.Should().Contain(a => a.DisplayId == "coordinator" && a.Name == "coordinator");
        agents.Should().Contain(a => a.DisplayId == "executor:code" && a.Name == "executor:code");
    }

    /// <summary>未注册 IAgentDefinitionProvider 时返回空列表（兜底）</summary>
    [Fact]
    public async Task GetAvailableSubAgentsAsync_NoProvider_ReturnsEmpty() {
        var sp = new ServiceCollection().BuildServiceProvider();
        await using var session = new JccChatSession(sp, null!, new WorkflowConfig {
            Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
        });

        var agents = await session.GetAvailableSubAgentsAsync();

        agents.Should().BeEmpty();
    }

    /// <summary>Description 为空时回退 WhenToUse 作为展示描述</summary>
    [Fact]
    public async Task GetAvailableSubAgentsAsync_MapsDescription_WhenToUseFallback() {
        var mockProvider = new MockAgentDefinitionProvider([
            new() { Role = AgentRole.Coordinator, WhenToUse = "General fallback", Description = null },
        ]);
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDefinitionProvider>(mockProvider);
        var sp = services.BuildServiceProvider();
        await using var session = new JccChatSession(sp, null!, new WorkflowConfig {
            Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
        });

        var agents = await session.GetAvailableSubAgentsAsync();

        agents.Should().HaveCount(1);
        agents[0].Description.Should().Be("General fallback");
    }

    /// <summary>StopAllSubAgentsAsync 遍历后台代理对 running/paused/pending 状态逐个终止</summary>
    [Fact]
    public async Task StopAllSubAgentsAsync_TerminatesAllActiveAgents() {
        var stopped = new List<string>();
        var agentMock = new Mock<IAgentService>();
        agentMock.Setup(x => x.GetRunningAgentsAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<RunningAgentInfo> {
                     new() { Id = "a1", Description = "agent-a1", State = AgentStatus.Running },
                     new() { Id = "a2", Description = "agent-a2", State = AgentStatus.Paused },
                     new() { Id = "a3", Description = "agent-a3", State = AgentStatus.Pending },
                 });
        agentMock.Setup(x => x.StopAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, CancellationToken>((id, _) => stopped.Add(id))
                 .ReturnsAsync(true);
        var services = new ServiceCollection();
        services.AddSingleton(agentMock.Object);
        var sp = services.BuildServiceProvider();
        await using var session = new JccChatSession(sp, null!, new WorkflowConfig {
            Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
        });

        var count = await session.StopAllSubAgentsAsync();

        count.Should().Be(3);
        stopped.Should().BeEquivalentTo(["a1", "a2", "a3"]);
    }

    /// <summary>StopSubAgentAsync 委托 StopBackgroundAgentAsync — agentId 命中 IAgentService 时返回 true</summary>
    [Fact]
    public async Task StopSubAgentAsync_DelegatesToAgentServiceStop() {
        var agentMock = new Mock<IAgentService>();
        agentMock.Setup(x => x.StopAgentAsync("hit", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
        var services = new ServiceCollection();
        services.AddSingleton(agentMock.Object);
        var sp = services.BuildServiceProvider();
        await using var session = new JccChatSession(sp, null!, new WorkflowConfig {
            Provider = new ProviderConfig { Vendor = "openai", ModelId = "gpt-4o" }
        });

        var ok = await session.StopSubAgentAsync("hit");

        ok.Should().BeTrue();
        agentMock.Verify(x => x.StopAgentAsync("hit", It.IsAny<CancellationToken>()), Times.Once);
    }
}

/// <summary>mock IAgentDefinitionProvider — 返回预设代理定义列表，供 GetAvailableSubAgentsAsync 测试</summary>
internal sealed class MockAgentDefinitionProvider : IAgentDefinitionProvider {
    private readonly List<AgentDefinition> _definitions;
    public MockAgentDefinitionProvider(List<AgentDefinition> definitions) => _definitions = definitions;

    public Task<List<AgentDefinition>> GetAgentDefinitionsAsync(string? workingDirectory = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_definitions);

    public Task<AgentDefinition?> GetAgentDefinitionAsync(AgentRole role, ExecutorVariant? variant = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
        => Task.FromResult<AgentDefinition?>(null);

    public void ClearCache() { }
}