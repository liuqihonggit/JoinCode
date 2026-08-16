namespace Api.LLM.Fallback;

/// <summary>
/// 缓冲流式装饰器 — 对齐 TS claude.ts 的 queryModelWithoutStreaming
/// 将 GetApiMessageContentsAsync 实现为"流式请求 + 缓冲整个流"
/// 好处：仍然获得流式的 usage 统计（含 cache_read_input_tokens 等缓存命中信息）
/// </summary>
/// <remarks>
/// TS 的 queryModelWithoutStreaming 并非发送 stream:false 请求，
/// 而是发送 stream:true 请求后缓冲整个流。
/// 这样非流式调用方也能享受流式路径的可靠性 + usage 统计。
///
/// 使用方式：
/// var buffered = new BufferedStreamingDecorator(innerQueryService);
/// // GetApiMessageContentsAsync 内部走流式 + 缓冲
/// // GetStreamEventContentsAsync 直接委托给内部服务
/// </remarks>
public sealed class BufferedStreamingDecorator : IQueryService
{
    private readonly IQueryService _inner;
    private readonly ILogger? _logger;

    public BufferedStreamingDecorator(IQueryService inner, ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger;
    }

    /// <summary>
    /// 非流式调用 — 内部走流式 + 缓冲整个流
    /// 对齐 TS: queryModelWithoutStreaming = stream:true + 收集最终 AssistantMessage
    /// </summary>
    public async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var contentBuilder = new StringBuilder();
        var metadata = new Dictionary<string, JsonElement>();
        string? modelId = null;
        MessageRole role = MessageRole.Assistant;
        TokenUsage? usage = null;

        await foreach (var evt in _inner.GetStreamEventContentsAsync(
            chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false))
        {
            if (evt.Role.HasValue)
                role = evt.Role.Value;

            if (evt.Content != null)
                contentBuilder.Append(evt.Content);

            if (evt.ModelId != null)
                modelId = evt.ModelId;

            if (evt.Metadata != null)
            {
                foreach (var kvp in evt.Metadata)
                {
                    switch (kvp.Key)
                    {
                        case "Usage":
                            try
                            {
                                usage = kvp.Value.Deserialize(NativeJsonContext.Default.TokenUsage);
                            }
                            catch (JsonException ex)
                            {
                                _logger?.LogWarning(ex, "Failed to deserialize TokenUsage from stream event metadata");
                            }
                            break;
                        case "FinishReason":
                        case "Id":
                        case "Created":
                            metadata[kvp.Key] = kvp.Value;
                            break;
                        case "AllToolCalls":
                            metadata["AllToolCalls"] = kvp.Value;
                            break;
                    }
                }
            }
        }

        if (usage != null)
        {
            metadata["Usage"] = JsonElementHelper.FromObject(usage, NativeJsonContext.Default.TokenUsage);
        }

        var message = new ApiMessage(role, contentBuilder.ToString(), metadata, modelId, usage);

        return [message];
    }

    /// <summary>
    /// 流式调用 — 直接委托给内部服务（流式不需要缓冲）
    /// </summary>
    public IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetStreamEventContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
    }
}
