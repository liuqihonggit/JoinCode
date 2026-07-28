namespace Sync.Tests.ToolHandlers;

public class AgentToolHandlersTests
{
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IAgentCoordinator> _coordinator = new();
    private readonly AgentToolHandlers _handler;

    public AgentToolHandlersTests()
    {
        _agentService.Setup(x => x.SpawnAgentAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentSpawnOptions opt, CancellationToken _) => new JoinCode.Abstractions.Interfaces.AgentInfo
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Description = opt.Description,
                AgentType = opt.AgentType,
                Status = opt.RunInBackground ? AgentStatus.Running : AgentStatus.Completed,
                IsolationMode = opt.IsolationMode,
                StartedAt = DateTime.UtcNow
            });

        _agentService.Setup(x => x.WaitForAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult
            {
                AgentId = "test-agent",
                Success = true,
                Output = "代理执行完成"
            });

        _agentService.Setup(x => x.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => id == "nonexistent" ? null : new JoinCode.Abstractions.Interfaces.AgentInfo
            {
                Id = id,
                Description = "测试代理",
                Status = AgentStatus.Running
            });

        _agentService.Setup(x => x.StopAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _agentService.Setup(x => x.GetAvailableAgentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentTypeInfo>
            {
                new() { Name = "general", Description = "通用代理" },
                new() { Name = "code-explorer", Description = "代码探索代理" }
            });

        _coordinator.Setup(x => x.GetRunningAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningAgentInfo>
            {
                new()
                {
                    Id = "agent-1",
                    Description = "正在修复bug",
                    AgentType = "general",
                    StartedAt = DateTime.UtcNow,
                    State = AgentStatus.Running
                },
                new()
                {
                    Id = "agent-2",
                    Description = "正在分析代码",
                    AgentType = "code-explorer",
                    StartedAt = DateTime.UtcNow.AddMinutes(-5),
                    State = AgentStatus.Running
                }
            });

        // 构建空管道 — 测试非 CreateAgent 方法不需要中间件
        var pipeline = new MiddlewarePipeline<Tools.Handlers.AgentToolContext>([]);

        _handler = new AgentToolHandlers(pipeline, _agentService.Object, _coordinator.Object,
            NullLogger<AgentToolHandlers>.Instance, null);
    }

    [Fact]
    public async Task AgentListAsync_ReturnsRunningAgents()
    {
        var result = await _handler.AgentListAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        var text = result.GetTextContent();
        Assert.Contains("agent-1", text);
        Assert.Contains("agent-2", text);
        Assert.Contains("正在修复bug", text);
    }

    [Fact]
    public async Task AgentListAsync_NoCoordinator_ReturnsError()
    {
        var pipeline = new MiddlewarePipeline<Tools.Handlers.AgentToolContext>([]);
        var handler = new AgentToolHandlers(pipeline, _agentService.Object, coordinator: null,
            NullLogger<AgentToolHandlers>.Instance, null);

        var result = await handler.AgentListAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("未初始化", result.GetTextContent());
    }

    [Fact]
    public async Task AgentListAsync_EmptyList_ReturnsNoAgentsMessage()
    {
        _coordinator.Setup(x => x.GetRunningAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningAgentInfo>());

        var result = await _handler.AgentListAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("没有正在运行", result.GetTextContent());
    }
}