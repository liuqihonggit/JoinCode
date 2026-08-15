namespace Core.Agents.Coordinator;

/// <summary>
/// 主代理 — 继承 AgentBase，走同一条 ExecuteStreamAsync 路径
/// 当前阶段: 覆写 ExecuteStreamAsync 委托到 ChatService 13中间件管道，保持全部功能
/// 后续阶段: 将 ChatService 中间件迁移到 QueryEngine 后，移除覆写，使用基类实现
/// </summary>
public sealed class MainAgent : AgentBase
{
    private readonly IChatService _chatService;
    private string _currentInput = string.Empty;

    /// <summary>
    /// 当前用户输入 — 每轮对话前由 SessionController 设置
    /// </summary>
    public string CurrentInput
    {
        get => _currentInput;
        set => _currentInput = value;
    }

    /// <summary>
    /// 主代理构造函数
    /// </summary>
    /// <param name="chatService">聊天服务 — 13中间件管道</param>
    /// <param name="queryEngine">查询引擎 — 当前未使用，后续迁移中间件后启用</param>
    /// <param name="logger">日志器</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="name">代理名称</param>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="contextManager">上下文管理器</param>
    public MainAgent(
        IChatService chatService,
        IQueryEngine queryEngine,
        ILogger? logger = null,
        IClockService? clock = null,
        string? name = null,
        string? systemPrompt = null,
        ObjectId sessionId = default,
        IChatContextManager? contextManager = null)
        : base(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: logger,
            clock: clock,
            name: name ?? "main",
            role: AgentRole.Coordinator,
            systemPrompt: systemPrompt,
            sessionId: sessionId,
            contextManager: contextManager)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// 覆写提示词构建 — 返回当前用户输入而非 Task 字段
    /// </summary>
    protected override string BuildPrompt() => _currentInput;

    /// <summary>
    /// 覆写流式执行 — 委托到 ChatService 13中间件管道，转换为 AgentStreamChunk 统一输出
    /// 输出 channel 写入在此统一处理，与子代理的 AgentBase.ExecuteStreamAsync 保持一致
    /// </summary>
    public override async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        StartedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Running;
        _executionCount++;

        if (Context is not null)
        {
            Context.StartedAt = StartedAt;
            Context.Status = AgentStatus.Running;
        }

        var responseBuilder = new StringBuilder();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TokenUsage? lastUsage = null;
        string? lastModelId = null;

        await foreach (var evt in _chatService.StreamWithEventsAsync(_currentInput, linkedToken).ConfigureAwait(false))
        {
            var chunk = AgentStreamChunk.FromChatStreamEvent(evt, UniqueId);
            if (chunk is null) continue;

            if (chunk.Type == AgentStreamChunkType.Content && !string.IsNullOrEmpty(chunk.Content))
            {
                responseBuilder.Append(chunk.Content);
                if (OutputChannelManager is not null)
                {
                    OutputChannelManager.Write(UniqueId, Options.DisplayName ?? Name, chunk.Content, AgentOutputChunkType.Text);
                }
            }
            else if (chunk.Type == AgentStreamChunkType.Complete)
            {
                lastUsage = chunk.Usage;
                lastModelId = chunk.ModelId;
            }

            yield return chunk;
        }

        stopwatch.Stop();
        CompletedAt = _clock.GetUtcNow();
        Status = TaskExecutionStatus.Completed;

        if (Context is not null)
        {
            Context.CompletedAt = CompletedAt;
            Context.Status = AgentStatus.Completed;
        }

        var finalOutput = responseBuilder.ToString();
        Output = finalOutput;

        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = finalOutput,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            Usage = lastUsage,
            ModelId = lastModelId,
            AgentId = UniqueId
        };
    }
}
