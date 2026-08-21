namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.LLM.Execution;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// AgentBase CriticalSystemReminder 每轮注入测试
/// 验证 criticalSystemReminder 作为 user message 注入到消息流(对齐 claude code re-injected at every user turn)
/// </summary>
public sealed class AgentBaseCriticalSystemReminderTests
{
    [Fact]
    public async Task ExecuteAsync_CriticalSystemReminder_InjectsAsUserMessage()
    {
        MessageList? capturedHistory = null;
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, MessageList history, QueryOptions? _, CancellationToken _) => capturedHistory = history)
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var options = new SubAgentOptions { CriticalSystemReminder = "CRITICAL: stay focused" };
        var agent = new AgentBase("test task", options, queryEngineMock.Object, null);

        await agent.ExecuteAsync();

        capturedHistory.Should().NotBeNull();
        var history = capturedHistory ?? new MessageList();
        history
            .Where(m => m.Role == MessageRole.User && m.Content is not null && m.Content.Contains("CRITICAL: stay focused"))
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_NoCriticalSystemReminder_DoesNotInjectExtraUserMessage()
    {
        MessageList? capturedHistory = null;
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, MessageList history, QueryOptions? _, CancellationToken _) => capturedHistory = history)
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var options = new SubAgentOptions();
        var agent = new AgentBase("test task", options, queryEngineMock.Object, null);

        await agent.ExecuteAsync();

        capturedHistory.Should().NotBeNull();
        var history = capturedHistory ?? new MessageList();
        history
            .Where(m => m.Role == MessageRole.User)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CriticalSystemReminder_ReInjectedOnSecondExecution()
    {
        var capturedHistories = new List<MessageList>();
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, MessageList history, QueryOptions? _, CancellationToken _) => capturedHistories.Add(history))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var options = new SubAgentOptions { CriticalSystemReminder = "CRITICAL: verify only" };
        var agent = new AgentBase("test task", options, queryEngineMock.Object, null);

        await agent.ExecuteAsync();
        await agent.ExecuteAsync();

        capturedHistories.Should().HaveCount(2);
        capturedHistories[0]
            .Where(m => m.Role == MessageRole.User && m.Content is not null && m.Content.Contains("CRITICAL: verify only"))
            .Should().HaveCount(1, "第一轮应注入");
        capturedHistories[1]
            .Where(m => m.Role == MessageRole.User && m.Content is not null && m.Content.Contains("CRITICAL: verify only"))
            .Should().HaveCount(1, "第二轮应再次注入");
    }
}
