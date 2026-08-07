namespace JoinCode.Composition.ContextFold;

/// <summary>
/// 上下文折叠摘要器实现 — L4 历史压缩层调 LLM 把对话头部摘要化
/// <para>在 Composition 层实现，注入 IChatClient（Abstractions），调 LLM 生成 head 摘要。</para>
/// <para>对齐 SubAgentSummaryClient 的调用模式：GetChatCompletionService → GetApiMessageContentsAsync。</para>
/// </summary>
[Register(typeof(IFoldSummarizer))]
public sealed partial class FoldSummarizer : ServiceEntity, IFoldSummarizer
{
    [Inject] private readonly ILogger<FoldSummarizer>? _logger;
    private readonly IChatClient _kernel;

    public FoldSummarizer(IChatClient kernel, ILogger<FoldSummarizer>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> SummarizeForFoldAsync(
        IReadOnlyList<ApiMessage> headMessages,
        CancellationToken cancellationToken = default)
    {
        if (headMessages.Count == 0)
            return string.Empty;

        var transcript = BuildTranscript(headMessages);
        var chatHistory = new MessageList
        {
            new(MessageRole.System, SystemPrompt),
            new(MessageRole.User, transcript),
        };

        var completion = _kernel.GetChatCompletionService();
        var results = await completion.GetApiMessageContentsAsync(chatHistory, cancellationToken: cancellationToken).ConfigureAwait(false);
        var summary = results.FirstOrDefault()?.Content;

        if (string.IsNullOrEmpty(summary))
        {
            throw new InvalidOperationException("L4 折叠摘要 LLM 返回空");
        }

        return summary;
    }

    private static string BuildTranscript(IReadOnlyList<ApiMessage> messages)
    {
        return string.Join("\n", messages.Select(msg =>
        {
            var role = msg.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                MessageRole.Tool => "tool",
                _ => msg.Role.ToString(),
            };
            return $"[{role}]: {msg.Content ?? string.Empty}";
        }));
    }

    private const string SystemPrompt =
        "你是对话历史摘要助手。请将用户提供的对话历史压缩成连贯的摘要，" +
        "保留关键信息、用户意图、重要决策和工具调用结果的核心结论。" +
        "省略冗余细节和中间过程，直接输出摘要内容，不要加任何前缀或解释。";
}
