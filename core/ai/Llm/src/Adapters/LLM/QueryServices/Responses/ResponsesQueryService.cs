namespace Api.LLM.QueryServices.Responses;

/// <summary>
/// Responses API QueryService — OpenAI/DeepSeek Responses API 格式(POST /responses)
/// 用 input + instructions 请求,output 数组响应,流式 event SSE(无 [DONE])
/// </summary>
public class ResponsesQueryService : QueryServiceBase
{
    public ResponsesQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null, ResilientHttpExecutor? resilientExecutor = null)
        : base(config, httpClient, logger, fs, resilientExecutor)
    {
    }

    /// <summary>非流式:构建 Responses 请求 → 发送 → 转换为 ApiMessage</summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(chatHistory, executionSettings, stream: false, kernel);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        return ConvertToApiMessages(response);
    }

    /// <summary>流式:构建 Responses 请求 → 发送流式 → 解析 event SSE → yield StreamEvent</summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(chatHistory, executionSettings, stream: true, kernel);
        var modelId = request.Model;

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);
        var endpoint = GetChatEndpoint(Config);

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.ResponsesStreaming", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var isFirstChunk = true;
        string? currentEvent = null;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            if (line.StartsWith("event: "))
            {
                currentEvent = line[7..].Trim();
                continue;
            }

            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];

            if (string.IsNullOrEmpty(data) || currentEvent is null) continue;

            JsonElement eventJson;
            try
            {
                eventJson = JsonDocument.Parse(data).RootElement;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                Logger?.LogWarning(ex, "Responses API event 反序列化失败, 跳过");
                continue;
            }

            var metadata = new Dictionary<string, JsonElement>();

            switch (currentEvent)
            {
                case "response.output_text.delta":
                    {
                        var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            var rateLimitHeaders = GetLastRateLimitHeaders();
                            if (rateLimitHeaders != null)
                            {
                                foreach (var kvp in rateLimitHeaders)
                                    metadata[$"ratelimit_{kvp.Key}"] = JsonElementHelper.FromString(kvp.Value);
                            }
                        }
                        yield return new StreamEvent(MessageRole.Assistant, delta, modelId, metadata);
                        break;
                    }
                case "response.reasoning_text.delta":
                    {
                        var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                        metadata["reasoning_content"] = JsonElementHelper.FromBoolean(true);
                        yield return new StreamEvent(MessageRole.Assistant, delta, modelId, metadata);
                        break;
                    }
                case "response.function_call_arguments.delta":
                    {
                        if (eventJson.TryGetProperty("item_id", out var itemIdProp))
                        {
                            var itemId = itemIdProp.GetString() ?? string.Empty;
                            var idx = itemId.GetHashCode() & 0x7FFFFFFF;
                            var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                            if (toolCallAccumulator.TryGetValue(idx, out var existing))
                                existing.Arguments.Append(delta);
                        }
                        break;
                    }
                case "response.output_item.added":
                    {
                        if (eventJson.TryGetProperty("item", out var itemProp) && itemProp.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();
                            if (type == "function_call")
                            {
                                var callId = itemProp.TryGetProperty("call_id", out var callIdProp) ? callIdProp.GetString() ?? "" : "";
                                var name = itemProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                                var idx = (itemProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "").GetHashCode() & 0x7FFFFFFF;
                                toolCallAccumulator[idx] = (callId, name, new StringBuilder());
                            }
                        }
                        break;
                    }
                case "response.completed":
                    {
                        if (eventJson.TryGetProperty("response", out var respProp) && respProp.TryGetProperty("usage", out var usageProp))
                        {
                            var tokenUsage = BuildTokenUsage(usageProp);
                            metadata["FinishReason"] = JsonElementHelper.FromString("stop");
                            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
                        }
                        if (toolCallAccumulator.Count > 0)
                        {
                            var entries = toolCallAccumulator
                                .Select(kv => new ToolCallEntry { Id = kv.Value.Id, Name = kv.Value.Name, Arguments = kv.Value.Arguments.ToString() })
                                .ToList();
                            metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries);
                            metadata["FinishReason"] = JsonElementHelper.FromString("tool_calls");
                        }
                        yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelId, metadata);
                        yield break;
                    }
                case "response.failed":
                    {
                        var error = eventJson.TryGetProperty("response", out var respProp) && respProp.TryGetProperty("error", out var errProp)
                            ? errProp.GetRawText() : "unknown error";
                        throw new InvalidOperationException($"Responses API failed: {error}");
                    }
            }
        }
    }

    #region 请求构建

    internal virtual ResponsesRequest CreateRequest(MessageList chatHistory, ChatOptions? settings, bool stream, IChatClient? kernel)
    {
        var modelId = Config.ModelId;
        if (settings?.FastMode == true && !string.IsNullOrEmpty(settings.FastModelId))
            modelId = settings.FastModelId;

        string? instructions = null;
        var inputSb = new StringBuilder();
        inputSb.Append('[');
        var firstInput = true;

        foreach (var msg in chatHistory)
        {
            if (msg.Role == MessageRole.System)
            {
                instructions = string.IsNullOrEmpty(instructions) ? msg.Content : instructions + "\n" + msg.Content;
                continue;
            }
            if (!firstInput) inputSb.Append(',');
            firstInput = false;
            var role = ConvertRoleToString(msg.Role);
            var contentType = msg.Role == MessageRole.Assistant ? "output_text" : "input_text";
            inputSb.Append("{\"type\":\"message\",\"role\":\"").Append(role)
                .Append("\",\"content\":[{\"type\":\"").Append(contentType)
                .Append("\",\"text\":\"").Append(EscapeJsonString(msg.Content ?? string.Empty)).Append("\"}]}");
        }
        inputSb.Append(']');

        var request = new ResponsesRequest
        {
            Model = modelId,
            Stream = stream,
            Temperature = settings?.Temperature,
            TopP = settings?.TopP,
            MaxOutputTokens = settings?.MaxTokens,
            Instructions = instructions,
            Input = JsonDocument.Parse(inputSb.ToString()).RootElement.Clone()
        };

        if (settings?.EffortLevel is not null)
        {
            request.Reasoning = new ResponsesReasoning { Effort = ChatOptions.EffortToReasoningEffort(settings.EffortLevel.Value) };
        }

        if (settings?.ThinkingEnabled == true)
        {
            request.Reasoning ??= new ResponsesReasoning { Effort = "high" };
        }

        if (settings?.ToolChoice == ToolChoice.AutoInvoke && kernel != null)
        {
            var tools = BuildToolsFromKernel(kernel);
            if (tools.Count > 0)
            {
                request.Tools = tools;
                request.ToolChoice = "auto";
            }
        }

        return request;
    }

    private static List<ResponsesTool> BuildToolsFromKernel(IChatClient kernel)
    {
        return EnumerateToolFunctions(kernel)
            .Select(function => new ResponsesTool
            {
                Type = "function",
                Name = function.Name,
                Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                Parameters = BuildParameters(function.Parameters)
            })
            .ToList();
    }

    private static JsonElement? BuildParameters(IReadOnlyList<IToolParam> parameters)
    {
        if (parameters.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{");
        var first = true;
        foreach (var param in parameters)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJsonString(param.Name)).Append("\":{\"type\":\"")
                .Append(MapClrTypeToJsonSchemaType(param.ParameterType)).Append('"');
            if (!string.IsNullOrEmpty(param.Description))
                sb.Append(",\"description\":\"").Append(EscapeJsonString(param.Description)).Append('"');
            sb.Append('}');
        }
        sb.Append('}');

        var required = parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList();
        if (required.Count > 0)
        {
            sb.Append(",\"required\":[");
            sb.Append(string.Join(",", required.Select(r => "\"" + EscapeJsonString(r) + "\"")));
            sb.Append(']');
        }
        sb.Append('}');

        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    #endregion

    #region 请求发送

    private async Task<ResponsesResponse> SendRequestAsync(ResponsesRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);
        var endpoint = GetChatEndpoint(Config);

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.Responses", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ResponsesResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(responseJson, NativeJsonContext.Default.ResponsesResponse);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new InvalidOperationException($"Failed to deserialize Responses API response: {ex.Message}", ex);
        }

        return result ?? throw new InvalidOperationException("Failed to deserialize Responses API response");
    }

    #endregion

    #region 响应转换

    internal static IReadOnlyList<ApiMessage> ConvertToApiMessages(ResponsesResponse response)
    {
        var messages = new List<ApiMessage>();
        var metadata = new Dictionary<string, JsonElement>
        {
            ["Id"] = JsonElementHelper.FromString(response.Id),
            ["FinishReason"] = JsonElementHelper.FromString(response.Status ?? "completed")
        };

        if (response.Usage is not null)
        {
            var tokenUsage = BuildTokenUsage(response.Usage);
            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }

        var toolCalls = new List<ToolCallEntry>();
        var textContent = new StringBuilder();

        foreach (var item in response.Output)
        {
            if (item.Type == "message" && item.Content is not null)
            {
                foreach (var content in item.Content)
                {
                    if (!string.IsNullOrEmpty(content.Text))
                        textContent.Append(content.Text);
                }
            }
            else if (item.Type == "function_call")
            {
                toolCalls.Add(new ToolCallEntry
                {
                    Id = item.CallId ?? "",
                    Name = item.Name ?? "",
                    Arguments = item.Arguments ?? ""
                });
            }
        }

        if (toolCalls.Count > 0)
        {
            metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(toolCalls);
            metadata["FinishReason"] = JsonElementHelper.FromString("tool_calls");
            messages.Add(new ApiMessage(MessageRole.Assistant, null, metadata));
        }
        else
        {
            messages.Add(new ApiMessage(MessageRole.Assistant, textContent.ToString(), metadata));
        }

        return messages;
    }

    private static TokenUsage BuildTokenUsage(ResponsesUsage usage)
    {
        return new TokenUsage
        {
            PromptTokens = usage.InputTokens,
            CompletionTokens = usage.OutputTokens,
            CacheReadInputTokens = usage.InputTokensDetails?.CachedTokens ?? 0,
            CacheCreationInputTokens = 0
        };
    }

    private static TokenUsage BuildTokenUsage(JsonElement usageJson)
    {
        var inputTokens = usageJson.TryGetProperty("input_tokens", out var itProp) ? itProp.GetInt32() : 0;
        var outputTokens = usageJson.TryGetProperty("output_tokens", out var otProp) ? otProp.GetInt32() : 0;
        var cachedTokens = 0;
        if (usageJson.TryGetProperty("input_tokens_details", out var itdProp) && itdProp.TryGetProperty("cached_tokens", out var ctProp))
            cachedTokens = ctProp.GetInt32();

        return new TokenUsage
        {
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
            CacheReadInputTokens = cachedTokens,
            CacheCreationInputTokens = 0
        };
    }

    #endregion
}
