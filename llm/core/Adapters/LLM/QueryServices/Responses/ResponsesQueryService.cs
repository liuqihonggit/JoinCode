namespace Api.LLM.QueryServices.Responses;

/// <summary>
/// Responses API QueryService — OpenAI/DeepSeek Responses API 格式(POST /responses)
/// 用 input + instructions 请求,output 数组响应,流式 event SSE(无 [DONE])
/// </summary>
public class ResponsesQueryService : QueryServiceBase {
    /// <summary>构造 Responses API 查询服务。</summary>
    /// <param name="config">供应商配置。</param>
    /// <param name="httpClient">HTTP 客户端。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="fs">文件系统。</param>
    /// <param name="resilientExecutor">弹性 HTTP 执行器。</param>
    public ResponsesQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null, ResilientHttpExecutor? resilientExecutor = null)
        : base(config, httpClient, logger, fs, resilientExecutor) {
    }

    /// <summary>非流式:构建 Responses 请求 → 发送 → 转换为 ApiMessage</summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default) {
        var request = CreateRequest(chatHistory, executionSettings, stream: false, kernel);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        return ConvertToApiMessages(response);
    }

    /// <summary>流式:构建 Responses 请求 → 发送流式 → 解析 event SSE → yield StreamEvent</summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var request = CreateRequest(chatHistory, executionSettings, stream: true, kernel);
        var modelId = request.Model;

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);
        var endpoint = GetChatEndpoint(Config);

        Diag.WriteLine($"[WIRE] Responses 流式请求体字节数={Encoding.UTF8.GetByteCount(json)} | reasoning={request.Reasoning?.Effort ?? "off"} | {endpoint}");

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.ResponsesStreaming", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = stream.AsUtf8Reader();

        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var reasoningAccumulator = new StringBuilder();
        var isFirstChunk = true;
        string? currentEvent = null;
        string? descRequestContent = null;
        var descRequestAccumulator = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null) {
            if (cancellationToken.IsCancellationRequested) yield break;

            if (line.StartsWith("event: ")) {
                currentEvent = line[7..].Trim();
                continue;
            }

            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];

            if (string.IsNullOrEmpty(data)) continue;

            JsonElement eventJson;
            try {
                eventJson = JsonDocument.Parse(data).RootElement;
            } catch (Exception ex) when (ex is JsonException or FormatException) {
                Logger?.LogWarning(ex, "Responses API event 反序列化失败, 跳过");
                continue;
            }

            // 事件类型解析: 优先 data 内 type 字段(无 event: 前缀的容错), 回退到 event: 前缀
            if (eventJson.ValueKind != JsonValueKind.Object) {
                continue;
            }
            if (eventJson.TryGetProperty("type", out var eventTypeProp) && eventTypeProp.ValueKind == JsonValueKind.String) {
                currentEvent = eventTypeProp.GetString();
            } else if (currentEvent is null) {
                continue;
            }

            var metadata = new Dictionary<string, JsonElement>();

            switch (currentEvent) {
                case "response.output_text.delta": {
                    var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                    descRequestAccumulator.Append(delta);
                    var accumulated = descRequestAccumulator.ToString();
                    if (kernel != null && IsToolDescriptionRequestComplete(accumulated)) {
                        descRequestContent = accumulated;
                        break;
                    }
                    if (isFirstChunk) {
                        isFirstChunk = false;
                        var rateLimitHeaders = GetLastRateLimitHeaders();
                        if (rateLimitHeaders != null) {
                            foreach (var kvp in rateLimitHeaders)
                                metadata[$"ratelimit_{kvp.Key}"] = JsonElementHelper.FromString(kvp.Value);
                        }
                    }
                    yield return new StreamEvent(MessageRole.Assistant, delta, modelId, metadata);
                    break;
                }
                case "response.reasoning_text.delta": {
                    var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                    metadata["reasoning_content"] = JsonElementHelper.FromBoolean(true);
                    if (delta.Length > 0) reasoningAccumulator.Append(delta);
                    yield return new StreamEvent(MessageRole.Assistant, delta, modelId, metadata);
                    break;
                }
                case "response.function_call_arguments.delta": {
                    if (eventJson.TryGetProperty("item_id", out var itemIdProp)) {
                        var itemId = itemIdProp.GetString() ?? string.Empty;
                        var idx = GetItemIdIndex(itemId);
                        var delta = eventJson.TryGetProperty("delta", out var deltaProp) ? deltaProp.GetString() ?? string.Empty : string.Empty;
                        if (toolCallAccumulator.TryGetValue(idx, out var existing))
                            existing.Arguments.Append(delta);
                    }
                    break;
                }
                case "response.output_item.added": {
                    if (eventJson.TryGetProperty("item", out var itemProp) && itemProp.TryGetProperty("type", out var typeProp)) {
                        if (typeProp.GetString() == "function_call") {
                            var (callId, name, idx) = ParseFunctionCallItem(itemProp);
                            toolCallAccumulator[idx] = (callId, name, new StringBuilder());
                        }
                    }
                    break;
                }
                case "response.completed":
                case "response.incomplete": {
                    PopulateCompletedMetadata(metadata, eventJson, toolCallAccumulator, reasoningAccumulator);
                    yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelId, metadata);
                    yield break;
                }
                case "response.failed": {
                    var error = ExtractFailedErrorMessage(eventJson);
                    throw new InvalidOperationException($"Responses API failed: {error}");
                }
            }

            if (descRequestContent is not null) break;
        }

        // 两阶段工具加载: 检测到 tool_description_request → 构建第二次请求(含 tool_descriptions)
        if (descRequestContent is null || kernel is null) yield break;
        Logger?.LogDebug("[WIRE] Responses 收到 tool_description_request, 发送第二次请求");
        var secondRequest = CreateSecondResponsesRequestWithDescriptions(request, descRequestContent, kernel);
        var secondJson = JsonSerializer.Serialize(secondRequest, NativeJsonContext.Default.ResponsesRequest);
        var secondEndpoint = GetChatEndpoint(Config);
        var secondResponse = await SendWithResilienceAsync(secondJson, secondEndpoint, "LLM.ResponsesStreaming2", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        secondResponse.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(secondResponse);
        var secondStream = await secondResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var secondReader = secondStream.AsUtf8Reader();
        var secondAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        string? secondCurrentEvent = null;
        string? sLine;
        while ((sLine = await secondReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null) {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (sLine.StartsWith("event: ")) { secondCurrentEvent = sLine[7..].Trim(); continue; }
            if (!sLine.StartsWith("data: ")) continue;
            var sData = sLine[6..];
            if (string.IsNullOrEmpty(sData)) continue;

            JsonElement sEventJson;
            try { sEventJson = JsonDocument.Parse(sData).RootElement; } catch (Exception ex) when (ex is JsonException or FormatException) { continue; }

            // 事件类型解析: 优先 data 内 type 字段(无 event: 前缀的容错), 回退到 event: 前缀
            if (sEventJson.ValueKind != JsonValueKind.Object) {
                continue;
            }
            if (sEventJson.TryGetProperty("type", out var sEventTypeProp) && sEventTypeProp.ValueKind == JsonValueKind.String) {
                secondCurrentEvent = sEventTypeProp.GetString();
            } else if (secondCurrentEvent is null) {
                continue;
            }

            var sMeta = new Dictionary<string, JsonElement>();
            switch (secondCurrentEvent) {
                case "response.output_text.delta": {
                    var sDelta = sEventJson.TryGetProperty("delta", out var d) ? d.GetString() ?? "" : "";
                    yield return new StreamEvent(MessageRole.Assistant, sDelta, modelId, sMeta);
                    break;
                }
                case "response.reasoning_text.delta": {
                    var sDelta = sEventJson.TryGetProperty("delta", out var d) ? d.GetString() ?? "" : "";
                    sMeta["reasoning_content"] = JsonElementHelper.FromBoolean(true);
                    yield return new StreamEvent(MessageRole.Assistant, sDelta, modelId, sMeta);
                    break;
                }
                case "response.function_call_arguments.delta": {
                    if (sEventJson.TryGetProperty("item_id", out var itemIdProp)) {
                        var itemId = itemIdProp.GetString() ?? "";
                        var idx = GetItemIdIndex(itemId);
                        var sDelta = sEventJson.TryGetProperty("delta", out var d) ? d.GetString() ?? "" : "";
                        if (secondAccumulator.TryGetValue(idx, out var ex)) ex.Arguments.Append(sDelta);
                    }
                    break;
                }
                case "response.output_item.added": {
                    if (sEventJson.TryGetProperty("item", out var itemProp) && itemProp.TryGetProperty("type", out var typeProp)) {
                        if (typeProp.GetString() == "function_call") {
                            var (callId, name, idx) = ParseFunctionCallItem(itemProp);
                            secondAccumulator[idx] = (callId, name, new StringBuilder());
                        }
                    }
                    break;
                }
                case "response.completed":
                case "response.incomplete": {
                    PopulateCompletedMetadata(sMeta, sEventJson, secondAccumulator, null);
                    yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelId, sMeta);
                    yield break;
                }
            }
        }
    }

    #region SSE 事件解析辅助 — 纯函数,供确定性测试

    /// <summary>检测累积文本是否为完整的 tool_description_request JSON</summary>
    internal static bool IsToolDescriptionRequestComplete(string accumulated) {
        return accumulated.Contains("tool_description_request") && accumulated.TrimEnd().EndsWith('}');
    }

    /// <summary>计算 SSE item_id 的累积器索引(非负哈希)</summary>
    internal static int GetItemIdIndex(string itemId) {
        return itemId.GetHashCode() & 0x7FFFFFFF;
    }

    /// <summary>解析 response.output_item.added 中 function_call item 的 call_id/name/idx</summary>
    internal static (string CallId, string Name, int Idx) ParseFunctionCallItem(JsonElement itemProp) {
        var callId = itemProp.TryGetProperty("call_id", out var callIdProp) ? callIdProp.GetString() ?? "" : "";
        var name = itemProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        var idx = GetItemIdIndex(itemProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "");
        return (callId, name, idx);
    }

    /// <summary>从工具调用累积器构建 ToolCallEntry 列表(纯转换)</summary>
    internal static List<ToolCallEntry> BuildToolCallEntriesFromAccumulator(
        Dictionary<int, (string Id, string Name, StringBuilder Arguments)> accumulator) {
        return accumulator
            .Select(kv => new ToolCallEntry { Id = kv.Value.Id, Name = kv.Value.Name, Arguments = kv.Value.Arguments.ToString() })
            .ToList();
    }

    /// <summary>填充 response.completed/response.incomplete 事件的 metadata(usage + tool_calls + reasoning)</summary>
    internal static void PopulateCompletedMetadata(
        Dictionary<string, JsonElement> metadata,
        JsonElement eventJson,
        Dictionary<int, (string Id, string Name, StringBuilder Arguments)> toolCallAccumulator,
        StringBuilder? reasoningAccumulator) {
        if (eventJson.TryGetProperty("response", out var respProp) && respProp.TryGetProperty("usage", out var usageProp)) {
            var tokenUsage = BuildTokenUsage(usageProp);
            metadata["FinishReason"] = JsonElementHelper.FromString("stop");
            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }
        if (toolCallAccumulator.Count > 0) {
            var entries = BuildToolCallEntriesFromAccumulator(toolCallAccumulator);
            metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries);
            metadata["FinishReason"] = JsonElementHelper.FromString("tool_calls");
        }
        if (reasoningAccumulator is { Length: > 0 }) {
            metadata[MessageMetadataKeyEnumConstants.ReasoningText] = JsonElementHelper.FromString(reasoningAccumulator.ToString());
        }
    }

    /// <summary>提取 response.failed 事件的错误信息(纯提取,不抛异常)</summary>
    internal static string ExtractFailedErrorMessage(JsonElement eventJson) {
        return eventJson.TryGetProperty("response", out var respProp) && respProp.TryGetProperty("error", out var errProp)
            ? errProp.GetRawText() : "unknown error";
    }

    #endregion

    #region 请求构建

    internal virtual ResponsesRequest CreateRequest(MessageList chatHistory, ChatOptions? settings, bool stream, IChatClient? kernel) {
        var modelId = Config.ModelId;
        if (settings?.FastMode == true && !string.IsNullOrEmpty(settings.FastModelId))
            modelId = settings.FastModelId;

        string? instructions = null;
        var inputItems = new List<ResponsesInputItemDto>(chatHistory.Count);

        foreach (var msg in chatHistory) {
            if (msg.Role == MessageRole.System) {
                instructions = string.IsNullOrEmpty(instructions) ? msg.Content : instructions + "\n" + msg.Content;
                continue;
            }

            if (msg.Role == MessageRole.Tool) {
                inputItems.Add(BuildFunctionCallOutput(msg));
                continue;
            }

            if (TryBuildAssistantMetadata(msg) is { } metadataItems) {
                inputItems.AddRange(metadataItems);
                continue;
            }

            var role = ConvertRoleToString(msg.Role);
            var contentType = msg.Role == MessageRole.Assistant ? "output_text" : "input_text";
            inputItems.Add(new ResponsesInputItemDto {
                Type = "message",
                Role = role,
                Content = new List<ResponsesInputContentDto> {
                    new() { Type = contentType, Text = msg.Content ?? string.Empty },
                },
            });
        }

        var request = new ResponsesRequest {
            Model = modelId,
            Stream = stream,
            Temperature = settings?.Temperature,
            TopP = settings?.TopP,
            MaxOutputTokens = settings?.MaxTokens,
            Instructions = instructions,
            Input = JsonSerializer.SerializeToElement(inputItems, NativeJsonContext.Safe.ListResponsesInputItemDto)
        };

        if (settings?.EffortLevel is not null) {
            request.Reasoning = new ResponsesReasoning { Effort = ChatOptions.EffortToReasoningEffort(settings.EffortLevel.Value) };
        }

        if (settings?.ThinkingEnabled == true) {
            request.Reasoning ??= new ResponsesReasoning { Effort = "high" };
        }

        if (settings?.ToolChoice == ToolChoice.AutoInvoke && kernel != null) {
            var (tools, toolGroups) = BuildToolsFromKernel(kernel);
            if (tools.Count > 0) {
                request.Tools = tools;
                request.ToolChoice = "auto";
            }
            if (toolGroups.Count > 0) {
                request.ToolGroups = toolGroups;
            }
        }

        return request;
    }

    /// <summary>
    /// 构建工具列表 — 两阶段加载：core_tools 发完整 schema，mcp_tools 发分组+名称
    /// </summary>
    internal static (List<ResponsesTool> Tools, List<OpenAIToolGroup> ToolGroups) BuildToolsFromKernel(IChatClient kernel) {
        var tools = new List<ResponsesTool>();
        var toolGroups = new List<OpenAIToolGroup>();

        foreach (var pluginName in kernel.Plugins.GetPluginNames()) {
            var plugin = kernel.Plugins.GetPlugin(pluginName);
            if (plugin is not IToolGroup group)
                continue;

            if (group.Name == ToolGroupNameConstants.McpTools) {
                toolGroups.Add(new OpenAIToolGroup {
                    Name = group.Name,
                    Tools = group.Functions.Select(f => f.Name).ToList()
                });
            } else {
                foreach (var function in group.Functions) {
                    tools.Add(new ResponsesTool {
                        Type = "function",
                        Name = function.Name,
                        Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                        Parameters = BuildParameters(function.Parameters)
                    });
                }
            }
        }

        return (tools, toolGroups);
    }

    /// <summary>
    /// 两阶段工具加载 — 解析 tool_description_request,构建第二次 Responses 请求(含 tool_descriptions)
    /// </summary>
    internal static ResponsesRequest CreateSecondResponsesRequestWithDescriptions(
        ResponsesRequest originalRequest, string descRequestContent, IChatClient kernel) {
        HashSet<string> toolNames;
        try {
            using var doc = JsonDocument.Parse(descRequestContent);
            toolNames = doc.RootElement.GetProperty("tools").EnumerateArray()
                .Select(t => t.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.Ordinal);
        } catch (JsonException ex) {
            throw new InvalidOperationException(
                $"Failed to parse tool_description_request JSON: {ex.Message} | Content: {descRequestContent[..Math.Min(descRequestContent.Length, 200)]}", ex);
        }

        var descriptions = new List<ResponsesTool>();
        foreach (var pluginName in kernel.Plugins.GetPluginNames()) {
            var plugin = kernel.Plugins.GetPlugin(pluginName);
            if (plugin is not IToolGroup group)
                continue;
            foreach (var function in group.Functions) {
                if (toolNames.Contains(function.Name)) {
                    descriptions.Add(new ResponsesTool {
                        Type = "function",
                        Name = function.Name,
                        Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                        Parameters = BuildParameters(function.Parameters)
                    });
                }
            }
        }

        return new ResponsesRequest {
            Model = originalRequest.Model,
            Input = originalRequest.Input,
            Instructions = originalRequest.Instructions,
            Stream = originalRequest.Stream,
            Temperature = originalRequest.Temperature,
            TopP = originalRequest.TopP,
            MaxOutputTokens = originalRequest.MaxOutputTokens,
            Tools = originalRequest.Tools,
            ToolChoice = originalRequest.ToolChoice,
            ToolGroups = originalRequest.ToolGroups,
            Reasoning = originalRequest.Reasoning,
            ToolDescriptions = descriptions
        };
    }

    internal static JsonElement? BuildParameters(IReadOnlyList<IToolParam> parameters) {
        if (parameters.Count == 0) return null;

        var properties = new Dictionary<string, JsonSchemaPropertyDto>(parameters.Count);
        foreach (var param in parameters) {
            properties[param.Name] = new JsonSchemaPropertyDto {
                Type = MapClrTypeToJsonSchemaType(param.ParameterType),
                Description = string.IsNullOrEmpty(param.Description) ? null : param.Description,
            };
        }

        var required = parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList();
        var schema = new JsonSchemaDto {
            Properties = properties,
            Required = required.Count > 0 ? required : null,
        };

        return JsonSerializer.SerializeToElement(schema, NativeJsonContext.Safe.JsonSchemaDto);
    }

    /// <summary>Tool 结果消息 → function_call_output item DTO</summary>
    internal static ResponsesInputItemDto BuildFunctionCallOutput(ApiMessage msg) {
        var callId = msg.Metadata is not null
            && msg.Metadata.TryGetValue(MessageMetadataKeyEnumConstants.ToolCallId, out var idProp)
            && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() ?? string.Empty
            : string.Empty;
        return new ResponsesInputItemDto {
            Type = "function_call_output",
            CallId = callId,
            Output = msg.Content ?? string.Empty,
        };
    }

    /// <summary>Assistant 消息的 reasoning + tool_calls 元数据 → Responses input item DTO 列表。返回 null 表示无元数据</summary>
    internal static List<ResponsesInputItemDto>? TryBuildAssistantMetadata(ApiMessage msg) {
        if (msg.Role != MessageRole.Assistant || msg.Metadata is null) return null;

        var items = new List<ResponsesInputItemDto>();

        if (msg.Metadata.TryGetValue(MessageMetadataKeyEnumConstants.ReasoningText, out var reasoningProp)
            && reasoningProp.ValueKind == JsonValueKind.String) {
            items.Add(new ResponsesInputItemDto {
                Type = "reasoning",
                Content = new List<ResponsesInputContentDto> {
                    new() { Type = "reasoning_text", Text = reasoningProp.GetString() ?? string.Empty },
                },
            });
        }

        if (msg.Metadata.TryGetValue(MessageMetadataKeyEnumConstants.ToolCalls, out var toolCallsProp)
            || msg.Metadata.TryGetValue("AllToolCalls", out toolCallsProp)) {
            if (toolCallsProp.ValueKind == JsonValueKind.Array) {
                foreach (var tc in toolCallsProp.EnumerateArray()) {
                    var id = tc.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var name = tc.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var args = tc.TryGetProperty("Arguments", out var argsProp) ? argsProp.GetString() ?? "{}" : "{}";
                    items.Add(new ResponsesInputItemDto {
                        Type = "function_call",
                        CallId = id,
                        Name = name,
                        Arguments = args,
                    });
                }
                return items;
            }
        }

        return items.Count > 0 ? items : null;
    }

    #endregion

    #region 请求发送

    private async Task<ResponsesResponse> SendRequestAsync(ResponsesRequest request, CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.ResponsesRequest);
        var endpoint = GetChatEndpoint(Config);

        Diag.WriteLine($"[WIRE] Responses 非流式请求体字节数={Encoding.UTF8.GetByteCount(json)} | reasoning={request.Reasoning?.Effort ?? "off"} | {endpoint}");

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.Responses", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        ExtractRateLimitHeaders(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ResponsesResponse? result;
        try {
            result = RelaxedJsonSerializer.Deserialize(responseJson, NativeJsonContext.Default.ResponsesResponse);
        } catch (Exception ex) when (ex is JsonException or FormatException) {
            throw new InvalidOperationException($"Failed to deserialize Responses API response: {ex.Message}", ex);
        }

        return result ?? throw new InvalidOperationException("Failed to deserialize Responses API response");
    }

    #endregion

    #region 响应转换

    internal static IReadOnlyList<ApiMessage> ConvertToApiMessages(ResponsesResponse response) {
        var messages = new List<ApiMessage>();
        var metadata = new Dictionary<string, JsonElement> {
            ["Id"] = JsonElementHelper.FromString(response.Id),
            ["FinishReason"] = JsonElementHelper.FromString(response.Status ?? "completed")
        };

        if (response.Usage is not null) {
            var tokenUsage = BuildTokenUsage(response.Usage);
            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }

        var toolCalls = new List<ToolCallEntry>();
        var textContent = new StringBuilder();
        var reasoningContent = new StringBuilder();

        foreach (var item in response.Output) {
            if (item.Type == "reasoning" && item.Content is not null) {
                foreach (var content in item.Content) {
                    if (!string.IsNullOrEmpty(content.Text))
                        reasoningContent.Append(content.Text);
                }
            } else if (item.Type == "message" && item.Content is not null) {
                foreach (var content in item.Content) {
                    if (!string.IsNullOrEmpty(content.Text))
                        textContent.Append(content.Text);
                }
            } else if (item.Type == "function_call") {
                toolCalls.Add(new ToolCallEntry {
                    Id = item.CallId ?? "",
                    Name = item.Name ?? "",
                    Arguments = item.Arguments ?? ""
                });
            }
        }

        if (reasoningContent.Length > 0) {
            metadata[MessageMetadataKeyEnumConstants.ReasoningText] = JsonElementHelper.FromString(reasoningContent.ToString());
        }

        if (toolCalls.Count > 0) {
            metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(toolCalls);
            metadata["FinishReason"] = JsonElementHelper.FromString("tool_calls");
            messages.Add(new ApiMessage(MessageRole.Assistant, null, metadata));
        } else {
            messages.Add(new ApiMessage(MessageRole.Assistant, textContent.ToString(), metadata));
        }

        return messages;
    }

    internal static TokenUsage BuildTokenUsage(ResponsesUsage usage) {
        return new TokenUsage {
            PromptTokens = usage.InputTokens,
            CompletionTokens = usage.OutputTokens,
            CacheReadInputTokens = usage.InputTokensDetails?.CachedTokens ?? 0,
            CacheCreationInputTokens = 0
        };
    }

    internal static TokenUsage BuildTokenUsage(JsonElement usageJson) {
        var inputTokens = usageJson.TryGetProperty("input_tokens", out var itProp) ? itProp.GetInt32() : 0;
        var outputTokens = usageJson.TryGetProperty("output_tokens", out var otProp) ? otProp.GetInt32() : 0;
        var cachedTokens = 0;
        if (usageJson.TryGetProperty("input_tokens_details", out var itdProp) && itdProp.TryGetProperty("cached_tokens", out var ctProp))
            cachedTokens = ctProp.GetInt32();

        return new TokenUsage {
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
            CacheReadInputTokens = cachedTokens,
            CacheCreationInputTokens = 0
        };
    }

    #endregion
}