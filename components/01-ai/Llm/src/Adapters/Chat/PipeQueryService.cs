
namespace Api.Chat;

public sealed partial class PipeQueryService : IQueryService
{
    private readonly PipeTransportConfig _config;
    [Inject] private readonly ILogger<PipeQueryService>? _logger;
    private readonly HttpClient _httpClient;

    public PipeQueryService(PipeTransportConfig config, string? apiKey = null, ILogger<PipeQueryService>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _httpClient = CreatePipeHttpClient(config, apiKey);
    }

    public async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(chatHistory, executionSettings, stream: false);
        var response = await SendRequestAsync(request, cancellationToken);

        return response.Choices.Select(ConvertToApiMessage).ToList();
    }

    public async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(chatHistory, executionSettings, stream: true);
        var responseStream = SendStreamingRequestAsync(request, cancellationToken);

        // 累积工具调用信息（流式响应中 tool_calls 可能跨多个 chunk）
        string? toolCallId = null;
        string? toolCallName = null;
        var toolCallArguments = new StringBuilder();

        await foreach (var chunk in responseStream)
        {
            if (chunk.Choices.Count == 0) continue;

            var choice = chunk.Choices[0];
            var content = choice.Delta?.Content ?? string.Empty;
            var role = ConvertRole(choice.Delta?.Role);

            // 检测流式 tool_calls
            if (choice.Delta?.ToolCalls?.Count > 0)
            {
                foreach (var tc in choice.Delta.ToolCalls)
                {
                    if (tc.Id != null) toolCallId = tc.Id;
                    if (tc.Function?.Name != null) toolCallName = tc.Function.Name;
                    if (tc.Function?.Arguments != null) toolCallArguments.Append(tc.Function.Arguments);
                }
            }

            // finish_reason = tool_calls 时，输出完整的工具调用信息
            if (choice.FinishReason == OpenAIFinishReasonConstants.ToolCalls && toolCallName != null)
            {
                yield return new StreamEvent(role, content, chunk.Model,
                    new Dictionary<string, JsonElement>
                    {
                        ["Id"] = JsonElementHelper.FromString(chunk.Id),
                        ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                        ["Created"] = JsonElementHelper.FromInt64(chunk.Created),
                        ["ToolCall"] = JsonElementHelper.FromString(toolCallName),
                        ["ToolCallId"] = JsonElementHelper.FromString(toolCallId ?? ""),
                        ["ToolCallArguments"] = JsonElementHelper.FromString(toolCallArguments.ToString())
                    });

                // 重置累积状态
                toolCallId = null;
                toolCallName = null;
                toolCallArguments.Clear();
                continue;
            }

            yield return new StreamEvent(role, content, chunk.Model,
                new Dictionary<string, JsonElement>
                {
                    ["Id"] = JsonElementHelper.FromString(chunk.Id),
                    ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                    ["Created"] = JsonElementHelper.FromInt64(chunk.Created)
                });
        }
    }

    private HttpClient CreatePipeHttpClient(PipeTransportConfig config, string? apiKey)
    {
        // P1-13: 添加 PooledConnectionLifetime 解决 DNS 不刷新（保留自定义 ConnectCallback 用于管道协议）
        // 决策: 管道通信必须自定义 ConnectCallback（NamedPipeClientStream），不能用 IHttpClientProvider 替代
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var pipeClient = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: config.PipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous);

                _logger?.LogInformation("Connecting to pipe: {PipeName}", config.PipeName);

                await pipeClient.ConnectAsync(config.ConnectionTimeoutMs, cancellationToken);

                _logger?.LogInformation("Connected to pipe: {PipeName}", config.PipeName);

                return pipeClient;
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(config.RequestTimeoutMs),
            BaseAddress = new Uri("http://localhost/")
        };

        client.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        return client;
    }

    private ChatRequest CreateChatRequest(MessageList chatHistory, ChatOptions? settings, bool stream)
    {
        var messages = chatHistory.Select(ConvertToMessage).ToList();

        return new ChatRequest
        {
            Model = settings?.ExtensionData?.TryGetValue("model", out var model) == true && model.ValueKind == JsonValueKind.String ? model.GetString() ?? "gpt-4" : "gpt-4",
            Messages = messages,
            Stream = stream,
            Temperature = settings?.Temperature,
            MaxTokens = settings?.MaxTokens
        };
    }

    private async Task<OpenAIChatResponse> SendRequestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, PipeJsonContext.Default.ChatRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger?.LogDebug("Sending chat request to pipe");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize(responseJson, PipeJsonContext.Default.OpenAIChatResponse);

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize response from pipe");
        }

        return result;
    }

    private async IAsyncEnumerable<OpenAIChatChunk> SendStreamingRequestAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, PipeJsonContext.Default.ChatRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger?.LogDebug("Sending streaming chat request to pipe");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") yield break;

            var chunk = JsonSerializer.Deserialize(data, PipeJsonContext.Default.OpenAIChatChunk);
            if (chunk != null)
            {
                yield return chunk;
            }
        }
    }

    private static ApiMessage ConvertToApiMessage(OpenAIChoice choice)
    {
        var message = choice.Message;
        var role = ConvertRole(message.Role);

        // 处理 tool_calls 响应
        if (message.ToolCalls?.Count > 0)
        {
            var tc = message.ToolCalls[0];
            return new ApiMessage(role, message.Content,
                new Dictionary<string, JsonElement>
                {
                    ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                    ["ToolCall"] = JsonElementHelper.FromString(tc.Function?.Name ?? ""),
                    ["ToolCallId"] = JsonElementHelper.FromString(tc.Id ?? ""),
                    ["ToolCallArguments"] = JsonElementHelper.FromString(tc.Function?.Arguments ?? "{}")
                });
        }

        return new ApiMessage(role, message.Content,
            new Dictionary<string, JsonElement> { ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason) });
    }

    private static MessageRole ConvertRole(string? role)
    {
        var parsed = MessageRoleExtensions.FromValue(role);
        return parsed ?? MessageRole.Assistant;
    }

    private static OpenAIApiMessage ConvertToMessage(ApiMessage content)
    {
        var msg = new OpenAIApiMessage
        {
            Role = ConvertRoleToString(content.Role),
            Content = content.Content
        };

        // Tool 角色消息必须带 tool_call_id
        if (content.Role == MessageRole.Tool && content.Metadata != null)
        {
            if (content.Metadata.TryGetValue("ToolCallId", out var tcIdEl) && tcIdEl.ValueKind == JsonValueKind.String)
                msg.ToolCallId = tcIdEl.GetString();
            if (content.Metadata.TryGetValue("ToolName", out var tcNameEl) && tcNameEl.ValueKind == JsonValueKind.String)
                msg.Name = tcNameEl.GetString();
        }

        // Assistant 消息带工具调用时，需要包含 tool_calls
        if (content.Role == MessageRole.Assistant && content.Metadata != null &&
            content.Metadata.TryGetValue("ToolCalls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
        {
            var toolCalls = new List<OpenAIToolCall>();
            foreach (var tcItem in tcEl.EnumerateArray())
            {
                var tc = new OpenAIToolCall();
                if (tcItem.TryGetProperty("Id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    tc.Id = idEl.GetString();
                if (tcItem.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    tc.Function = new OpenAIToolCallFunction
                    {
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

    private static string ConvertRoleToString(MessageRole role)
    {
        return role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            MessageRole.Tool => "tool",
            _ => "assistant"
        };
    }

    internal sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenAIApiMessage> Messages { get; set; } = new();

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
    }
}
