
namespace Api.Chat;

public sealed partial class PipeQueryService : IQueryService {
    private readonly PipeTransportConfig _config;
    private readonly ILogger<PipeQueryService>? _logger;
    private readonly HttpClient _httpClient;
    private readonly ResilientHttpExecutor? _resilientExecutor;

    /// <summary>
    /// 构造管道查询服务。
    /// </summary>
    /// <param name="config">管道传输配置。</param>
    /// <param name="apiKey">API 密钥，可选。</param>
    /// <param name="logger">日志记录器，可选。</param>
    /// <param name="resilientExecutor">弹性 HTTP 执行器，可选。</param>
    public PipeQueryService(PipeTransportConfig config, string? apiKey = null, ILogger<PipeQueryService>? logger = null, ResilientHttpExecutor? resilientExecutor = null) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _httpClient = CreatePipeHttpClient(config, apiKey);
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

        // 累积工具调用信息（流式响应中 tool_calls 可能跨多个 chunk）
        string? toolCallId = null;
        string? toolCallName = null;
        var toolCallArguments = new StringBuilder();

        await foreach (var chunk in responseStream) {
            if (chunk.Choices.Count == 0) continue;

            var choice = chunk.Choices[0];
            var content = choice.Delta?.Content?.Text ?? string.Empty;
            var role = ConvertRole(choice.Delta?.Role);

            // 检测流式 tool_calls
            if (choice.Delta?.ToolCalls?.Count > 0) {
                foreach (var tc in choice.Delta.ToolCalls) {
                    if (tc.Id != null) toolCallId = tc.Id;
                    if (tc.Function?.Name != null) toolCallName = tc.Function.Name;
                    if (tc.Function?.Arguments != null) toolCallArguments.Append(tc.Function.Arguments);
                }
            }

            // finish_reason = tool_calls 时，输出完整的工具调用信息
            if (choice.FinishReason == OpenAIFinishReasonEnumConstants.ToolCalls && toolCallName != null) {
                yield return new StreamEvent(role, content, chunk.Model,
                    new Dictionary<string, JsonElement> {
                        ["Id"] = JsonElementHelper.FromString(chunk.Id),
                        ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                        ["Created"] = JsonElementHelper.FromInt64(chunk.Created),
                        ["AllToolCalls"] = ToolCallEntry.ToToolCallsJson([
                            new() { Id = toolCallId, Name = toolCallName, Arguments = toolCallArguments.ToString() }
                        ])
                    });

                // 重置累积状态
                toolCallId = null;
                toolCallName = null;
                toolCallArguments.Clear();
                continue;
            }

            yield return new StreamEvent(role, content, chunk.Model,
                new Dictionary<string, JsonElement> {
                    ["Id"] = JsonElementHelper.FromString(chunk.Id),
                    ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                    ["Created"] = JsonElementHelper.FromInt64(chunk.Created)
                });
        }
    }

    private HttpClient CreatePipeHttpClient(PipeTransportConfig config, string? apiKey) {
        // P1-13: 添加 PooledConnectionLifetime 解决 DNS 不刷新（保留自定义 ConnectCallback 用于管道协议）
        // 决策: 管道通信必须自定义 ConnectCallback（NamedPipeClientStream），不能用 IHttpClientProvider 替代
        var handler = SocketsHttpHandlerFactory.CreateWithDnsRefresh();
        handler.ConnectCallback = async (context, cancellationToken) => {
            var pipeClient = new NamedPipeClientStream(
                serverName: ".",
                pipeName: config.PipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            _logger?.LogInformation("Connecting to pipe: {PipeName}", config.PipeName);

            await pipeClient.ConnectAsync(config.ConnectionTimeoutMs, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Connected to pipe: {PipeName}", config.PipeName);

            return pipeClient;
        };

        var client = new HttpClient(handler) {
            Timeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs),
            BaseAddress = new Uri("http://localhost/")
        };

        client.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(apiKey)) {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        return client;
    }

    private ChatRequest CreateChatRequest(MessageList chatHistory, ChatOptions? settings, bool stream) {
        var messages = chatHistory.Select(ConvertToMessage).ToList();

        return new ChatRequest {
            Model = settings?.ExtensionData?.TryGetValue("model", out var model) == true && model.ValueKind == JsonValueKind.String ? model.GetString() ?? DefaultModelCatalog.FallbackModel : DefaultModelCatalog.FallbackModel, // P1-⑥ 委托统一数据源
            Messages = messages,
            Stream = stream,
            Temperature = settings?.Temperature,
            MaxTokens = settings?.MaxTokens
        };
    }

    private async Task<OpenAIChatResponse> SendRequestAsync(ChatRequest request, CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(request, PipeJsonContext.Default.ChatRequest);

        _logger?.LogDebug("Sending chat request to pipe");

        var response = await QueryServiceBase.SendWithResilienceCoreAsync(
            _httpClient, _resilientExecutor, json, "/v1/chat/completions", "Pipe.ChatCompletion", cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = RelaxedJsonSerializer.Deserialize(responseJson, PipeJsonContext.Default.OpenAIChatResponse);

        if (result == null) {
            throw new InvalidOperationException("Failed to deserialize response from pipe");
        }

        return result;
    }

    private async IAsyncEnumerable<OpenAIChatChunk> SendStreamingRequestAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(request, PipeJsonContext.Default.ChatRequest);

        _logger?.LogDebug("Sending streaming chat request to pipe");

        var response = await QueryServiceBase.SendWithResilienceCoreAsync(
            _httpClient, _resilientExecutor, json, "/v1/chat/completions", "Pipe.StreamingChatCompletion", cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = stream.AsUtf8Reader();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null) {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") yield break;

            var chunk = RelaxedJsonSerializer.Deserialize(data, PipeJsonContext.Default.OpenAIChatChunk);
            if (chunk != null) {
                yield return chunk;
            }
        }
    }

    private static ApiMessage ConvertToApiMessage(OpenAIChoice choice) {
        var message = choice.Message;
        var role = ConvertRole(message.Role);

        // 处理 tool_calls 响应
        if (message.ToolCalls?.Count > 0) {
            var entries = message.ToolCalls.Select(tc => new ToolCallEntry {
                Id = tc.Id,
                Name = tc.Function?.Name ?? "",
                Arguments = tc.Function?.Arguments ?? "{}"
            }).ToList();
            return new ApiMessage(role, message.Content?.Text,
                new Dictionary<string, JsonElement> {
                    ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                    ["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries)
                });
        }

        return new ApiMessage(role, message.Content?.Text,
            new Dictionary<string, JsonElement> { ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason) });
    }

    private static MessageRole ConvertRole(string? role)
        => QueryServiceBase.ConvertRole(role);

    private static OpenAIApiMessage ConvertToMessage(ApiMessage content) {
        var msg = new OpenAIApiMessage {
            Role = QueryServiceBase.ConvertRoleToString(content.Role),
            Content = content.Content
        };

        // Tool 角色消息必须带 tool_call_id
        if (content.Role == MessageRole.Tool && content.Metadata != null) {
            if (content.Metadata.TryGetValue("ToolCallId", out var tcIdEl) && tcIdEl.ValueKind == JsonValueKind.String)
                msg.ToolCallId = tcIdEl.GetString();
            if (content.Metadata.TryGetValue("ToolName", out var tcNameEl) && tcNameEl.ValueKind == JsonValueKind.String)
                msg.Name = tcNameEl.GetString();
        }

        // Assistant 消息带工具调用时，需要包含 tool_calls
        if (content.Role == MessageRole.Assistant && content.Metadata != null &&
            content.Metadata.TryGetValue("ToolCalls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array) {
            var toolCalls = new List<OpenAIToolCall>();
            foreach (var tcItem in tcEl.EnumerateArray()) {
                var tc = new OpenAIToolCall();
                if (tcItem.TryGetProperty("Id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    tc.Id = idEl.GetString();
                if (tcItem.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    tc.Function = new OpenAIToolCallFunction {
                        Name = nameEl.GetString(),
                        Arguments = tcItem.TryGetProperty("Arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String
                            ? argsEl.GetString() : "{}"
                    };
                toolCalls.Add(tc);
            }
            msg.ToolCalls = toolCalls;
        }

        return msg;
    }


    internal sealed class ChatRequest {
        /// <summary>获取或设置模型名称。</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>获取或设置消息列表。</summary>
        [JsonPropertyName("messages")]
        public List<OpenAIApiMessage> Messages { get; set; } = new();

        /// <summary>获取或设置是否流式。</summary>
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        /// <summary>获取或设置温度参数。</summary>
        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? Temperature { get; set; }

        /// <summary>获取或设置最大 Token 数。</summary>
        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
    }
}