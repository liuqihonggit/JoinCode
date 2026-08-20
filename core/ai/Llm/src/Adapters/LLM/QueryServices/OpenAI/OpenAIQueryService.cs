namespace Api.LLM.QueryServices.OpenAI;

using Api.LLM.CacheProtocol;
using JoinCode.Abstractions.Diagnostics;

/// <summary>
/// OpenAI 协议 QueryService 实现 — 覆盖 OpenAI 兼容协议（chat/completions 端点 + Bearer Token）
/// Azure / Agnes 等 OpenAI 兼容供应商可继承本类，覆写 URL/端点/认证差异部分
/// 注：URL/端点/认证差异已通过 IProviderDefinition 多态在基类处理，本类仅实现协议请求/响应
/// </summary>
public class OpenAIQueryService : QueryServiceBase
{
    private static readonly OpenAICacheProtocol CacheProtocol = new();

    public OpenAIQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null, ResilientHttpExecutor? resilientExecutor = null)
        : base(config, httpClient, logger, fs, resilientExecutor)
    {
    }

    /// <summary>非流式：构建 OpenAI 请求 → 发送 → 转换为 ApiMessage</summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        Logger?.LogDebug("[WIRE {CallId}] 非流式请求入口 | 消息数={MsgCount}", CallTrace.CurrentId, chatHistory.Count);
        var request = CreateRequest(chatHistory, executionSettings, stream: false, kernel);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        Logger?.LogDebug("[WIRE {CallId}] 非流式响应 | choices={ChoiceCount}", CallTrace.CurrentId, response.Choices.Count);

        // 两阶段工具加载: 非流式检测 tool_description_request → 发送第二次请求
        var firstContent = response.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        if (firstContent.Contains("tool_description_request") && kernel != null)
        {
            Logger?.LogDebug("[WIRE {CallId}] 非流式收到 tool_description_request, 发送第二次请求", CallTrace.CurrentId);
            var secondRequest = CreateSecondRequestWithDescriptions(request, firstContent, kernel);
            var secondResponse = await SendRequestAsync(secondRequest, cancellationToken).ConfigureAwait(false);
            return secondResponse.Choices.Select(c => ConvertToApiMessage(c, secondResponse.Usage)).ToList();
        }

        return response.Choices.Select(c => ConvertToApiMessage(c, response.Usage)).ToList();
    }

    /// <summary>流式：构建 OpenAI 请求 → 发送流式 → 累积工具调用 → yield StreamEvent</summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Logger?.LogDebug("[WIRE {CallId}] 流式请求入口 | 消息数={MsgCount}", CallTrace.CurrentId, chatHistory.Count);
        var request = CreateRequest(chatHistory, executionSettings, stream: true, kernel);
        var responseStream = SendStreamingRequestAsync(request, cancellationToken);

        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var isFirstChunk = true;
        var descRequestAccumulator = new StringBuilder();

        await foreach (var chunk in responseStream)
        {
            // 最终 usage chunk: choices 为空但包含 usage 字段 —
            // stream_options.include_usage=true 时, OpenAI API 在最后发送一个仅含 usage 的 chunk
            // 必须将其转换为 TokenUsage 并通过 metadata["Usage"] 传递给消费者(用于缓存命中分析)
            if (chunk.Choices.Count == 0)
            {
                if (chunk.Usage is null) continue;

                var tokenUsage = BuildTokenUsage(chunk.Usage);

                var usageMetadata = new Dictionary<string, JsonElement>
                {
                    ["Id"] = JsonElementHelper.FromString(chunk.Id),
                    ["FinishReason"] = JsonElementHelper.FromString(OpenAIFinishReasonConstants.Stop),
                    ["Created"] = JsonElementHelper.FromInt64(chunk.Created),
                    ["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage)
                };
                yield return new StreamEvent(MessageRole.Assistant, string.Empty, chunk.Model, usageMetadata);
                continue;
            }

            var choice = chunk.Choices[0];
            var content = choice.Delta?.Content ?? string.Empty;
            var role = ConvertRole(choice.Delta?.Role);

            // 两阶段工具加载: 检测 tool_description_request → 构建工具描述 → 发送第二次请求
            // 支持分多 delta 发送: 累积 content,检测累积内容是否包含完整 tool_description_request JSON
            if (!string.IsNullOrEmpty(content))
                descRequestAccumulator.Append(content);
            var accumulatedContent = descRequestAccumulator.ToString();
            if (kernel != null && accumulatedContent.Contains("tool_description_request") && accumulatedContent.TrimEnd().EndsWith('}'))
            {
                Logger?.LogDebug("[WIRE {CallId}] 收到 tool_description_request, 发送第二次请求", CallTrace.CurrentId);
                var secondRequest = CreateSecondRequestWithDescriptions(request, accumulatedContent, kernel);
                var secondStream = SendStreamingRequestAsync(secondRequest, cancellationToken);
                var secondAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
                var secondFirstChunk = true;

                await foreach (var sc in secondStream)
                {
                    if (sc.Choices.Count == 0)
                    {
                        if (sc.Usage is null) continue;
                        var tu = BuildTokenUsage(sc.Usage);
                        var um = new Dictionary<string, JsonElement>
                        {
                            ["Id"] = JsonElementHelper.FromString(sc.Id),
                            ["FinishReason"] = JsonElementHelper.FromString(OpenAIFinishReasonConstants.Stop),
                            ["Created"] = JsonElementHelper.FromInt64(sc.Created),
                            ["Usage"] = JsonElementHelper.FromObject(tu, NativeJsonContext.Default.TokenUsage)
                        };
                        yield return new StreamEvent(MessageRole.Assistant, string.Empty, sc.Model, um);
                        continue;
                    }

                    var sc2 = sc.Choices[0];
                    var scContent = sc2.Delta?.Content ?? string.Empty;
                    var scRole = ConvertRole(sc2.Delta?.Role);

                    if (sc2.Delta?.ToolCalls != null)
                    {
                        foreach (var tc in sc2.Delta.ToolCalls)
                        {
                            var idx = tc.Index ?? 0;
                            if (!string.IsNullOrEmpty(tc.Id))
                                secondAccumulator[idx] = (tc.Id, tc.Function?.Name ?? "", new StringBuilder());
                            if (tc.Function?.Arguments != null && secondAccumulator.TryGetValue(idx, out var ex))
                                ex.Arguments.Append(tc.Function.Arguments);
                        }
                    }

                    var scMeta = new Dictionary<string, JsonElement>
                    {
                        ["Id"] = JsonElementHelper.FromString(sc.Id),
                        ["FinishReason"] = JsonElementHelper.FromString(sc2.FinishReason),
                        ["Created"] = JsonElementHelper.FromInt64(sc.Created)
                    };

                    if (sc.Usage is not null)
                    {
                        var tu = BuildTokenUsage(sc.Usage);
                        scMeta["Usage"] = JsonElementHelper.FromObject(tu, NativeJsonContext.Default.TokenUsage);
                    }

                    if (sc2.Delta?.ReasoningContent != null)
                        scMeta["reasoning_content"] = JsonElementHelper.FromBoolean(true);

                    if (sc2.FinishReason == OpenAIFinishReasonConstants.ToolCalls && secondAccumulator.Count > 0)
                    {
                        var entries = secondAccumulator
                            .OrderBy(kv => kv.Key)
                            .Select(kv => new ToolCallEntry { Id = kv.Value.Id, Name = kv.Value.Name, Arguments = kv.Value.Arguments.ToString() })
                            .ToList();
                        scMeta["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries);
                    }

                    var scStreamContent = sc2.Delta?.ReasoningContent ?? scContent;
                    if (secondFirstChunk)
                    {
                        secondFirstChunk = false;
                        var rlh = GetLastRateLimitHeaders();
                        if (rlh != null)
                            foreach (var kvp in rlh)
                                scMeta[$"ratelimit_{kvp.Key}"] = JsonElementHelper.FromString(kvp.Value);
                    }
                    yield return new StreamEvent(scRole, scStreamContent, sc.Model, scMeta);
                }
                yield break;
            }

            if (choice.Delta?.ToolCalls != null)
            {
                foreach (var tc in choice.Delta.ToolCalls)
                {
                    var idx = tc.Index ?? 0;

                    if (!string.IsNullOrEmpty(tc.Id))
                    {
                        toolCallAccumulator[idx] = (tc.Id, tc.Function?.Name ?? "", new StringBuilder());
                    }

                    if (tc.Function?.Arguments != null && toolCallAccumulator.TryGetValue(idx, out var existing))
                    {
                        existing.Arguments.Append(tc.Function.Arguments);
                    }
                }
            }

            var metadata = new Dictionary<string, JsonElement>
            {
                ["Id"] = JsonElementHelper.FromString(chunk.Id),
                ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason),
                ["Created"] = JsonElementHelper.FromInt64(chunk.Created)
            };

            // 部分供应商(如 DeepSeek)可能在中间 chunk 也带 usage — 合并到 metadata
            if (chunk.Usage is not null)
            {
                var tokenUsage = BuildTokenUsage(chunk.Usage);

                metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
            }

            if (choice.Delta?.ReasoningContent != null)
            {
                metadata["reasoning_content"] = JsonElementHelper.FromBoolean(true);
            }

            if (choice.FinishReason == OpenAIFinishReasonConstants.ToolCalls && toolCallAccumulator.Count > 0)
            {
                var entries = toolCallAccumulator
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new ToolCallEntry
                    {
                        Id = kv.Value.Id,
                        Name = kv.Value.Name,
                        Arguments = kv.Value.Arguments.ToString()
                    })
                    .ToList();
                metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries);
            }

            var streamContent = choice.Delta?.ReasoningContent ?? content;
            if (isFirstChunk)
            {
                isFirstChunk = false;
                var rateLimitHeaders = GetLastRateLimitHeaders();
                if (rateLimitHeaders != null)
                {
                    foreach (var kvp in rateLimitHeaders)
                    {
                        metadata[$"ratelimit_{kvp.Key}"] = JsonElementHelper.FromString(kvp.Value);
                    }
                }
            }
            yield return new StreamEvent(role, streamContent, chunk.Model, metadata);
        }
    }

    #region 请求构建

    internal virtual OpenAIChatRequest CreateRequest(MessageList chatHistory, ChatOptions? settings, bool stream, IChatClient? kernel)
    {
        var messages = chatHistory.Select(ConvertToOpenAIMessage).ToList();

        var modelId = Config.ModelId;
        if (settings?.FastMode == true && !string.IsNullOrEmpty(settings.FastModelId))
            modelId = settings.FastModelId;

        var request = new OpenAIChatRequest
        {
            Model = modelId,
            Messages = messages,
            Stream = stream,
            Temperature = settings?.Temperature,
            MaxTokens = settings?.MaxTokens,
            TopP = settings?.TopP,
            FrequencyPenalty = settings?.FrequencyPenalty,
            PresencePenalty = settings?.PresencePenalty
        };

        // 流式请求时显式要求 API 返回 usage(含 cached_tokens) —
        // 真实 OpenAI API 在最后一个 chunk(choices 为空)返回 usage 字段
        if (stream)
        {
            request.StreamOptions = new OpenAIStreamOptions { IncludeUsage = true };
        }

        if (settings?.EffortLevel is not null)
        {
            request.ReasoningEffort = ChatOptions.EffortToReasoningEffort(settings.EffortLevel.Value);
        }

        if (settings?.ThinkingEnabled == true)
        {
            request.Thinking = new OpenAIThinkingOptions { Type = "enabled" };
        }

        if (settings?.ToolChoice == ToolChoice.AutoInvoke && kernel != null)
        {
            var (tools, toolGroups) = BuildToolsFromKernel(kernel);
            if (tools.Count > 0)
            {
                request.Tools = tools;
                request.ToolChoice = "auto";
            }
            if (toolGroups.Count > 0)
            {
                request.ToolGroups = toolGroups;
            }
        }

        return request;
    }

    internal static OpenAIApiMessage ConvertToOpenAIMessage(ApiMessage m)
    {
        var role = m.Role;
        var content = m.Content;

        var msg = new OpenAIApiMessage
        {
            Role = ConvertRoleToString(role),
            Content = content
        };

        if (m.Role == MessageRole.Assistant && m.Metadata != null)
        {
            if (m.Metadata.TryGetValue("ToolCalls", out var toolCallsObj))
            {
                msg.ToolCalls = ConvertToOpenAIToolCalls(toolCallsObj);
                if (msg.ToolCalls is { Count: > 0 })
                {
                    msg.Content = null;
                }
            }
        }
        else if (m.Role == MessageRole.Tool && m.Metadata != null)
        {
            if (m.Metadata.TryGetValue("ToolCallId", out var toolCallIdObj) &&
                toolCallIdObj.TryGetString(out var toolCallId))
            {
                msg.ToolCallId = toolCallId;
            }

            if (m.Metadata.TryGetValue("ToolName", out var toolNameObj) &&
                toolNameObj.TryGetString(out var toolName))
            {
                msg.Name = toolName;
            }
        }

        return msg;
    }

    /// <summary>
    /// 构建工具列表 — 两阶段加载：core_tools 发完整 schema，mcp_tools 发分组+名称
    /// 其他 group name 向后兼容，发完整 schema
    /// </summary>
    internal static (List<OpenAITool> Tools, List<OpenAIToolGroup> ToolGroups) BuildToolsFromKernel(IChatClient kernel)
    {
        var tools = new List<OpenAITool>();
        var toolGroups = new List<OpenAIToolGroup>();

        foreach (var pluginName in kernel.Plugins.PluginNames)
        {
            var plugin = kernel.Plugins.GetPlugin(pluginName);
            if (plugin is not IToolGroup group)
                continue;

            if (group.Name == ToolGroupNameConstants.McpTools)
            {
                toolGroups.Add(new OpenAIToolGroup
                {
                    Name = group.Name,
                    Tools = group.Functions.Select(f => f.Name).ToList()
                });
            }
            else
            {
                foreach (var function in group.Functions)
                {
                    tools.Add(new OpenAITool
                    {
                        Function = new OpenAIFunctionDefinition
                        {
                            Name = function.Name,
                            Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                            Parameters = BuildParameters(function.Parameters)
                        }
                    });
                }
            }
        }

        return (tools, toolGroups);
    }

    /// <summary>
    /// 两阶段工具加载 — 解析 tool_description_request,构建第二次请求(含 tool_descriptions)
    /// </summary>
    internal static OpenAIChatRequest CreateSecondRequestWithDescriptions(
        OpenAIChatRequest originalRequest, string descRequestContent, IChatClient kernel)
    {
        HashSet<string> toolNames;
        try
        {
            var doc = JsonDocument.Parse(descRequestContent);
            toolNames = doc.RootElement.GetProperty("tools").EnumerateArray()
                .Select(t => t.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse tool_description_request JSON: {ex.Message} | Content: {descRequestContent[..Math.Min(descRequestContent.Length, 200)]}", ex);
        }

        var descriptions = new List<OpenAITool>();
        foreach (var pluginName in kernel.Plugins.PluginNames)
        {
            var plugin = kernel.Plugins.GetPlugin(pluginName);
            if (plugin is not IToolGroup group)
                continue;
            foreach (var function in group.Functions)
            {
                if (toolNames.Contains(function.Name))
                {
                    descriptions.Add(new OpenAITool
                    {
                        Function = new OpenAIFunctionDefinition
                        {
                            Name = function.Name,
                            Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                            Parameters = BuildParameters(function.Parameters)
                        }
                    });
                }
            }
        }

        return new OpenAIChatRequest
        {
            Model = originalRequest.Model,
            Messages = originalRequest.Messages,
            Stream = originalRequest.Stream,
            StreamOptions = originalRequest.StreamOptions,
            Temperature = originalRequest.Temperature,
            MaxTokens = originalRequest.MaxTokens,
            TopP = originalRequest.TopP,
            FrequencyPenalty = originalRequest.FrequencyPenalty,
            PresencePenalty = originalRequest.PresencePenalty,
            Tools = originalRequest.Tools,
            ToolChoice = originalRequest.ToolChoice,
            ToolGroups = originalRequest.ToolGroups,
            ReasoningEffort = originalRequest.ReasoningEffort,
            Thinking = originalRequest.Thinking,
            ToolDescriptions = descriptions
        };
    }

    private static OpenAIFunctionParameters BuildParameters(IReadOnlyList<IToolParam> parameters)
    {
        if (parameters.Count == 0) return new OpenAIFunctionParameters();

        var props = new Dictionary<string, OpenAIParameterProperty>();

        foreach (var param in parameters)
        {
            props[param.Name] = new OpenAIParameterProperty
            {
                Type = MapClrTypeToJsonSchemaType(param.ParameterType),
                Description = string.IsNullOrEmpty(param.Description) ? null : param.Description
            };
        }

        var required = parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList();

        return new OpenAIFunctionParameters
        {
            Properties = props,
            Required = required.Count > 0 ? required : null
        };
    }

    #endregion

    #region 请求发送

    private async Task<OpenAIChatResponse> SendRequestAsync(OpenAIChatRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);
        var endpoint = GetChatEndpoint(Config);

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.ChatCompletion", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        ExtractRateLimitHeaders(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        OpenAIChatResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(responseJson, NativeJsonContext.Default.OpenAIChatResponse);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new InvalidOperationException($"Failed to deserialize chat completion response: {ex.Message}", ex);
        }

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize chat completion response");
        }

        return result;
    }

    private async IAsyncEnumerable<OpenAIChatChunk> SendStreamingRequestAsync(
        OpenAIChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);
        var endpoint = GetChatEndpoint(Config);

        var response = await SendWithResilienceAsync(json, endpoint, "LLM.StreamingChatCompletion", cancellationToken,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        ExtractRateLimitHeaders(response);
        Logger?.LogDebug("[WIRE {CallId}] POST {Endpoint} → {StatusCode}", CallTrace.CurrentId, endpoint, response.StatusCode);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var chunkCount = 0;
        var contentChunks = 0;
        var toolCallChunks = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]")
            {
                Diag.WriteLine($"[WIRE {CallTrace.CurrentId}] 流结束 | chunks={chunkCount}, content={contentChunks}, toolCalls={toolCallChunks}");
                yield break;
            }

            OpenAIChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, NativeJsonContext.Default.OpenAIChatChunk);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                Logger?.LogWarning(ex, "[WIRE {CallId}] chunk 反序列化失败, 跳过", CallTrace.CurrentId);
                continue;
            }

            if (chunk != null)
            {
                chunkCount++;
                if (chunk.Choices.Count > 0)
                {
                    var choice = chunk.Choices[0];
                    if (!string.IsNullOrEmpty(choice.Delta?.Content)) contentChunks++;
                    if (choice.Delta?.ToolCalls != null && choice.Delta.ToolCalls.Count > 0) toolCallChunks++;
                }
                yield return chunk;
            }
            else
            {
                Logger?.LogWarning("[WIRE {CallId}] chunk 反序列化为 null, data={Data}", CallTrace.CurrentId, data);
            }
        }

        Diag.WriteLine($"[WIRE {CallTrace.CurrentId}] 流异常结束(无[DONE]) | chunks={chunkCount}, content={contentChunks}, toolCalls={toolCallChunks}");
    }

    internal static ApiMessage ConvertToApiMessage(OpenAIChoice choice, OpenAIUsage? usage)
    {
        var metadata = new Dictionary<string, JsonElement> { ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason) };

        if (usage != null)
        {
            var tokenUsage = BuildTokenUsage(usage);

            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }

        if (choice.Message.ReasoningContent != null)
        {
            metadata["reasoning_content"] = JsonElementHelper.FromString(choice.Message.ReasoningContent);
        }

        if (choice.Message.ToolCalls is { Count: > 0 })
        {
            metadata["ToolCalls"] = JsonElementHelper.FromObject(choice.Message.ToolCalls, NativeJsonContext.Default.ListOpenAIToolCall);
            var entries = choice.Message.ToolCalls.Select(tc => new ToolCallEntry
            {
                Id = tc.Id,
                Name = tc.Function?.Name ?? "",
                Arguments = tc.Function?.Arguments ?? "{}"
            }).ToList();
            metadata["AllToolCalls"] = ToolCallEntry.ToToolCallsJson(entries);
        }

        return new ApiMessage(
            ConvertRole(choice.Message.Role),
            choice.Message.Content,
            metadata);
    }

    #endregion

    private static TokenUsage BuildTokenUsage(OpenAIUsage usage)
    {
        return CacheProtocol.MapUsage(usage);
    }
}
