namespace Api.LLM.QueryServices.Anthropic;

using Api.LLM.CacheProtocol;

/// <summary>
/// Anthropic 协议 QueryService 实现 — 完全独立的协议（v1/messages 端点 + x-api-key Header + content blocks）
/// 不复用 OpenAI 协议路径，仅继承基类的协议无关基础设施（HttpClient / 速率限制 / 角色转换）
/// </summary>
public sealed class AnthropicQueryService : QueryServiceBase
{
    private static readonly AnthropicCacheProtocol CacheProtocol = new();

    public AnthropicQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null)
        : base(config, httpClient, logger, fs)
    {
    }

    /// <summary>非流式：构建 Anthropic 请求 → 发送 → 转换为 ApiMessage</summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = await CreateAnthropicRequest(chatHistory, executionSettings, stream: false, kernel).ConfigureAwait(false);
        var response = await SendAnthropicRequestAsync(request, cancellationToken).ConfigureAwait(false);
        return ConvertAnthropicResponseToApiMessages(response);
    }

    /// <summary>流式：构建 Anthropic 请求 → 发送流式 → 解析 content block deltas → yield StreamEvent</summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = await CreateAnthropicRequest(chatHistory, executionSettings, stream: true, kernel).ConfigureAwait(false);
        var isFirstChunk = true;
        await foreach (var msg in SendAnthropicStreamingRequestAsync(request, cancellationToken).ConfigureAwait(false))
        {
            if (isFirstChunk)
            {
                isFirstChunk = false;
                var enrichedMsg = EnrichWithRateLimitMetadata(msg);
                yield return enrichedMsg ?? msg;
                continue;
            }
            yield return msg;
        }
    }

    #region 请求构建

    private async Task<AnthropicMessagesRequest> CreateAnthropicRequest(
        MessageList chatHistory,
        ChatOptions? settings,
        bool stream,
        IChatClient? kernel)
    {
        var (systemBlocks, anthropicMessages) = ConvertToAnthropicMessages(chatHistory);

        var modelId = Config.ModelId;
        if (settings?.FastMode == true && !string.IsNullOrEmpty(settings.FastModelId))
            modelId = settings.FastModelId;

        var request = new AnthropicMessagesRequest
        {
            Model = modelId,
            MaxTokens = settings?.MaxTokens ?? 4096,
            System = systemBlocks.Count > 0 ? systemBlocks : null,
            Messages = anthropicMessages,
            Stream = stream,
            Temperature = settings?.Temperature,
            TopP = settings?.TopP
        };

        if (settings?.EffortLevel is not null)
        {
            request.Thinking = new AnthropicThinkingConfig
            {
                Type = "enabled",
                BudgetTokens = ChatOptions.EffortToBudgetTokens(settings.EffortLevel.Value)
            };
        }

        if (settings?.ToolChoice == ToolChoice.AutoInvoke && kernel != null)
        {
            var allTools = BuildAnthropicToolsFromKernel(kernel);
            if (allTools.Count > 0)
            {
                var deferredToolInfos = settings.DeferredTools;
                var discoveredTools = settings.DiscoveredTools;

                if (deferredToolInfos is { Count: > 0 } && discoveredTools != null)
                {
                    var deferredNames = new HashSet<string>(
                        deferredToolInfos.Select(t => t.Name), StringComparer.Ordinal);

                    var snapshot = await discoveredTools.SnapshotAsync().ConfigureAwait(false);
                    var discoveredSet = new HashSet<string>(snapshot, StringComparer.Ordinal);

                    var filteredTools = new List<AnthropicToolDefinition>();
                    var deferredNotDiscovered = new List<DeferredToolInfo>();

                    foreach (var tool in allTools)
                    {
                        if (deferredNames.Contains(tool.Name))
                        {
                            if (discoveredSet.Contains(tool.Name))
                            {
                                filteredTools.Add(tool);
                            }
                            else
                            {
                                var info = deferredToolInfos.First(t => t.Name == tool.Name);
                                deferredNotDiscovered.Add(info);
                            }
                        }
                        else
                        {
                            filteredTools.Add(tool);
                        }
                    }

                    if (deferredNotDiscovered.Count > 0)
                    {
                        var deferredDefs = BuildDeferredToolDefinitions(deferredNotDiscovered);
                        filteredTools.AddRange(deferredDefs);

                        filteredTools.Add(BuildToolSearchToolDefinition());
                    }

                    CacheProtocol.PlaceCacheControlOnTools(filteredTools, filteredTools.Any(t => t.Name.Contains('.')));
                    request.Tools = filteredTools;
                }
                else
                {
                    CacheProtocol.PlaceCacheControlOnTools(allTools, allTools.Any(t => t.Name.Contains('.')));
                    request.Tools = allTools;
                }

                request.ToolChoice = new { type = "auto" };
            }
        }

        if (settings?.ExtensionData != null &&
            settings.ExtensionData.TryGetValue("web_search_tool", out var webSearchToolJson))
        {
            var webSearchTool = JsonSerializer.Deserialize(webSearchToolJson, AnthropicJsonContext.Default.AnthropicToolDefinition);
            if (webSearchTool is not null)
            {
                request.Tools ??= [];
                request.Tools.Add(webSearchTool);
            }
        }

        var hasMcpTools = request.Tools is { Count: > 0 } && request.Tools.Any(t => t.Name.Contains('.'));

        if (request.System is { Count: > 0 })
        {
            CacheProtocol.PlaceCacheControlOnSystemBlocks(systemBlocks, hasMcpTools);
        }

        CacheProtocol.PlaceCacheControlOnToolResults(anthropicMessages, hasMcpTools);

        if (settings?.ContextManagement is not null)
        {
            request.ContextManagement = ConvertContextManagement(settings.ContextManagement);
        }

        return request;
    }

    private static AnthropicContextManagement ConvertContextManagement(ContextManagementConfig config)
    {
        var edits = new List<AnthropicContextEditStrategy>(config.Edits.Count);
        foreach (var strategy in config.Edits)
        {
            edits.Add(strategy switch
            {
                ClearToolUsesStrategy s => new AnthropicClearToolUsesStrategy
                {
                    Trigger = s.Trigger is not null
                        ? new AnthropicContextTrigger { Type = s.Trigger.Type, Value = s.Trigger.Value }
                        : null,
                    Keep = s.Keep is not null
                        ? new AnthropicContextKeep { Type = s.Keep.Type, Value = s.Keep.Value }
                        : null,
                    ClearToolInputs = s.ClearToolInputs,
                    ExcludeTools = s.ExcludeTools is not null
                        ? new List<string>(s.ExcludeTools)
                        : null,
                    ClearAtLeast = s.ClearAtLeast is not null
                        ? new AnthropicContextTokenThreshold { Type = s.ClearAtLeast.Type, Value = s.ClearAtLeast.Value }
                        : null,
                },
                ClearThinkingStrategy s => new AnthropicClearThinkingStrategy
                {
                    Keep = s.Keep,
                },
                _ => throw new InvalidOperationException($"Unknown ContextEditStrategy type: {strategy.Type}")
            });
        }
        return new AnthropicContextManagement { Edits = edits };
    }

    internal static (List<AnthropicSystemContentBlock> System, List<AnthropicMessage> Messages) ConvertToAnthropicMessagesPublic(
        MessageList chatHistory)
        => ConvertToAnthropicMessages(chatHistory);

    private static (List<AnthropicSystemContentBlock> System, List<AnthropicMessage> Messages) ConvertToAnthropicMessages(
        MessageList chatHistory)
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>();
        var messages = new List<AnthropicMessage>();
        var pendingToolResults = new List<AnthropicToolResultBlock>();

        foreach (var msg in chatHistory)
        {
            switch (msg.Role)
            {
                case MessageRole.System:
                    var isStatic = CacheProtocol.IsStaticSystemBlock(msg);
                    systemBlocks.Add(new AnthropicSystemContentBlock
                    {
                        Text = msg.Content ?? string.Empty,
                        IsStatic = isStatic
                    });
                    break;

                case MessageRole.User:
                    if (pendingToolResults.Count > 0)
                    {
                        pendingToolResults.Add(CreateToolResultBlock(msg));
                        messages.Add(new AnthropicMessage
                        {
                            Role = "user",
                            Content = pendingToolResults.Cast<AnthropicContentBlock>().ToList()
                        });
                        pendingToolResults.Clear();
                    }
                    else
                    {
                        messages.Add(new AnthropicMessage { Role = "user", Content = msg.Content });
                    }
                    break;

                case MessageRole.Assistant:
                    FlushToolResultsAsUserMessage(pendingToolResults, messages);

                    if (msg.Metadata != null &&
                        msg.Metadata.TryGetValue("ToolCalls", out var toolCallsObj))
                    {
                        var contentBlocks = new List<AnthropicContentBlock>();
                        if (!string.IsNullOrWhiteSpace(msg.Content))
                        {
                            contentBlocks.Add(new AnthropicTextBlock { Text = msg.Content });
                        }

                        var toolCalls = ConvertToOpenAIToolCalls(toolCallsObj);
                        if (toolCalls != null)
                        {
                            foreach (var tc in toolCalls)
                            {
                                contentBlocks.Add(new AnthropicToolUseBlock
                                {
                                    Id = tc.Id ?? "",
                                    Name = tc.Function?.Name ?? "",
                                    Input = tc.Function?.Arguments
                                });
                            }
                        }

                        messages.Add(new AnthropicMessage { Role = "assistant", Content = contentBlocks });
                    }
                    else
                    {
                        messages.Add(new AnthropicMessage { Role = "assistant", Content = msg.Content });
                    }
                    break;

                case MessageRole.Tool:
                    if (msg.Metadata != null &&
                        msg.Metadata.TryGetValue("ToolCallId", out var toolCallIdObj) &&
                        toolCallIdObj.TryGetString(out var toolCallId))
                    {
                        pendingToolResults.Add(new AnthropicToolResultBlock
                        {
                            ToolUseId = toolCallId ?? string.Empty,
                            Content = msg.Content
                        });
                    }
                    break;
            }
        }

        FlushToolResultsAsUserMessage(pendingToolResults, messages);

        return (systemBlocks, messages);
    }

    private static void FlushToolResultsAsUserMessage(
        List<AnthropicToolResultBlock> pendingToolResults,
        List<AnthropicMessage> messages)
    {
        if (pendingToolResults.Count == 0) return;

        messages.Add(new AnthropicMessage
        {
            Role = "user",
            Content = pendingToolResults.Cast<AnthropicContentBlock>().ToList()
        });
        pendingToolResults.Clear();
    }

    private static AnthropicToolResultBlock CreateToolResultBlock(ApiMessage msg)
    {
        var toolUseId = "";
        if (msg.Metadata != null &&
            msg.Metadata.TryGetValue("ToolCallId", out var idObj) &&
            idObj.TryGetString(out var tid))
        {
            toolUseId = tid;
        }

        object? content;
        if (msg.ContentBlocks is { Count: > 0 })
        {
            var blocks = new List<Dictionary<string, JsonElement>>();
            foreach (var block in msg.ContentBlocks)
            {
                if (block.Type == ToolContentType.Image && !string.IsNullOrEmpty(block.Data) && !string.IsNullOrEmpty(block.MimeType))
                {
                    var sourceDict = new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonElementHelper.FromString("base64"),
                        ["media_type"] = JsonElementHelper.FromString(block.MimeType),
                        ["data"] = JsonElementHelper.FromString(block.Data)
                    };
                    blocks.Add(new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonElementHelper.FromString("image"),
                        ["source"] = JsonElementHelper.FromObject(sourceDict, ContractsJsonContext.Default.DictionaryStringJsonElement)
                    });
                }
                else if (block.Type == ToolContentType.Text && !string.IsNullOrEmpty(block.Text))
                {
                    blocks.Add(new Dictionary<string, JsonElement>
                    {
                        ["type"] = JsonElementHelper.FromString("text"),
                        ["text"] = JsonElementHelper.FromString(block.Text)
                    });
                }
            }
            content = blocks.Count > 0 ? blocks : msg.Content;
        }
        else
        {
            content = msg.Content;
        }

        return new AnthropicToolResultBlock
        {
            ToolUseId = toolUseId ?? string.Empty,
            Content = content
        };
    }

    private static List<AnthropicToolDefinition> BuildAnthropicToolsFromKernel(IChatClient kernel)
    {
        return EnumerateToolFunctions(kernel)
            .Select(function => new AnthropicToolDefinition
            {
                Name = function.Name,
                Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                InputSchema = BuildAnthropicInputSchema(function.Parameters)
            })
            .ToList();
    }

    private static List<AnthropicToolDefinition> BuildDeferredToolDefinitions(IEnumerable<DeferredToolInfo> deferredTools)
    {
        return deferredTools.Select(t => new AnthropicToolDefinition
        {
            Name = t.Name,
            Description = ToolPromptRegistration.GetDetailedDescription(t.Name) ?? t.Description,
            InputSchema = null,
            DeferLoading = true
        }).ToList();
    }

    private static AnthropicToolDefinition BuildToolSearchToolDefinition()
    {
        return new AnthropicToolDefinition
        {
            Name = SystemToolName.ToolSearch.ToValue(),
            Description = "Search for deferred tools by name or keyword. Use 'select:ToolName1,ToolName2' to directly select tools, or enter keywords to search. Deferred tools are loaded on-demand to save context window space.",
            InputSchema = new AnthropicInputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, AnthropicSchemaProperty>
                {
                    ["query"] = new()
                    {
                        Type = "string",
                        Description = "Search query: use 'select:tool_name' for exact selection, or keywords to search by name and description"
                    }
                },
                Required = ["query"]
            }
        };
    }

    private static AnthropicInputSchema BuildAnthropicInputSchema(IReadOnlyList<IToolParam> parameters)
    {
        if (parameters.Count == 0)
        {
            return new AnthropicInputSchema();
        }

        var props = new Dictionary<string, AnthropicSchemaProperty>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            props[param.Name] = new AnthropicSchemaProperty
            {
                Type = MapClrTypeToJsonSchemaType(param.ParameterType),
                Description = string.IsNullOrEmpty(param.Description) ? null : param.Description
            };

            if (param.IsRequired)
            {
                required.Add(param.Name);
            }
        }

        return new AnthropicInputSchema
        {
            Properties = props,
            Required = required.Count > 0 ? required : null
        };
    }

    #endregion

    #region 请求发送

    private async Task<AnthropicMessagesResponse> SendAnthropicRequestAsync(
        AnthropicMessagesRequest request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, AnthropicJsonContext.Default.AnthropicMessagesRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = GetChatEndpoint(Config);
        var response = await HttpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ExtractRateLimitHeaders(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        AnthropicMessagesResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(responseJson, AnthropicJsonContext.Default.AnthropicMessagesResponse);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize Anthropic messages response: {ex.Message}", ex);
        }

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize Anthropic messages response");
        }

        return result;
    }

    internal static IReadOnlyList<ApiMessage> ConvertAnthropicResponseToApiMessages(AnthropicMessagesResponse response)
    {
        var textParts = new StringBuilder();
        var thinkingParts = new StringBuilder();
        var toolUseBlocks = new List<(string Id, string Name, string Input)>();
        var webSearchResults = new List<string>();

        foreach (var block in response.Content)
        {
            switch (block.Type)
            {
                case AnthropicContentBlockType.Thinking:
                    if (block.Thinking != null)
                        thinkingParts.Append(block.Thinking);
                    break;
                case AnthropicContentBlockType.Text:
                    textParts.Append(block.Text);
                    break;
                case AnthropicContentBlockType.ToolUse:
                    var inputJson = block.Input switch
                    {
                        string s => s,
                        JsonElement je => je.GetRawText(),
                        _ => "{}"
                    };
                    toolUseBlocks.Add((block.Id ?? "", block.Name ?? "", inputJson));
                    break;
                case AnthropicContentBlockType.ServerToolUse:
                    break;
                case AnthropicContentBlockType.WebSearchToolResult:
                    if (block.Content is JsonElement contentEl)
                    {
                        if (contentEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentEl.EnumerateArray())
                            {
                                var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
                                {
                                    textParts.Append($"[{title}]({url})\n");
                                }
                            }
                            webSearchResults.Add(contentEl.GetRawText());
                        }
                        else
                        {
                            var errorCode = contentEl.TryGetProperty("error_code", out var ec) ? ec.GetString() : "unknown";
                            textParts.Append($"Web search error: {errorCode}\n");
                        }
                    }
                    break;
            }
        }

        var metadata = new Dictionary<string, JsonElement>
        {
            ["Id"] = JsonElementHelper.FromString(response.Id),
            ["FinishReason"] = JsonElementHelper.FromString(response.StopReason?.ToValue()),
            ["Model"] = JsonElementHelper.FromString(response.Model)
        };

        if (thinkingParts.Length > 0)
        {
            metadata["thinking_content"] = JsonElementHelper.FromString(thinkingParts.ToString());
        }

        if (webSearchResults.Count > 0)
        {
            metadata["web_search_results"] = JsonElementHelper.FromString($"[{string.Join(",", webSearchResults)}]");
        }

        if (response.Usage != null)
        {
            var tokenUsage = BuildTokenUsage(response.Usage);

            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }

        if (toolUseBlocks.Count > 0)
        {
            var first = toolUseBlocks[0];
            metadata["ToolCall"] = JsonElementHelper.FromString(first.Name);
            metadata["ToolCallId"] = JsonElementHelper.FromString(first.Id);
            metadata["ToolCallArguments"] = JsonElementHelper.FromString(first.Input);

            var openaiToolCalls = toolUseBlocks.Select((tc, i) => new OpenAIToolCall
            {
                Index = i,
                Id = tc.Id,
                Type = "function",
                Function = new OpenAIToolCallFunction
                {
                    Name = tc.Name,
                    Arguments = tc.Input
                }
            }).ToList();
            metadata["ToolCalls"] = JsonElementHelper.FromObject(openaiToolCalls, NativeJsonContext.Default.ListOpenAIToolCall);
        }

        var chatContent = textParts.Length > 0 ? textParts.ToString() : null;
        return new List<ApiMessage> { new(MessageRole.Assistant, chatContent, metadata) };
    }

    private async IAsyncEnumerable<StreamEvent> SendAnthropicStreamingRequestAsync(
        AnthropicMessagesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, AnthropicJsonContext.Default.AnthropicMessagesRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = GetChatEndpoint(Config);
        var response = await HttpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ExtractRateLimitHeaders(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var messageId = string.Empty;
        var modelName = string.Empty;
        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var serverToolUseTracker = new Dictionary<int, (string ToolUseId, string? LastQuery, StringBuilder JsonBuilder)>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("event: "))
            {
                continue;
            }

            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            AnthropicStreamingEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize(data, AnthropicJsonContext.Default.AnthropicStreamingEvent);
            }
            catch (JsonException)
            {
                continue;
            }

            if (evt == null) continue;

            switch (evt.Type)
            {
                case AnthropicStreamingEventType.MessageStart:
                    if (evt.Message != null)
                    {
                        messageId = evt.Message.Id;
                        modelName = evt.Message.Model;
                    }
                    break;

                case AnthropicStreamingEventType.ContentBlockStart:
                    if (evt.ContentBlock != null && evt.Index.HasValue)
                    {
                        var idx = evt.Index.Value;
                        if (evt.ContentBlock.Type == AnthropicContentBlockType.ToolUse)
                        {
                            toolCallAccumulator[idx] = (
                                evt.ContentBlock.Id ?? "",
                                evt.ContentBlock.Name ?? "",
                                new StringBuilder()
                            );
                        }
                        else if (evt.ContentBlock.Type == AnthropicContentBlockType.ServerToolUse)
                        {
                            var serverToolUseId = evt.ContentBlock.Id ?? "";
                            serverToolUseTracker[idx] = (serverToolUseId, null, new StringBuilder());

                            var metadata = new Dictionary<string, JsonElement>
                            {
                                ["Id"] = JsonElementHelper.FromString(messageId),
                                ["Model"] = JsonElementHelper.FromString(modelName),
                                ["server_tool_use"] = JsonElementHelper.FromBoolean(true),
                                ["tool_use_id"] = JsonElementHelper.FromString(serverToolUseId),
                                ["tool_name"] = JsonElementHelper.FromString(evt.ContentBlock.Name ?? "")
                            };
                            yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelName, metadata);
                        }
                        else if (evt.ContentBlock.Type == AnthropicContentBlockType.WebSearchToolResult)
                        {
                            var searchMetadata = new Dictionary<string, JsonElement>
                            {
                                ["Id"] = JsonElementHelper.FromString(messageId),
                                ["Model"] = JsonElementHelper.FromString(modelName),
                                ["web_search_result"] = JsonElementHelper.FromBoolean(true),
                                ["tool_use_id"] = JsonElementHelper.FromString(evt.ContentBlock.Id ?? "")
                            };

                            if (evt.ContentBlock.Content is JsonElement contentEl)
                            {
                                if (contentEl.ValueKind == JsonValueKind.Array)
                                {
                                    var links = new StringBuilder();
                                    foreach (var item in contentEl.EnumerateArray())
                                    {
                                        var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                        var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                                        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
                                        {
                                            links.Append($"[{title}]({url})\n");
                                        }
                                    }
                                    if (links.Length > 0)
                                    {
                                        searchMetadata["search_links"] = JsonElementHelper.FromString(links.ToString());
                                    }
                                }
                                else if (contentEl.ValueKind == JsonValueKind.Object)
                                {
                                    var errorCode = contentEl.TryGetProperty("error_code", out var ec) ? ec.GetString() : "unknown";
                                    searchMetadata["search_error"] = JsonElementHelper.FromString(errorCode);
                                }
                            }

                            yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelName, searchMetadata);
                        }
                    }
                    break;

                case AnthropicStreamingEventType.ContentBlockDelta:
                    if (evt.Delta == null || !evt.Index.HasValue) break;
                    {
                        var idx = evt.Index.Value;
                        var delta = evt.Delta;

                        if (delta.Type == AnthropicDeltaType.ThinkingDelta && delta.Thinking != null)
                        {
                            var metadata = new Dictionary<string, JsonElement>
                            {
                                ["Id"] = JsonElementHelper.FromString(messageId),
                                ["Model"] = JsonElementHelper.FromString(modelName),
                                ["thinking_content"] = JsonElementHelper.FromBoolean(true)
                            };
                            yield return new StreamEvent(MessageRole.Assistant, delta.Thinking, modelName, metadata);
                        }
                        else if (delta.Type == AnthropicDeltaType.TextDelta && delta.Text != null)
                        {
                            var metadata = new Dictionary<string, JsonElement>
                            {
                                ["Id"] = JsonElementHelper.FromString(messageId),
                                ["Model"] = JsonElementHelper.FromString(modelName)
                            };
                            yield return new StreamEvent(MessageRole.Assistant, delta.Text, modelName, metadata);
                        }
                        else if (delta.Type == AnthropicDeltaType.InputJsonDelta && delta.PartialJson != null)
                        {
                            if (toolCallAccumulator.TryGetValue(idx, out var existing))
                            {
                                existing.Arguments.Append(delta.PartialJson);
                            }

                            if (serverToolUseTracker.TryGetValue(idx, out var tracker))
                            {
                                tracker.JsonBuilder.Append(delta.PartialJson);
                                var partialJson = tracker.JsonBuilder.ToString();

                                var queryMatch = System.Text.RegularExpressions.Regex.Match(
                                    partialJson, @"""query""\s*:\s*""((?:[^""\\]|\\.)*)""");
                                if (queryMatch.Success)
                                {
                                    var extractedQuery = queryMatch.Groups[1].Value;
                                    extractedQuery = extractedQuery.Replace("\\\"", "\"")
                                        .Replace("\\\\", "\\")
                                        .Replace("\\n", "\n");

                                    if (extractedQuery != tracker.LastQuery)
                                    {
                                        serverToolUseTracker[idx] = (tracker.ToolUseId, extractedQuery, tracker.JsonBuilder);
                                        var queryUpdateMetadata = new Dictionary<string, JsonElement>
                                        {
                                            ["Id"] = JsonElementHelper.FromString(messageId),
                                            ["Model"] = JsonElementHelper.FromString(modelName),
                                            ["server_tool_use"] = JsonElementHelper.FromBoolean(true),
                                            ["tool_use_id"] = JsonElementHelper.FromString(tracker.ToolUseId),
                                            ["tool_name"] = JsonElementHelper.FromString("web_search"),
                                            ["query_update"] = JsonElementHelper.FromString(extractedQuery)
                                        };
                                        yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelName, queryUpdateMetadata);
                                    }
                                }
                            }
                        }
                    }
                    break;

                case AnthropicStreamingEventType.MessageDelta:
                    if (evt.Delta?.StopReason != null || evt.Usage != null)
                    {
                        var metadata = new Dictionary<string, JsonElement>
                        {
                            ["Id"] = JsonElementHelper.FromString(messageId),
                            ["Model"] = JsonElementHelper.FromString(modelName),
                            ["FinishReason"] = JsonElementHelper.FromString(evt.Delta?.StopReason?.ToValue())
                        };

                        if (evt.Usage != null)
                        {
                            var tokenUsage = BuildTokenUsage(evt.Usage);

                            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
                        }

                        if (evt.Delta?.StopReason == AnthropicStopReason.ToolUse && toolCallAccumulator.Count > 0)
                        {
                            var first = toolCallAccumulator.Values.First();
                            metadata["ToolCall"] = JsonElementHelper.FromString(first.Name);
                            metadata["ToolCallId"] = JsonElementHelper.FromString(first.Id);
                            metadata["ToolCallArguments"] = JsonElementHelper.FromString(first.Arguments.ToString());

                            if (toolCallAccumulator.Count > 1)
                            {
                                var allCallsJson = "[" + string.Join(",", toolCallAccumulator.Values.Select(tc => $"{{\"Name\":\"{tc.Name}\",\"Id\":\"{tc.Id}\"}}")) + "]";
                                metadata["AllToolCalls"] = JsonElementHelper.FromJson(allCallsJson);
                            }
                        }

                        yield return new StreamEvent(MessageRole.Assistant, string.Empty, modelName, metadata);
                    }
                    break;

                case AnthropicStreamingEventType.MessageStop:
                    yield break;
            }
        }
    }

    #endregion

    private static TokenUsage BuildTokenUsage(AnthropicUsage usage)
    {
        return CacheProtocol.MapUsage(usage);
    }
}
