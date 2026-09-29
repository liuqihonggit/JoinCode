namespace JoinCode.Reasoning.Tests.Agents;

/// <summary>
/// 用于推理 Agent 测试的伪造实现集合
/// </summary>
internal sealed class FakeQueryEngine : IQueryEngine {
    public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<QueryStreamChunk>();

    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, QueryOptions? options, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<QueryStreamChunk>();

    public IQueryService GetChatCompletionService() => new FakeQueryService((string?)null);

    public IChatClient GetKernel() => new FakeChatClient((string?)null);
}

internal sealed class FakeQueryService : IQueryService {
    private readonly Func<MessageList, string?>? _responseFactory;
    private readonly Exception? _exceptionToThrow;

    public FakeQueryService(string? fixedResponse) {
        _responseFactory = _ => fixedResponse;
    }

    public FakeQueryService(Func<MessageList, string?> responseFactory) {
        _responseFactory = responseFactory;
    }

    public FakeQueryService(Exception exceptionToThrow) {
        _exceptionToThrow = exceptionToThrow;
    }

    public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default) {
        if (_exceptionToThrow is not null) {
            throw _exceptionToThrow;
        }

        var content = _responseFactory?.Invoke(chatHistory);
        return Task.FromResult<IReadOnlyList<ApiMessage>>(
        [
            new ApiMessage(MessageRole.Assistant, content)
            {
                TokenUsage = content is null ? null : new TokenUsage(10, 5),
            },
        ]);
    }

    public IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default) {
        return AsyncEnumerable.Empty<StreamEvent>();
    }
}

internal sealed class FakeChatClient : IChatClient {
    private readonly IQueryService _queryService;

    public FakeChatClient(IQueryService queryService) {
        _queryService = queryService;
    }

    public FakeChatClient(string? fixedResponse)
        : this(new FakeQueryService(fixedResponse)) { }

    public FakeChatClient(Func<MessageList, string?> responseFactory)
        : this(new FakeQueryService(responseFactory)) { }

    public FakeChatClient(Exception exceptionToThrow)
        : this(new FakeQueryService(exceptionToThrow)) { }

    public IToolCollection Plugins => new FakeToolCollection();

    public IQueryService GetChatCompletionService() => _queryService;
}

internal sealed class FakeToolCollection : IToolCollection {
    public string[] GetPluginNames() => [];

    public void Add(IToolGroup plugin) { }

    public IToolGroup? GetPlugin(string name) => null;

    public bool Remove(string name) => false;
}

internal sealed class FakeMessageBroker : IMailbox {
    public List<CoordinatorMessage> SentMessages { get; } = [];
    public List<CoordinatorMessage> BroadcastMessages { get; } = [];

    public void RegisterAgent(string agentId, string? sessionId = null) { }

    public void UnregisterAgent(string agentId) { }

    public Task<bool> SendAsync(string agentId, CoordinatorMessage message, CancellationToken cancellationToken = default) {
        SentMessages.Add(message);
        return Task.FromResult(true);
    }

    public Task BroadcastAsync(CoordinatorMessage message, CancellationToken cancellationToken = default) {
        BroadcastMessages.Add(message);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<CoordinatorMessage> ReceiveAsync(string agentId, CancellationToken cancellationToken = default) {
        return AsyncEnumerable.Empty<CoordinatorMessage>();
    }

    public string[] GetRegisteredAgents() => [];

    public string? GetSessionId(string agentId) => null;
}

/// <summary>
/// 推理 Agent 桩子 — ReasonAsync 返回固定 AgentAction，记录调用次数与收到的上下文，用于断言引擎调用顺序与视锥注册
/// </summary>
internal sealed class StubReasoningAgent : ReasoningAgent {
    private readonly AgentAction _fixedAction;

    /// <summary>ReasonAsync 被调用的次数</summary>
    public int ReasonCallCount { get; private set; }

    /// <summary>历次 ReasonAsync 收到的上下文快照（按调用顺序）</summary>
    public List<ReasoningContext> ReceivedContexts { get; } = [];

    /// <summary>桩子系统提示词</summary>
    public override string SystemPrompt => "桩子Agent";

    /// <summary>
    /// 构造桩子 Agent — 绑定角色与固定返回动作
    /// </summary>
    /// <param name="role">Agent 角色</param>
    /// <param name="fixedAction">ReasonAsync 固定返回的动作；为 null 时返回空动作</param>
    public StubReasoningAgent(AgentRole role, AgentAction? fixedAction = null)
        : base(new FakeQueryEngine(), NullLogger<StubReasoningAgent>.Instance, role, $"桩子{role}") {
        _fixedAction = fixedAction ?? new AgentAction { AgentRole = role };
    }

    /// <summary>
    /// 返回固定动作并记录调用
    /// </summary>
    public override Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct) {
        ReasonCallCount++;
        ReceivedContexts.Add(context);
        return System.Threading.Tasks.Task.FromResult(_fixedAction);
    }
}