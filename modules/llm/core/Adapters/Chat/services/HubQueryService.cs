
namespace Api.Chat;

public sealed partial class HubQueryService : IQueryService {
    private readonly HubTransportConfig _config;
    private readonly ILogger<HubQueryService>? _logger;
    private readonly HttpClient _httpClient;
    private readonly ResilientHttpExecutor? _resilientExecutor;

    /// <summary>
    /// 构造 Hub 查询服务 — 通过 HTTP 连接 AgentHubServer 转发 LLM 请求。
    /// </summary>
    /// <param name="config">Hub 传输配置。</param>
    /// <param name="apiKey">API 密钥（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <param name="resilientExecutor">弹性 HTTP 执行器（可选）。</param>
    public HubQueryService(
        HubTransportConfig config,
        string? apiKey = null,
        ILogger<HubQueryService>? logger = null,
        ResilientHttpExecutor? resilientExecutor = null) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _httpClient = CreateHubHttpClient(config, apiKey);
        _resilientExecutor = resilientExecutor;
    }

    /// <summary>获取非流式聊天响应消息列表。</summary>
    public async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default) {
        var request = CreateChatRequest(chatHistory, executionSettings, stream: false);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        return response.Choices.Select(ConvertToApiMessage).ToList();
    }

    /// <summary>获取流式聊天事件枚举。</summary>
    public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var request = CreateChatRequest(chatHistory, executionSettings, stream: true);
        var responseStream = SendStreamingRequestAsync(request, cancellationToken);

        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        await foreach (var chunk in responseStream) {
            if (chunk.Choices.Count == 0) continue;

            var choice = chunk.Choices[0];
            var content = choice.Delta?.Content?.Text ?? string.Empty;
            var role = ConvertRole(choice.Delta?.Role);

            if (choice.Delta?.ToolCalls?.Count > 0) {
                foreach (var tc in choice.Delta.ToolCalls)
                    OpenAIQueryService.ProcessToolCallDelta(tc, toolCallAccumulator);
            }

            if (choice.FinishReason == OpenAIFinishReasonEnumConstants.ToolCalls && toolCallAccumulator.Count > 0) {
                var entries = toolCallAccumulator
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new ToolCallEntry { Id = kv.Value.Id, Name = kv.Value.Name, Arguments = kv.Value.Arguments.ToString() })
                    .ToList();
                yield return new StreamEvent(role, content, chunk.Model,
                    new Dictionary<string, JsonElement> {
                        ["Id"] = JsonElementHelper.FromString(chunk.Id),
                        ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                        ["Created"] = JsonElementHelper.FromInt64(chunk.Created),
                        ["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries)
                    });

                toolCallAccumulator.Clear();
                continue;
            }

            yield return new StreamEvent(role, content, chunk.Model,
                new Dictionary<string, JsonElement> {
                    ["Id"] = JsonElementHelper.FromString(chunk.Id),
                    ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                    ["Created"] = JsonElementHelper.FromInt64(chunk.Created)
                });
        }

        if (toolCallAccumulator.Count > 0) {
            var flushEntries = toolCallAccumulator
                .OrderBy(kv => kv.Key)
                .Select(kv => new ToolCallEntry { Id = kv.Value.Id, Name = kv.Value.Name, Arguments = kv.Value.Arguments.ToString() })
                .ToList();
            yield return new StreamEvent(MessageRole.Assistant, null, null,
                new Dictionary<string, JsonElement> {
                    ["Id"] = JsonElementHelper.FromString("flush"),
                    ["FinishReason"] = JsonElementHelper.FromString(OpenAIFinishReasonEnumConstants.ToolCalls),
                    ["Created"] = JsonElementHelper.FromInt64(0),
                    ["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(flushEntries)
                });
        }
    }

    private HttpClient CreateHubHttpClient(HubTransportConfig config, string? apiKey) {
        var handler = SocketsHttpHandlerFactory.CreateWithDnsRefresh();
        var client = new HttpClient(handler) {
            Timeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs),
            BaseAddress = new Uri(config.Endpoint)
        };

        client.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(apiKey)) {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        if (!string.IsNullOrEmpty(config.AgentId)) {
            client.DefaultRequestHeaders.Add("X-Agent-Id", config.AgentId);
        }

        return client;
    }

    private ChatRequest CreateChatRequest(MessageList chatHistory, ChatOptions? settings, bool stream) {
        var messages = chatHistory.Select(ConvertToMessage).ToList();

        return new ChatRequest {
            Model = settings?.ExtensionData?.TryGetValue("model", out var model) == true && model.ValueKind == JsonValueKind.String ? model.GetString() ?? DefaultModelCatalog.FallbackModel : DefaultModelCatalog.FallbackModel,
            Messages = messages,
            Stream = stream,
            Temperature = settings?.Temperature,
            MaxTokens = settings?.MaxTokens
        };
    }

    private async Task<OpenAIChatResponse> SendRequestAsync(ChatRequest request, CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(request, HubJsonContext.Default.ChatRequest);

        _logger?.LogDebug("Sending chat request to hub: {Endpoint}", _config.Endpoint);

        var response = await QueryServiceBase.SendWithResilienceCoreAsync(
            _httpClient, _resilientExecutor, json, "/v1/chat/completions", "Hub.ChatCompletion", cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = RelaxedJsonSerializer.Deserialize(responseJson, HubJsonContext.Default.OpenAIChatResponse);

        if (result == null) {
            throw new InvalidOperationException("Failed to deserialize response from hub");
        }

        return result;
    }

    private async IAsyncEnumerable<OpenAIChatChunk> SendStreamingRequestAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(request, HubJsonContext.Default.ChatRequest);

        _logger?.LogDebug("Sending streaming chat request to hub: {Endpoint}", _config.Endpoint);

        var response = await QueryServiceBase.SendWithResilienceCoreAsync(
            _httpClient, _resilientExecutor, json, "/v1/chat/completions", "Hub.StreamingChatCompletion", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = stream.AsUtf8Reader();

        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var idleTimeout = TimeSpan.FromMilliseconds(Math.Max(_config.RequestTimeoutMs, 1_000));

        string? line;
        while (true) {
            idleCts.CancelAfter(idleTimeout);
            try {
                line = await reader.ReadLineAsync(idleCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException(
                    $"SSE 流式响应超过 {idleTimeout.TotalSeconds:F0}s 无新数据（hub {_config.Endpoint}）。" +
                    "触发点：HubQueryService.SendStreamingRequestAsync 逐行读取空闲超时。" +
                    "处理指引：检查 AgentHubServer 是否仍在推送流；正常但响应慢时上调 HubTransportConfig.RequestTimeoutMs。");
            }

            if (line == null) yield break;
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") yield break;

            var chunk = RelaxedJsonSerializer.Deserialize(data, HubJsonContext.Default.OpenAIChatChunk);
            if (chunk != null) {
                yield return chunk;
            }
        }
    }

    internal static ApiMessage ConvertToApiMessage(OpenAIChoice choice)
        => PipeQueryService.ConvertToApiMessage(choice);

    private static MessageRole ConvertRole(string? role)
        => QueryServiceBase.ConvertRole(role);

    internal static OpenAIApiMessage ConvertToMessage(ApiMessage content)
        => PipeQueryService.ConvertToMessage(content);


    internal sealed class ChatRequest {
        /// <summary>模型名称。</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>消息列表。</summary>
        [JsonPropertyName("messages")]
        public List<OpenAIApiMessage> Messages { get; set; } = new();

        /// <summary>是否启用流式响应。</summary>
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        /// <summary>采样温度。</summary>
        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? Temperature { get; set; }

        /// <summary>最大生成 token 数。</summary>
        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
    }
}

/// <summary>Hub 传输配置 — AgentHubServer 连接参数。</summary>
public sealed class HubTransportConfig {
    /// <summary>AgentHubServer 的 HTTP endpoint 地址。</summary>
    public string Endpoint { get; init; } = "http://localhost:3457";

    /// <summary>当前 jcc 实例的 Agent 标识符。</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>请求超时时间（毫秒）。</summary>
    public int RequestTimeoutMs { get; init; } = 90_000;

    /// <summary>连接超时时间（毫秒）。</summary>
    public int ConnectionTimeoutMs { get; init; } = 5_000;
}
