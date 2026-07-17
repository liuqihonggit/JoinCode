
using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// QueryEngine.Create 方法的参数封装
/// </summary>
public sealed record QueryEngineOptions(
    IChatClient Kernel,
    IToolRegistry ToolRegistry,
    QueryEngineConfig? Config = null,
    ILogger<QueryEngine>? Logger = null);

/// <summary>
/// QueryEngine - 查询引擎，处理AI对话和工具调用
/// 参考Claude Code的QueryEngine实现
/// 可选依赖通过 IQueryMiddleware 中间件管道注入，构造函数仅保留核心依赖
/// </summary>
[Register]
public sealed partial class QueryEngine : IQueryEngine
{
    private readonly IChatClient _kernel;
    private readonly IToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<QueryEngine>? _logger;
    private readonly QueryEngineConfig _config;
    private readonly IServiceProvider? _serviceProvider;
    private MiddlewarePipeline<QueryMiddlewareContext>? _pipeline;
    private QueryOptions? _currentOptions;

    /// <summary>
    /// DI 构造函数 — 中间件通过 IServiceProvider 延迟解析，避免构造时
    /// 解析 IEnumerable&lt;IQueryMiddleware&gt; 导致循环依赖
    /// （IdleReminderMiddleware → IToolIdleReminderService → ITodoService →
    ///   ITaskRuntime → IWorkflowTaskExecutor → IAgentLifecycleManager → IQueryEngine → ♾️）
    /// </summary>
    public QueryEngine(
        IChatClient kernel,
        IToolRegistry toolRegistry,
        IOptions<QueryEngineConfig> configOptions,
        IServiceProvider? serviceProvider = null,
        ILogger<QueryEngine>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _config = configOptions?.Value ?? new QueryEngineConfig();
        _serviceProvider = serviceProvider;
        _logger = logger;
        // 管道延迟构建 — 首次 QueryAsync 时才解析 IQueryMiddleware 集合
    }

    /// <summary>
    /// 延迟构建中间件管道：DI 中间件 + 核心执行中间件
    /// </summary>
    private MiddlewarePipeline<QueryMiddlewareContext> GetOrCreatePipeline()
    {
        if (_pipeline is not null)
            return _pipeline;

        var allMiddlewares = new List<IMiddleware<QueryMiddlewareContext>>();
        if (_serviceProvider is not null)
        {
            var middlewares = _serviceProvider.GetService<IEnumerable<IQueryMiddleware>>();
            if (middlewares is not null)
                allMiddlewares.AddRange(middlewares);
        }
        allMiddlewares.Add(new QueryCoreMiddleware(this));

        _pipeline = new MiddlewarePipeline<QueryMiddlewareContext>(allMiddlewares, onError: null);
        return _pipeline;
    }

    /// <summary>
    /// 创建带默认配置的QueryEngine（无 DI 中间件，用于测试/非 DI 场景）
    /// </summary>
    public static QueryEngine Create(QueryEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new QueryEngine(
            options.Kernel,
            options.ToolRegistry,
            Microsoft.Extensions.Options.Options.Create(options.Config ?? new QueryEngineConfig()),
            serviceProvider: null,
            logger: options.Logger);
    }

    /// <summary>
    /// 执行查询
    /// </summary>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in QueryAsync(userInput, chatHistory, options: null, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 执行查询（带工具过滤选项）
    /// </summary>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        QueryOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var context = new QueryMiddlewareContext
        {
            UserInput = userInput,
            ChatHistory = chatHistory,
            Options = options,
            Config = _config,
            Kernel = _kernel,
            ToolRegistry = _toolRegistry,
            Logger = _logger,
        };

        _currentOptions = options;
        chatHistory.AddUserMessage(userInput);
        _logger?.LogInformation("[QueryEngine] 开始处理查询: {Input}", userInput);

        var pipeline = GetOrCreatePipeline();
        await pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        foreach (var chunk in context.OutputChunks)
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 核心执行循环 — 由 QueryCoreMiddleware 调用
    /// </summary>
    private async Task ExecuteCoreLoopAsync(QueryMiddlewareContext context, CancellationToken cancellationToken)
    {
        var retryCount = 0;

        while (context.TotalToolCalls < _config.MaxToolCallIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 迭代前钩子（USD 预算检查等）
            foreach (var hook in context.BeforeIterationHooks)
            {
                await hook(context, cancellationToken).ConfigureAwait(false);
            }

            if (context.ShouldStop)
            {
                break;
            }

            QueryIterationResult? iterationResult = null;
            var success = false;

            // 重试机制
            while (!success && retryCount <= _config.Retry.MaxRetries)
            {
                try
                {
                    iterationResult = await ExecuteIterationInternalAsync(
                        context, cancellationToken).ConfigureAwait(false);
                    success = true;
                    retryCount = 0;
                }
                catch (Exception ex) when (retryCount < _config.Retry.MaxRetries && IsRetryable(ex))
                {
                    retryCount++;
                    var delay = CalculateRetryDelay(retryCount);
                    _logger?.LogWarning(ex, "[QueryEngine] 迭代执行失败，{RetryCount}/{MaxRetries} 次重试，等待 {DelayMs}ms",
                        retryCount, _config.Retry.MaxRetries, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!success || iterationResult == null)
            {
                context.OutputChunks.Add(new QueryStreamChunk
                {
                    Type = AgentStreamChunkType.Error,
                    Content = $"执行失败，已达到最大重试次数 ({_config.Retry.MaxRetries})"
                });
                return;
            }

            // 处理内容输出
            foreach (var chunk in iterationResult.Chunks)
            {
                context.OutputChunks.Add(chunk);
            }

            // 更新上下文的 Token 使用信息
            context.InputTokens = iterationResult.InputTokens;
            context.OutputTokens = iterationResult.OutputTokens;
            context.ToolName = iterationResult.ToolCall?.ToolName;
            context.HasToolCall = iterationResult.ToolCall is not null;

            // LLM 调用后钩子（Token 预算消耗、成本追踪等）
            foreach (var hook in context.AfterLlmCallHooks)
            {
                await hook(context, cancellationToken).ConfigureAwait(false);
            }

            // 处理查询完成（无工具调用）
            if (iterationResult.ToolCall == null)
            {
                context.Stopwatch.Stop();
                context.IsQueryComplete = true;

                // 查询完成钩子（空闲提醒、停止 Hook 等）
                foreach (var hook in context.OnCompleteHooks)
                {
                    await hook(context, cancellationToken).ConfigureAwait(false);
                }

                _logger?.LogInformation("[QueryEngine] 查询完成，耗时 {ElapsedMs}ms", context.Stopwatch.ElapsedMilliseconds);

                var content = iterationResult.Content ?? string.Empty;
                context.OutputChunks.Add(new QueryStreamChunk
                {
                    Type = AgentStreamChunkType.Complete,
                    Content = content,
                    ExecutionTimeMs = context.Stopwatch.ElapsedMilliseconds,
                    TotalToolCalls = context.TotalToolCalls,
                    CostUsd = context.TotalCostUsd,
                    CacheSafeParams = BuildCacheSafeParams(context)
                });
                return;
            }

            context.TotalToolCalls++;

            context.OutputChunks.Add(new QueryStreamChunk
            {
                Type = AgentStreamChunkType.ToolCallStart,
                ToolName = iterationResult.ToolCall.ToolName,
                ToolCallNumber = context.TotalToolCalls
            });

            // 执行工具调用
            var toolResult = await ExecuteToolAsync(iterationResult.ToolCall, context.Options, cancellationToken).ConfigureAwait(false);

            context.OutputChunks.Add(new QueryStreamChunk
            {
                Type = AgentStreamChunkType.ToolCallEnd,
                ToolName = iterationResult.ToolCall.ToolName,
                ToolResult = toolResult,
                ToolCallNumber = context.TotalToolCalls
            });

            // 将工具结果添加到对话历史
            AddToolResultToHistory(context, iterationResult.ToolCall, toolResult);

            // 工具调用后钩子（内容替换预算检查、递减回报检测、历史裁剪、空闲提醒等）
            foreach (var hook in context.AfterToolCallHooks)
            {
                await hook(context, cancellationToken).ConfigureAwait(false);
            }

            if (context.ShouldBreak)
            {
                break;
            }
        }

        _logger?.LogWarning("[QueryEngine] 达到最大工具调用次数限制: {Max}", _config.MaxToolCallIterations);
        context.OutputChunks.Add(new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Error,
            Content = $"达到最大工具调用次数限制 ({_config.MaxToolCallIterations})"
        });
    }

    private async Task<QueryIterationResult> ExecuteIterationInternalAsync(
        QueryMiddlewareContext context,
        CancellationToken cancellationToken)
    {
        var parameters = new LlmParameters
        {
            Temperature = _config.Temperature,
            MaxTokens = _config.MaxTokens,
            TopP = _config.TopP
        };
        var executionSettings = parameters.ToExecutionSettings(ToolChoice.AutoInvoke);

        // 对齐 TS executeForkedSkill: 传递 effort 给子智能体
        // 如果 QueryOptions 指定了 EffortLevel，覆盖默认值
        if (_currentOptions?.EffortLevel is { } effortLevel)
        {
            executionSettings = new JoinCode.Abstractions.LLM.ChatOptions
            {
                Temperature = executionSettings.Temperature,
                MaxTokens = executionSettings.MaxTokens,
                TopP = executionSettings.TopP,
                FrequencyPenalty = executionSettings.FrequencyPenalty,
                PresencePenalty = executionSettings.PresencePenalty,
                ToolChoice = executionSettings.ToolChoice,
                EffortLevel = effortLevel,
            };
        }

        // 对齐 Reasonix Coordinator: 双模型分离 — 按请求指定模型
        if (!string.IsNullOrEmpty(_currentOptions?.ModelId))
        {
            var modelId = _currentOptions.ModelId ?? string.Empty;
            executionSettings = new JoinCode.Abstractions.LLM.ChatOptions
            {
                Temperature = executionSettings.Temperature,
                MaxTokens = executionSettings.MaxTokens,
                TopP = executionSettings.TopP,
                FrequencyPenalty = executionSettings.FrequencyPenalty,
                PresencePenalty = executionSettings.PresencePenalty,
                ToolChoice = executionSettings.ToolChoice,
                EffortLevel = executionSettings.EffortLevel,
                FastMode = true,
                FastModelId = modelId,
                ExtensionData = new Dictionary<string, System.Text.Json.JsonElement> { ["model"] = JoinCode.Abstractions.Utils.JsonElementHelper.FromString(modelId) },
            };
        }

        var chatCompletionService = _kernel.GetChatCompletionService();
        var responseBuilder = new StringBuilder();
        var chunks = new List<QueryStreamChunk>();
        ToolCallRequest? pendingToolCall = null;
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var streamChunk in chatCompletionService.GetStreamEventContentsAsync(
            context.ChatHistory, executionSettings, _kernel, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = streamChunk.Content ?? string.Empty;

            // 检查工具调用
            if (streamChunk.Metadata?.TryGetValue("ToolCall", out var toolCallEl) == true &&
                toolCallEl.ValueKind == JsonValueKind.String)
            {
                var toolCallName = toolCallEl.GetString();
                var toolCallId = streamChunk.Metadata?.TryGetValue("ToolCallId", out var idEl) == true && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                var toolCallArguments = streamChunk.Metadata?.TryGetValue("ToolCallArguments", out var argsEl) == true && argsEl.ValueKind == JsonValueKind.String
                    ? argsEl.GetString()
                    : null;

                pendingToolCall = new ToolCallRequest
                {
                    ToolName = toolCallName ?? string.Empty,
                    ToolCallId = toolCallId,
                    RawArguments = toolCallArguments,
                    Arguments = ExtractArguments(streamChunk.Metadata, toolCallArguments)
                };
                break;
            }

            // 累积内容
            if (!string.IsNullOrEmpty(content))
            {
                responseBuilder.Append(content);
                chunks.Add(new QueryStreamChunk
                {
                    Type = AgentStreamChunkType.Content,
                    Content = content
                });
            }

            // 追踪Token使用量
            if (streamChunk.Metadata?.TryGetValue("InputTokens", out var inputTokensEl) == true &&
                inputTokensEl.ValueKind == JsonValueKind.Number)
            {
                try { inputTokens = inputTokensEl.GetInt32(); }
                catch (FormatException ex)
                {
                    _logger?.LogWarning(ex, "[QueryEngine] InputTokens 值不是有效 int32(可能是浮点/科学计数法),跳过");
                }
            }
            if (streamChunk.Metadata?.TryGetValue("OutputTokens", out var outputTokensEl) == true &&
                outputTokensEl.ValueKind == JsonValueKind.Number)
            {
                try { outputTokens = outputTokensEl.GetInt32(); }
                catch (FormatException ex)
                {
                    _logger?.LogWarning(ex, "[QueryEngine] OutputTokens 值不是有效 int32(可能是浮点/科学计数法),跳过");
                }
            }
        }

        if (pendingToolCall == null)
        {
            var finalContent = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(finalContent))
            {
                context.ChatHistory.AddAssistantMessage(finalContent);
            }

            return new QueryIterationResult
            {
                Content = finalContent,
                Chunks = chunks,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        // 执行工具调用
        _logger?.LogInformation("[QueryEngine] 执行工具调用 #{Num}: {ToolName}", context.TotalToolCalls + 1, pendingToolCall.ToolName);

        return new QueryIterationResult
        {
            ToolCall = pendingToolCall,
            Chunks = chunks,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCallRequest request, QueryOptions? options, CancellationToken cancellationToken)
    {
        if (options is not null && !options.IsToolAllowed(request.ToolName))
        {
            _logger?.LogWarning("[QueryEngine] 工具被过滤拒绝: {ToolName}", request.ToolName);
            return new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = $"工具 {request.ToolName} 不在当前代理的可用工具列表中" } },
                IsError = true
            };
        }

        try
        {
            var result = await _toolRegistry.ExecuteToolAsync(request.ToolName, request.Arguments, cancellationToken).ConfigureAwait(false);
            _currentOptions?.ProgressTracker?.RecordToolUse(request.ToolName);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[QueryEngine] 工具调用失败: {ToolName}", request.ToolName);
            _currentOptions?.ProgressTracker?.RecordToolUse(request.ToolName);
            return new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = $"工具调用失败: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private void AddToolResultToHistory(QueryMiddlewareContext context, ToolCallRequest toolCall, ToolResult result)
    {
        // 对齐 TS convertResultContentToContentBlocks — 分离文本和非文本内容
        var textContents = new List<string>();
        var nonTextContents = new List<ToolContent>();

        foreach (var c in result.Content)
        {
            if (result.IsError)
            {
                if (!string.IsNullOrEmpty(c.Text))
                    textContents.Add($"Error: {c.Text}");
            }
            else if (c.Type == ToolContentType.Image && !string.IsNullOrEmpty(c.Data) && !string.IsNullOrEmpty(c.MimeType))
            {
                // 图片类型 — 保留为多模态内容块
                nonTextContents.Add(c);
            }
            else if (!string.IsNullOrEmpty(c.Text))
            {
                textContents.Add(c.Text);
            }
            // resource 类型（二进制写盘）已在 McpClientToolHandlers 中转为文本路径
        }

        var toolResultText = string.Join("\n", textContents);

        // 内容替换 — 由 ContentReplacementMiddleware 通过上下文提供 IContentReplacementService
        // MaybePersistLargeToolResult 是即时持久化（非预算机制），在添加历史时调用
        if (context.ContentReplacementService is not null && !result.IsError && !string.IsNullOrEmpty(toolResultText))
        {
            var sessionId = _currentOptions?.SessionId ?? "default";
            var replacement = context.ContentReplacementService.MaybePersistLargeToolResult(
                toolCall.ToolName, toolCall.ToolCallId ?? string.Empty, toolResultText, sessionId);
            if (replacement is not null)
                toolResultText = replacement;
        }

        var assistantMetadata = toolCall.ToolCallId != null
            ? ToolCallEntry.BuildAssistantMetadata([new() { Id = toolCall.ToolCallId, Name = toolCall.ToolName, Arguments = toolCall.RawArguments ?? "{}" }])
            : new Dictionary<string, JsonElement>();
        context.ChatHistory.Add(new ApiMessage(MessageRole.Assistant, null, assistantMetadata));

        var toolMetadata = ToolCallEntry.BuildToolResultMetadata(toolCall.ToolCallId, toolCall.ToolName);
        context.ChatHistory.Add(new ApiMessage(MessageRole.Tool, toolResultText, toolMetadata)
        {
            // 对齐 TS — 将多模态内容块传递到 ApiMessage，由 ChatService 转换为 LLM API 格式
            ContentBlocks = nonTextContents.Count > 0 ? nonTextContents : null
        });
    }

    private Dictionary<string, JsonElement> ExtractArguments(IReadOnlyDictionary<string, JsonElement>? metadata, string? rawArguments)
    {
        if (!string.IsNullOrEmpty(rawArguments))
        {
            var jsonRepair = ToolCallRepairService.RepairJson(rawArguments);
            var parsed = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArguments);
            if (parsed.Count > 0)
                return parsed;

            _logger?.LogWarning("Failed to parse tool call arguments JSON: {Args}", rawArguments);
        }

        if (metadata == null)
            return new Dictionary<string, JsonElement>();

        var fallback = metadata
            .Where(kvp => kvp.Key.StartsWith("Argument_"))
            .ToDictionary(
                kvp => kvp.Key["Argument_".Length..],
                kvp => kvp.Value);
        return fallback;
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is HttpRequestException or TimeoutException or TaskCanceledException;
    }

    private int CalculateRetryDelay(int retryCount)
    {
        if (!_config.Retry.EnableExponentialBackoff)
        {
            return _config.Retry.RetryDelayMs;
        }

        // 指数退避: delay * 2^(retryCount-1)
        var delay = _config.Retry.RetryDelayMs * Math.Pow(2, retryCount - 1);
        return (int)Math.Min(delay, WorkflowConstants.Retry.MaxDelayMs);
    }

    // IQueryEngine 接口实现
    public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var chatHistory = new MessageList();
        var result = new StringBuilder();

        foreach (var chunk in QueryAsync(query, chatHistory, cancellationToken).ToBlockingEnumerable(cancellationToken))
        {
            if (chunk.Content != null)
            {
                result.Append(chunk.Content);
            }
        }

        return Task.FromResult(result.ToString());
    }

    public IQueryService GetChatCompletionService()
    {
        return _kernel.GetChatCompletionService();
    }

    public IChatClient GetKernel()
    {
        return _kernel;
    }

    private JoinCode.Abstractions.LLM.Chat.CacheSafeParams? BuildCacheSafeParams(QueryMiddlewareContext context)
    {
        var existing = context.Options?.CacheSafeParams;

        return new JoinCode.Abstractions.LLM.Chat.CacheSafeParams
        {
            RenderedSystemPrompt = existing?.RenderedSystemPrompt,
            ModelId = existing?.ModelId,
            ToolNames = existing?.ToolNames,
            UserContext = existing?.UserContext is not null
                ? new Dictionary<string, string>(existing.UserContext)
                : null,
            SystemContext = existing?.SystemContext is not null
                ? new Dictionary<string, string>(existing.SystemContext)
                : null,
            ContentReplacementState = context.Options?.ContentReplacementState?.Clone()
        };
    }

    /// <summary>
    /// 核心执行中间件 — 管道最内层，执行 LLM 调用 + 工具执行循环
    /// </summary>
    private sealed class QueryCoreMiddleware : IMiddleware<QueryMiddlewareContext>
    {
        private readonly QueryEngine _engine;


        public QueryCoreMiddleware(QueryEngine engine) => _engine = engine;

        public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
        {
            // 核心中间件不调用 next() — 它是管道的终端
            return _engine.ExecuteCoreLoopAsync(context, ct);
        }
    }
}

/// <summary>
/// 工具调用请求
/// </summary>
internal sealed class ToolCallRequest
{
    public string ToolName { get; set; } = string.Empty;
    public string? ToolCallId { get; set; }
    public string? RawArguments { get; set; }
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();
}

/// <summary>
/// 迭代执行结果
/// </summary>
internal sealed class QueryIterationResult
{
    public string? Content { get; init; }
    public ToolCallRequest? ToolCall { get; init; }
    public List<QueryStreamChunk> Chunks { get; init; } = new();
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

/// <summary>
/// Token 成本追踪接口 — 仅用于 Query 层内部的简单 Token 计数
/// 注意: 与 JoinCode.Abstractions.Interfaces.ICostTracker（完整成本追踪）不同
/// </summary>
public interface ITokenCostTracker
{
    void TrackUsage(int inputTokens, int outputTokens);
    decimal GetTotalCost();
    (int InputTokens, int OutputTokens, decimal Cost) GetUsage();
}

/// <summary>
/// 空 Token 成本追踪器
/// </summary>
public partial class NullTokenCostTracker : ITokenCostTracker
{
    public void TrackUsage(int inputTokens, int outputTokens) { }
    public decimal GetTotalCost() => 0m;
    public (int InputTokens, int OutputTokens, decimal Cost) GetUsage() => (0, 0, 0m);
}

/// <summary>
/// Token 成本追踪器实现
/// </summary>
public partial class TokenCostTracker : ITokenCostTracker
{
    private readonly CostTrackingConfig _config;
    private int _totalInputTokens;
    private int _totalOutputTokens;

    public TokenCostTracker(CostTrackingConfig config)
    {
        _config = config;
    }

    public void TrackUsage(int inputTokens, int outputTokens)
    {
        if (!_config.Enabled) return;

        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
    }

    public decimal GetTotalCost()
    {
        if (!_config.Enabled) return 0m;

        var inputCost = (_totalInputTokens / 1000m) * _config.InputTokenCostPer1K;
        var outputCost = (_totalOutputTokens / 1000m) * _config.OutputTokenCostPer1K;
        return inputCost + outputCost;
    }

    public (int InputTokens, int OutputTokens, decimal Cost) GetUsage()
    {
        var cost = GetTotalCost();
        return (_totalInputTokens, _totalOutputTokens, cost);
    }
}
