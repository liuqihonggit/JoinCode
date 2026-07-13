namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 推理Agent基类 — 提供LLM调用 + 消息通信的通用能力
/// </summary>
public abstract class ReasoningAgentBase : IReasoningAgent
{
    protected readonly ILogger _logger;
    private readonly IChatClient? _chatClient;
    private readonly IAgentMessageBroker? _messageBroker;

    public abstract AgentRole Role { get; }
    public abstract string Name { get; }
    public abstract string SystemPrompt { get; }

    protected ReasoningAgentBase(
        ILogger logger,
        IChatClient? chatClient = null,
        IAgentMessageBroker? messageBroker = null)
    {
        _logger = logger;
        _chatClient = chatClient;
        _messageBroker = messageBroker;
    }

    public abstract Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct);

    /// <summary>
    /// 调用LLM获取结构化响应
    /// </summary>
    protected async Task<(string? Content, TokenUsage? Usage)> CallLlmAsync(string userPrompt, float temperature = 0.3f, int maxTokens = 2000, CancellationToken ct = default)
    {
        if (_chatClient is null) return (null, null);

        var chatService = _chatClient.GetChatCompletionService();
        var chatHistory = new MessageList
        {
            new ApiMessage(MessageRole.System, SystemPrompt),
            new ApiMessage(MessageRole.User, userPrompt),
        };

        var options = new ChatOptions { Temperature = temperature, MaxTokens = maxTokens };

        try
        {
            var results = await chatService.GetApiMessageContentsAsync(chatHistory, options, _chatClient, ct).ConfigureAwait(false);
            var result = results.FirstOrDefault();
            return (result?.Content, result?.TokenUsage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{AgentName}] LLM调用失败", Name);
            return (null, null);
        }
    }

    /// <summary>
    /// 向指定Agent发送消息
    /// </summary>
    protected async Task SendMessageAsync(string toAgentId, string messageType, string content, CancellationToken ct = default)
    {
        if (_messageBroker is null) return;

        var message = new CoordinatorMessage
        {
            FromAgentId = Role.ToValue(),
            ToAgentId = toAgentId,
            MessageType = messageType,
            Content = content,
        };

        await _messageBroker.SendMessageAsync(toAgentId, message, ct).ConfigureAwait(false);
        _logger.LogDebug("[{AgentName}] 发送消息 → {ToAgent}: {Type}", Name, toAgentId, messageType);
    }

    /// <summary>
    /// 广播消息给所有已注册Agent
    /// </summary>
    protected async Task BroadcastAsync(string messageType, string content, CancellationToken ct = default)
    {
        if (_messageBroker is null) return;

        var message = new CoordinatorMessage
        {
            FromAgentId = Role.ToValue(),
            ToAgentId = "broadcast",
            MessageType = messageType,
            Content = content,
        };

        await _messageBroker.BroadcastAsync(message, ct).ConfigureAwait(false);
        _logger.LogDebug("[{AgentName}] 广播消息: {Type}", Name, messageType);
    }

    /// <summary>
    /// 从JSON字符串中提取第一个JSON对象
    /// </summary>
    protected static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    /// <summary>
    /// 解析信任度枚举
    /// </summary>
    protected static TrustLevel ParseTrustLevel(string? value) => value switch
    {
        "DirectEvidence" => TrustLevel.DirectEvidence,
        "StrongCorroboration" => TrustLevel.StrongCorroboration,
        "Weak" => TrustLevel.Weak,
        "Hearsay" => TrustLevel.Hearsay,
        "Unreliable" => TrustLevel.Unreliable,
        _ => TrustLevel.Moderate,
    };
}
