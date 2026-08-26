
namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Execution;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// T5.0: Worker 主循环查邮箱 — ContractChangeNotifications 队列消费测试
/// 验证每轮 LLM 调用前消费契约变更通知，追加到 chatHistory
/// </summary>
public sealed class AgentBaseContractChangeTests
{
    private static Mock<IQueryEngine> CreateQueryEngineMock()
    {
        var mock = new Mock<IQueryEngine>();
        mock.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_WithContractChange_ShouldAddNotificationToChatHistory()
    {
        var queryEngine = CreateQueryEngineMock();
        var initialMessages = new MessageList();
        initialMessages.AddSystemMessage("test system");

        var options = new SubAgentOptions { MaxIterations = 1, InitialMessageList = initialMessages };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue("IFoo 接口签名变更");
        agent.ContractChangeNotifications = queue;

        await agent.ExecuteAsync();

        initialMessages.Should().Contain(m => m.Content != null && m.Content.Contains("[契约变更通知]") && m.Content.Contains("IFoo 接口签名变更"));
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleContractChanges_ShouldAddAllNotifications()
    {
        var queryEngine = CreateQueryEngineMock();
        var initialMessages = new MessageList();
        initialMessages.AddSystemMessage("test system");

        var options = new SubAgentOptions { MaxIterations = 1, InitialMessageList = initialMessages };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue("变更1: IFoo");
        queue.Enqueue("变更2: IBar");
        agent.ContractChangeNotifications = queue;

        await agent.ExecuteAsync();

        initialMessages.Should().Contain(m => m.Content != null && m.Content.Contains("变更1: IFoo"));
        initialMessages.Should().Contain(m => m.Content != null && m.Content.Contains("变更2: IBar"));
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyContractChangeQueue_ShouldNotAddNotification()
    {
        var queryEngine = CreateQueryEngineMock();
        var initialMessages = new MessageList();
        initialMessages.AddSystemMessage("test system");

        var options = new SubAgentOptions { MaxIterations = 1, InitialMessageList = initialMessages };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);
        agent.ContractChangeNotifications = new ConcurrentQueue<string>();

        await agent.ExecuteAsync();

        initialMessages.Should().NotContain(m => m.Content != null && m.Content.Contains("[契约变更通知]"));
    }

    [Fact]
    public async Task ExecuteAsync_WithNullContractChangeQueue_ShouldNotThrow()
    {
        var queryEngine = CreateQueryEngineMock();
        var options = new SubAgentOptions { MaxIterations = 1 };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);

        var act = () => agent.ExecuteAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_ContractChangeQueue_ShouldBeDrainedAfterExecution()
    {
        var queryEngine = CreateQueryEngineMock();
        var initialMessages = new MessageList();
        initialMessages.AddSystemMessage("test system");

        var options = new SubAgentOptions { MaxIterations = 1, InitialMessageList = initialMessages };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue("变更内容");
        agent.ContractChangeNotifications = queue;

        await agent.ExecuteAsync();

        queue.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithMaturedDeferredMail_ShouldAddToChatHistory()
    {
        var queryEngine = CreateQueryEngineMock();
        var initialMessages = new MessageList();
        initialMessages.AddSystemMessage("test system");

        var options = new SubAgentOptions { MaxIterations = 1, InitialMessageList = initialMessages };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);

        var mail = new DeferredMail
        {
            To = agent.ObjectId.UniqueId,
            From = "w1",
            Subject = "测试文件冲突",
            Body = "Foo.test.cs 变更",
            OpenAfterTurns = 1,
            Marker = MailMarker.TestFileConflict,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var mailMock = new Mock<IDeferredMailService>();
        mailMock.Setup(x => x.TickTurns(It.IsAny<string>())).Returns(new List<DeferredMail> { mail }.AsReadOnly());
        agent.DeferredMailService = mailMock.Object;

        await agent.ExecuteAsync();

        initialMessages.Should().Contain(m => m.Content != null && m.Content.Contains("[延迟邮件]") && m.Content.Contains("Foo.test.cs 变更"));
    }

    [Fact]
    public async Task ExecuteAsync_WithNullDeferredMailService_ShouldNotThrow()
    {
        var queryEngine = CreateQueryEngineMock();
        var options = new SubAgentOptions { MaxIterations = 1 };
        var agent = new AgentBase("test task", options, queryEngine.Object, null);

        var act = () => agent.ExecuteAsync();
        await act.Should().NotThrowAsync();
    }
}
