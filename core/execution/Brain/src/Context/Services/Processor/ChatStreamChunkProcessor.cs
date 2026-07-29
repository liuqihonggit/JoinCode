﻿﻿﻿﻿﻿namespace Core.Context;

/// <summary>
/// 流式块处理动作
/// </summary>
public enum ChunkAction
{
    /// <summary>跳过当前块，继续处理下一个</summary>
    Continue,
    /// <summary>发射事件并继续</summary>
    Yield,
    /// <summary>发射事件并跳出循环（工具调用或循环输出检测）</summary>
    Break
}

/// <summary>
/// 一轮迭代的累积状态
/// </summary>
public sealed partial class IterationState
{
    /// <summary>累积的助手文本响应</summary>
    public StringBuilder FullResponse { get; } = new();
    /// <summary>累积的思考内容</summary>
    public StringBuilder ThinkingResponse { get; } = new();
    /// <summary>流式响应中的 Token 用量</summary>
    public TokenUsage? StreamUsage { get; set; }
    /// <summary>流式响应中的模型 ID</summary>
    public string? StreamModelId { get; set; }
    /// <summary>检测到的工具调用名称</summary>
    public string? ToolCallName { get; set; }
    /// <summary>工具调用 ID</summary>
    public string? ToolCallId { get; set; }
    /// <summary>工具调用参数（已解析）</summary>
    public Dictionary<string, JsonElement>? ToolCallArguments { get; set; }
    /// <summary>
    /// 本轮检测到的全部工具调用列表（支持单响应多工具调用）
    /// 对齐 TS: 同一 LLM 响应中的多个 tool_calls 按顺序执行
    /// </summary>
    public List<ToolCallEntry> ToolCalls { get; } = new();
    /// <summary>是否检测到工具调用</summary>
    public bool IsToolCallDetected => ToolCallName is not null;
}

/// <summary>
/// 流式块处理结果
/// </summary>
public sealed record StreamChunkResult
{
    /// <summary>
    /// 处理动作
    /// </summary>
    public required ChunkAction Action { get; init; }

    /// <summary>
    /// 要发射的事件列表
    /// </summary>
    public required IReadOnlyList<ChatStreamEvent> Events { get; init; }
}

/// <summary>
/// 聊天流式块处理器 — 解析 LLM 流式响应块并生成事件
/// 负责元数据解析、思考/内容分离、循环检测、用量提取
/// </summary>
[Register]
public sealed partial class ChatStreamChunkProcessor : IChatStreamChunkProcessor
{
    private readonly IOutputLoopDetector _loopDetector;
    private readonly IChatUsageProcessor _usageProcessor;
    [Inject] private readonly ILogger<ChatStreamChunkProcessor>? _logger;

    /// <summary>
    /// 初始化流式块处理器
    /// </summary>
    public ChatStreamChunkProcessor(
        IOutputLoopDetector loopDetector,
        IChatUsageProcessor usageProcessor,
        ILogger<ChatStreamChunkProcessor>? logger = null)
    {
        _loopDetector = loopDetector;
        _usageProcessor = usageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// 创建一轮迭代的状态 — 每轮工具调用前调用
    /// </summary>
    public IterationState CreateIterationState() => new();

    /// <summary>
    /// 处理单个流式块
    /// </summary>
    public StreamChunkResult ProcessChunk(StreamEvent chunk, IterationState state)
    {
        // 1. 工具调用检测
        if (chunk.Metadata?.TryGetValue("ToolCall", out var tcEl) == true && tcEl.ValueKind == JsonValueKind.String)
        {
            state.ToolCallName = ToolCallRepairService.RepairToolName(tcEl.GetString());
            state.ToolCallId = chunk.Metadata?.TryGetValue("ToolCallId", out var idEl) == true && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() : null;
            if (chunk.Metadata?.TryGetValue("ToolCallArguments", out var argsEl) == true && argsEl.ValueKind == JsonValueKind.String)
            {
                var rawArgs = argsEl.GetString();
                var jsonRepair = ToolCallRepairService.RepairJson(rawArgs);
                state.ToolCallArguments = JsonArgumentParser.Parse(jsonRepair.Success ? jsonRepair.RepairedJson : rawArgs);
            }

            // 解析全部工具调用列表（支持单响应多工具调用）
            // AllToolCalls 格式: [{"Id":"...","Name":"...","Arguments":"..."},...]
            if (chunk.Metadata is not null && chunk.Metadata.TryGetValue("AllToolCalls", out var allEl) && allEl.ValueKind == JsonValueKind.Array)
            {
                ParseAllToolCalls(allEl, state);
            }
            else
            {
                // 单工具调用场景，用已解析的单个工具调用填充列表
                state.ToolCalls.Add(new ToolCallEntry
                {
                    Id = state.ToolCallId,
                    Name = state.ToolCallName ?? string.Empty,
                    Arguments = state.ToolCallArguments is not null
                        ? JsonSerializer.Serialize(state.ToolCallArguments, ChatServiceJsonContext.Default.DictionaryStringJsonElement)
                        : "{}"
                });
            }

            return new StreamChunkResult { Action = ChunkAction.Break, Events = [] };
        }

        // 2. server_tool_use 进度事件
        if (chunk.Metadata?.TryGetValue("server_tool_use", out var stuEl) == true && stuEl.ValueKind == JsonValueKind.True)
        {
            var toolName = chunk.Metadata?.TryGetValue("tool_name", out var tnEl) == true && tnEl.ValueKind == JsonValueKind.String
                ? tnEl.GetString() ?? "web_search" : "web_search";
            var toolUseId = chunk.Metadata?.TryGetValue("tool_use_id", out var tuiEl) == true && tuiEl.ValueKind == JsonValueKind.String
                ? tuiEl.GetString() : null;

            if (chunk.Metadata?.TryGetValue("query_update", out var quEl) == true && quEl.ValueKind == JsonValueKind.String)
            {
                var query = quEl.GetString() ?? "";
                return new StreamChunkResult
                {
                    Action = ChunkAction.Continue,
                    Events = [ChatStreamEvent.ToolProgress(toolName, "query_update", query, toolUseId)]
                };
            }

            return new StreamChunkResult
            {
                Action = ChunkAction.Continue,
                Events = [ChatStreamEvent.ToolProgress(toolName, "server_tool_use", "Searching…", toolUseId)]
            };
        }

        // 3. web_search_result 进度事件
        if (chunk.Metadata?.TryGetValue("web_search_result", out var wsrEl) == true && wsrEl.ValueKind == JsonValueKind.True)
        {
            var toolName = "web_search";
            var toolUseId = chunk.Metadata?.TryGetValue("tool_use_id", out var tuiEl2) == true && tuiEl2.ValueKind == JsonValueKind.String
                ? tuiEl2.GetString() : null;

            if (chunk.Metadata?.TryGetValue("search_links", out var slEl) == true && slEl.ValueKind == JsonValueKind.String)
            {
                var links = slEl.GetString() ?? "";
                var linkCount = links.Count(c => c == '\n');
                return new StreamChunkResult
                {
                    Action = ChunkAction.Continue,
                    Events = [ChatStreamEvent.ToolProgress(toolName, "search_results_received",
                        $"Found {linkCount} results", toolUseId)]
                };
            }

            if (chunk.Metadata?.TryGetValue("search_error", out var seEl) == true && seEl.ValueKind == JsonValueKind.String)
            {
                var errorCode = seEl.GetString() ?? "unknown";
                return new StreamChunkResult
                {
                    Action = ChunkAction.Continue,
                    Events = [ChatStreamEvent.ToolProgress(toolName, "search_results_received",
                        $"Search error: {errorCode}", toolUseId)]
                };
            }

            return new StreamChunkResult { Action = ChunkAction.Continue, Events = [] };
        }

        // 4. 思考/内容分离
        var isThinking = chunk.Metadata?.ContainsKey("thinking_content") == true
            || chunk.Metadata?.ContainsKey("reasoning_content") == true;

        var events = new List<ChatStreamEvent>();
        var shouldBreak = false;

        if (chunk.Content is not null)
        {
            if (isThinking)
            {
                state.ThinkingResponse.Append(chunk.Content);
                events.Add(ChatStreamEvent.Thinking(chunk.Content));
            }
            else
            {
                state.FullResponse.Append(chunk.Content);

                var loopResult = _loopDetector.Detect(state.FullResponse.ToString());
                if (loopResult.IsLoopDetected)
                {
                    _logger?.LogWarning("[ChatStreamChunkProcessor] 检测到LLM循环输出，第{N}次触发，重复模式长度: {Len}, 重复次数: {Count}",
                        loopResult.LoopTriggerCount, loopResult.RepeatedPattern?.Length ?? 0, loopResult.RepeatCount);
                    events.Add(ChatStreamEvent.Text(chunk.Content));
                    events.Add(ChatStreamEvent.LoopDetected(loopResult.LoopTriggerCount, loopResult.LoopStartIndex, loopResult.RepeatedPattern));
                }
                else
                {
                    events.Add(ChatStreamEvent.Text(chunk.Content));
                }
            }
        }

        // 5. 用量提取
        if (chunk.Metadata?.TryGetValue("Usage", out var usageEl) == true && usageEl.ValueKind == JsonValueKind.Object)
        {
            try
            {
                state.StreamUsage = JsonSerializer.Deserialize<TokenUsage>(usageEl, ChatServiceJsonContext.Default.TokenUsage);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                _logger?.LogWarning(ex, "TokenUsage 反序列化失败，跳过 Usage 数据");
            }
        }

        // 6. 限流数据提取
        if (chunk.Metadata is not null)
        {
            _usageProcessor.TryExtractRateLimitData(chunk.Metadata);
        }

        // 7. 模型 ID 追踪
        if (chunk.ModelId is not null)
        {
            state.StreamModelId = chunk.ModelId;
        }

        return new StreamChunkResult
        {
            Action = shouldBreak ? ChunkAction.Break : (events.Count > 0 ? ChunkAction.Yield : ChunkAction.Continue),
            Events = events
        };
    }

    /// <summary>
    /// 解析 AllToolCalls 元数据为 ToolCallEntry 列表
    /// 格式: [{"Id":"...","Name":"...","Arguments":"..."},...]
    /// 对每个工具调用独立执行参数修复（RepairToolName + RepairJson）
    /// </summary>
    private static void ParseAllToolCalls(JsonElement allEl, IterationState state)
    {
        foreach (var item in allEl.EnumerateArray())
        {
            string? id = null;
            string? name = null;
            var arguments = "{}";

            if (item.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                id = idProp.GetString();
            if (item.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                name = nameProp.GetString();
            if (item.TryGetProperty("Arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                arguments = argsProp.GetString() ?? "{}";

            if (name is null) continue;

            // 对每个工具调用独立执行工具名修复
            var repairedName = ToolCallRepairService.RepairToolName(name);
            // 对每个工具调用独立执行 JSON 修复
            var jsonRepair = ToolCallRepairService.RepairJson(arguments);
            var repairedArgs = jsonRepair.Success ? jsonRepair.RepairedJson : arguments;

            state.ToolCalls.Add(new ToolCallEntry
            {
                Id = id,
                Name = repairedName,
                Arguments = repairedArgs
            });
        }
    }
}
