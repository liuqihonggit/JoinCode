namespace Api.LLM.QueryServices.OpenAI;

/// <summary>
/// OpenAI 协议 QueryService 实现 — 覆盖 OpenAI 兼容协议（chat/completions 端点 + Bearer Token）
/// Azure / Agnes 等 OpenAI 兼容供应商可继承本类，覆写 URL/端点/认证差异部分
/// 注：URL/端点/认证差异已通过 IProviderDefinition 多态在基类处理，本类仅实现协议请求/响应
/// </summary>
public class OpenAIQueryService : QueryServiceBase
{
    public OpenAIQueryService(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null)
        : base(config, httpClient, logger, fs)
    {
    }

    /// <summary>非流式：构建 OpenAI 请求 → 发送 → 转换为 ApiMessage</summary>
    public override async Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(chatHistory, executionSettings, stream: false, kernel);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        return response.Choices.Select(c => ConvertToApiMessage(c, response.Usage)).ToList();
    }

    /// <summary>流式：构建 OpenAI 请求 → 发送流式 → 累积工具调用 → yield StreamEvent</summary>
    public override async IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(chatHistory, executionSettings, stream: true, kernel);
        var responseStream = SendStreamingRequestAsync(request, cancellationToken);

        var toolCallAccumulator = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        var isFirstChunk = true;

        await foreach (var chunk in responseStream)
        {
            // 最终 usage chunk: choices 为空但包含 usage 字段 —
            // stream_options.include_usage=true 时, OpenAI API 在最后发送一个仅含 usage 的 chunk
            // 必须将其转换为 TokenUsage 并通过 metadata["Usage"] 传递给消费者(用于缓存命中分析)
            if (chunk.Choices.Count == 0)
            {
                if (chunk.Usage is null) continue;

                var tokenUsage = new TokenUsage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens)
                {
                    CacheReadInputTokens = chunk.Usage.PromptTokensDetails?.CachedTokens ?? 0
                };

                if (chunk.Usage.PromptCacheHitTokens.HasValue || chunk.Usage.PromptCacheMissTokens.HasValue)
                {
                    tokenUsage.CacheCreationInputTokens = chunk.Usage.PromptCacheMissTokens ?? 0;
                    tokenUsage.CacheReadInputTokens = chunk.Usage.PromptCacheHitTokens ?? 0;
                }

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
                var tokenUsage = new TokenUsage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens)
                {
                    CacheReadInputTokens = chunk.Usage.PromptTokensDetails?.CachedTokens ?? 0
                };

                if (chunk.Usage.PromptCacheHitTokens.HasValue || chunk.Usage.PromptCacheMissTokens.HasValue)
                {
                    tokenUsage.CacheCreationInputTokens = chunk.Usage.PromptCacheMissTokens ?? 0;
                    tokenUsage.CacheReadInputTokens = chunk.Usage.PromptCacheHitTokens ?? 0;
                }

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

                var first = entries[0];
                metadata["ToolCall"] = JsonElementHelper.FromString(first.Name);
                metadata["ToolCallId"] = JsonElementHelper.FromString(first.Id ?? "");
                metadata["ToolCallArguments"] = JsonElementHelper.FromString(first.Arguments);
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

    internal OpenAIChatRequest CreateRequest(MessageList chatHistory, ChatOptions? settings, bool stream, IChatClient? kernel)
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
        }

        return msg;
    }

    private static List<OpenAIToolCall>? ConvertToOpenAIToolCalls(object? toolCallsObj)
    {
        return toolCallsObj switch
        {
            List<OpenAIToolCall> direct => direct,
            JsonElement je when je.ValueKind == JsonValueKind.Array => je.EnumerateArray().Select(item => new OpenAIToolCall
            {
                Id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null,
                Type = "function",
                Function = new OpenAIToolCallFunction
                {
                    Name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null,
                    Arguments = item.TryGetProperty("Arguments", out var argsProp) ? argsProp.GetString() : null
                }
            }).ToList(),
            _ => null
        };
    }

    private static List<OpenAITool> BuildToolsFromKernel(IChatClient kernel)
    {
        return kernel.Plugins.PluginNames
            .Select(name => kernel.Plugins.GetPlugin(name))
            .Where(p => p is not null)
            .SelectMany(p => p!.Functions)
            .Select(function => new OpenAITool
            {
                Function = new OpenAIFunctionDefinition
                {
                    Name = function.Name,
                    Description = ToolPromptRegistration.GetDetailedDescription(function.Name) ?? function.Description,
                    Parameters = BuildParameters(function.Parameters)
                }
            })
            .ToList();
    }

    private static OpenAIFunctionParameters? BuildParameters(IReadOnlyList<IToolParam> parameters)
    {
        if (parameters.Count == 0) return null;

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

    private static string MapClrTypeToJsonSchemaType(Type? type)
    {
        if (type == null) return "string";
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Int32 or TypeCode.Int64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            TypeCode.Boolean => "boolean",
            _ => "string"
        };
    }

    #endregion

    #region 请求发送

    private async Task<OpenAIChatResponse> SendRequestAsync(OpenAIChatRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = GetChatEndpoint(Config);
        var response = await HttpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
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
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = GetChatEndpoint(Config);
        var response = await HttpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ExtractRateLimitHeaders(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") yield break;

            OpenAIChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, NativeJsonContext.Default.OpenAIChatChunk);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // FormatException: JSON 源生成器在数字解析失败(如遇到非 ASCII 数字字符)时抛出
                // JsonException: 标准 JSON 格式错误
                // 两者都应跳过当前 chunk 而非终止整个流
                Logger?.LogWarning(ex, "Failed to deserialize streaming chunk, skipping: {Data}", data);
                continue;
            }

            if (chunk != null)
            {
                yield return chunk;
            }
        }
    }

    internal static ApiMessage ConvertToApiMessage(OpenAIChoice choice, OpenAIUsage? usage)
    {
        var metadata = new Dictionary<string, JsonElement> { ["FinishReason"] = JsonElementHelper.FromString(choice.FinishReason) };

        if (usage != null)
        {
            var tokenUsage = new TokenUsage(usage.PromptTokens, usage.CompletionTokens)
            {
                CacheReadInputTokens = usage.PromptTokensDetails?.CachedTokens ?? 0
            };

            if (usage.PromptCacheHitTokens.HasValue || usage.PromptCacheMissTokens.HasValue)
            {
                tokenUsage.CacheCreationInputTokens = usage.PromptCacheMissTokens ?? 0;
                tokenUsage.CacheReadInputTokens = usage.PromptCacheHitTokens ?? 0;
            }

            metadata["Usage"] = JsonElementHelper.FromObject(tokenUsage, NativeJsonContext.Default.TokenUsage);
        }

        if (choice.Message.ReasoningContent != null)
        {
            metadata["reasoning_content"] = JsonElementHelper.FromString(choice.Message.ReasoningContent);
        }

        if (choice.Message.ToolCalls is { Count: > 0 })
        {
            var firstToolCall = choice.Message.ToolCalls[0];
            metadata["ToolCall"] = JsonElementHelper.FromString(firstToolCall.Function?.Name);
            metadata["ToolCallId"] = JsonElementHelper.FromString(firstToolCall.Id);
            metadata["ToolCallArguments"] = JsonElementHelper.FromString(firstToolCall.Function?.Arguments);
            metadata["ToolCalls"] = JsonElementHelper.FromObject(choice.Message.ToolCalls, NativeJsonContext.Default.ListOpenAIToolCall);
        }

        return new ApiMessage(
            ConvertRole(choice.Message.Role),
            choice.Message.Content,
            metadata);
    }

    #endregion
}
