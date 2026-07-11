namespace Core.Context;

/// <summary>
/// 对话管道计时数据 — 追踪每轮对话各阶段耗时
/// </summary>
public sealed class ChatTiming
{
    private readonly System.Diagnostics.Stopwatch _total = new();
    private readonly System.Diagnostics.Stopwatch _preprocess = new();
    private readonly System.Diagnostics.Stopwatch _llmCall = new();
    private readonly System.Diagnostics.Stopwatch _postProcess = new();

    /// <summary>LLM 首 token 延迟（毫秒）</summary>
    public long FirstTokenLatencyMs { get; set; }

    /// <summary>LLM 调用总耗时（毫秒）</summary>
    public long LlmTotalMs => _llmCall.ElapsedMilliseconds;

    /// <summary>预处理耗时（毫秒）</summary>
    public long PreprocessMs => _preprocess.ElapsedMilliseconds;

    /// <summary>后处理耗时（毫秒）</summary>
    public long PostProcessMs => _postProcess.ElapsedMilliseconds;

    /// <summary>总耗时（毫秒）</summary>
    public long TotalMs => _total.ElapsedMilliseconds;

    /// <summary>LLM 调用次数（工具调用迭代）</summary>
    public int LlmCallCount { get; set; }

    public void StartTotal() => _total.Restart();
    public void StopTotal() => _total.Stop();
    public void StartPreprocess() => _preprocess.Restart();
    public void StopPreprocess() => _preprocess.Stop();
    public void StartLlmCall() => _llmCall.Restart();
    public void StopLlmCall() => _llmCall.Stop();
    public void StartPostProcess() => _postProcess.Restart();
    public void StopPostProcess() => _postProcess.Stop();

    /// <summary>
    /// 格式化计时摘要 — 用于 CLI 输出
    /// </summary>
    /// <param name="usage">可选 token 用量 — 传入时追加前缀缓存统计（省钱刚需可见性）</param>
    public string FormatSummary(TokenUsage? usage = null)
    {
        var base_summary = $"[Timing] 总耗时={TotalMs}ms | 预处理={PreprocessMs}ms | LLM={LlmTotalMs}ms(首token={FirstTokenLatencyMs}ms, 调用{LlmCallCount}次) | 后处理={PostProcessMs}ms";
        if (usage is null)
            return base_summary;
        return $"{base_summary} | 缓存=创建{usage.CacheCreationInputTokens},读取{usage.CacheReadInputTokens}";
    }
}

/// <summary>
/// 中间件共享上下文 — 携带管道各阶段的状态
/// </summary>
public sealed class ChatMiddlewareContext
{
    // === 输入 ===

    /// <summary>
    /// 用户发送的消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 干运行模式 — 不持久化对话历史、不写入助手回复
    /// 由 PreviewChatMiddleware 设置，也可由调用方初始化
    /// </summary>
    public bool IsDryRun { get; set; }

    /// <summary>
    /// 遥测 Span 名称 — 不同发送方法使用不同名称（如 "chat.send.events"、"chat.send.stream"、"chat.send.sync"）
    /// </summary>
    public string SpanName { get; init; } = "chat.send.events";

    /// <summary>
    /// 当前对话轮次号 — 由 ChatService 在创建 context 时设置
    /// </summary>
    public int ConversationTurn { get; init; }

    // === PreChatMiddleware 填充 ===

    /// <summary>
    /// 预处理结果 — 关键词分析、同义词注入、提示注入信息
    /// </summary>
    public PreprocessResult? PreprocessResult { get; set; }

    /// <summary>
    /// LLM 执行设置 — 温度、最大 token、工具选择等
    /// </summary>
    public ChatOptions? ExecutionSettings { get; set; }

    /// <summary>
    /// 提示状态快照 — 用于缓存检测
    /// </summary>
    public PromptStateSnapshot? PromptSnapshot { get; set; }

    /// <summary>
    /// 遥测 Span — 跟踪整个聊天请求
    /// </summary>
    public ITelemetrySpan? Span { get; set; }

    // === QueryLoopMiddleware 填充 ===

    /// <summary>
    /// 工具调用总次数
    /// </summary>
    public int TotalToolCalls { get; set; }

    /// <summary>
    /// 最终 token 用量
    /// </summary>
    public TokenUsage? FinalUsage { get; set; }

    /// <summary>
    /// 最终模型 ID
    /// </summary>
    public string? FinalModelId { get; set; }

    // === LoopInterventionMiddleware 填充 ===

    /// <summary>
    /// 循环检测触发次数 — 由 LoopInterventionMiddleware 设置，供上层判断漏斗级别
    /// </summary>
    public int LoopTriggerCount { get; set; }

    /// <summary>
    /// 当前 TODO 表完成数 — 循环检测触发时的快照
    /// </summary>
    public int CurrentCompletedTodoCount { get; set; }

    /// <summary>
    /// 上次循环检测时的 TODO 表完成数 — 用于判断任务是否推进
    /// </summary>
    public int PreviousCompletedTodoCount { get; set; }

    /// <summary>
    /// 任务是否有推进 — 自上次循环检测以来 TODO 完成数是否增加
    /// </summary>
    public bool HasTaskProgressed { get; set; }

    // === 计时 ===

    /// <summary>
    /// 对话管道计时数据 — 追踪每轮各阶段耗时
    /// </summary>
    public ChatTiming Timing { get; } = new();

    // === 共享 ===

    /// <summary>
    /// 工具执行上下文 — 维护技能执行时的会话级状态
    /// 由 ChatService 传入共享实例，确保管道各阶段和 ChatService 使用同一状态
    /// </summary>
    public required ToolUseContext ToolUseContext { get; init; }
}
