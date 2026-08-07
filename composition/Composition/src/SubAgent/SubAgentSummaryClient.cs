namespace JoinCode.Composition.SubAgent;

/// <summary>
/// 子智能体摘要客户端实现 — L2 自摘要层调 LLM 压缩子智能体输出
/// <para>在 Composition 层实现，注入 IChatClient（Abstractions），调 LLM 生成连贯摘要。</para>
/// <para>对齐 Dream ChatCompletionClient 的调用模式：GetChatCompletionService → GetApiMessageContentsAsync。</para>
/// </summary>
[Register(typeof(ISubAgentSummaryClient))]
public sealed partial class SubAgentSummaryClient : ServiceEntity, ISubAgentSummaryClient
{
    [Inject] private readonly ILogger<SubAgentSummaryClient>? _logger;
    private readonly IChatClient _kernel;

    public SubAgentSummaryClient(IChatClient kernel, ILogger<SubAgentSummaryClient>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> SummarizeAsync(string text, string agentId, int maxOutputTokens, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            var chatHistory = new MessageList
            {
                new(MessageRole.System, BuildSystemPrompt(maxOutputTokens)),
                new(MessageRole.User, text),
            };

            var completion = _kernel.GetChatCompletionService();
            var results = await completion.GetApiMessageContentsAsync(chatHistory, cancellationToken: cancellationToken).ConfigureAwait(false);
            var summary = results.FirstOrDefault()?.Content;

            if (string.IsNullOrEmpty(summary))
            {
                _logger?.LogWarning("子智能体 {AgentId} L2 摘要 LLM 返回空", agentId);
                return null;
            }

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "子智能体 {AgentId} L2 摘要 LLM 调用失败", agentId);
            return null;
        }
    }

    private static string BuildSystemPrompt(int maxOutputTokens)
    {
        return $"你是摘要助手。请将用户提供的文本压缩成不超过 {maxOutputTokens} token 的连贯摘要，保留关键信息、结论和重要数据。直接输出摘要内容，不要加任何前缀或解释。";
    }
}
