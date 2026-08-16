
namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 推理智能体基类 — 继承 AgentBase，自动获得 Entity 身份 + IChatContextManager 压缩管线
/// 额外提供 LLM 调用 + 消息通信能力
/// 子类: ProsecutorAgent, JudgeAgent, DefenderAgent
/// </summary>
public abstract class ReasoningAgent : AgentBase
{
    protected new readonly ILogger _logger;
    protected readonly IChatClient? _chatClient;
    protected readonly IMailbox? _messageBroker;

    /// <summary>
    /// 人格提示词 — 定义 Agent 的行为准则和推理策略
    /// </summary>
    public new abstract string SystemPrompt { get; }

    /// <summary>
    /// 执行推理 — 接收完整上下文，返回动作
    /// </summary>
    public abstract Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct);

    protected ReasoningAgent(
        IQueryEngine queryEngine,
        ILogger logger,
        AgentRole role,
        string name,
        IChatClient? chatClient = null,
        IMailbox? messageBroker = null)
        : base(string.Empty, null, queryEngine, logger, name: name, role: role)
    {
        _logger = logger;
        _chatClient = chatClient;
        _messageBroker = messageBroker;
    }

    /// <summary>
    /// 调用LLM获取结构化响应
    /// </summary>
    protected async Task<(string? Content, TokenUsage? Usage, int EstimatedPromptTokens)> CallLlmAsync(string userPrompt, float temperature = 0.3f, int maxTokens = 2000, CancellationToken ct = default)
    {
        if (_chatClient is null) return (null, null, 0);

        var estimatedPromptTokens = PromptBudgetEstimator.Estimate(SystemPrompt, userPrompt);

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
            return (result?.Content, result?.TokenUsage, estimatedPromptTokens);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[{AgentName}] LLM调用失败", Name);
            return (null, null, estimatedPromptTokens);
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

        await _messageBroker.SendAsync(toAgentId, message, ct).ConfigureAwait(false);
        _logger?.LogDebug("[{AgentName}] 发送消息 → {ToAgent}: {Type}", Name, toAgentId, messageType);
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
        _logger?.LogDebug("[{AgentName}] 广播消息: {Type}", Name, messageType);
    }

    /// <summary>
    /// 从 LLM 输出中提取 JSON 对象
    /// </summary>
    protected static string? ExtractJsonObject(string content, ILogger? logger = null)
    {
        var json = LlmJsonHelper.ExtractJsonBlock(content);
        if (json is not null)
        {
            var repairResult = LlmJsonHelper.RepairJson(json, logger);
            return repairResult.Success ? repairResult.RepairedJson : json;
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        var inlineJson = content[start..(end + 1)];
        var inlineRepair = LlmJsonHelper.RepairJson(inlineJson, logger);
        return inlineRepair.Success ? inlineRepair.RepairedJson : inlineJson;
    }

    /// <summary>
    /// 如果 ContextManager 可用且 prompt 超预算，则压缩
    /// </summary>
    protected async Task<string> CompressPromptIfNeededAsync(ReasoningContext context, AgentRole role, string userPrompt, CancellationToken ct)
    {
        if (ContextManager is null) return userPrompt;

        var estimatedTokens = PromptBudgetEstimator.Estimate(userPrompt);
        if (estimatedTokens <= context.Options.MaxPromptTokens) return userPrompt;

        var decision = ContextManager.DecideAfterUsage(new TokenUsage(estimatedTokens, 0));
        if (decision is ContextFoldDecision.None) return userPrompt;

        await ContextManager.FoldIfNeededAsync(decision, ct).ConfigureAwait(false);
        var messages = await ContextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        var lastUser = messages.LastOrDefault(m => m.Role == MessageRole.User);
        return lastUser?.Content ?? userPrompt;
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
