namespace NativeTui.Host;

/// <summary>
/// 进程内事件直连消费器 — IEventConsumer 的 native_tui 宿主实现（第四实现：CLI DX / GUI / bridge / 进程内直连），
/// ADR 0143 阶段二零协议税核心：引擎回调实时构造内存 BridgeEvent record 推入并发队列
/// （Actor 邮箱模型：回合线程生产，主循环 tick 排空消费，无共享锁，对齐 Core BridgeEventPump 的 ConcurrentQueue 先例），
/// 不经 JSON 序列化。语义与 engine BridgeEventConsumer 完全对齐（唯一差异：WriteEvent 序列化 stdout → 队列写内存对象）：
/// OnText → token_delta；OnThinking 回合内单次信号 + 墙钟差结算 thinking_ended；
/// OnToolStart/OnToolEnd → tool_call/tool_result（摘要截断 200 + error 前缀 + StructuredPatch → DiffHunk 投影）；
/// OnDone → assistant_message(完整文本)；OnToolProgress/OnLoopDetected/OnTimingSummary 不消费。
/// </summary>
public sealed class InProcessEventConsumer : IResettableEventConsumer {
    /// <summary>工具结果摘要截断上限（对齐 engine BridgeEventConsumer 的 200 字符显示截断）。</summary>
    private const int ToolResultSummaryLimit = 200;

    private readonly ConcurrentQueue<BridgeEvent> _events;
    private readonly Func<long> _wallClock;
    private readonly StringBuilder _fullResponse = new();
    private long? _thinkingStartMs;
    private bool _thinkingSignalled;

    /// <summary>构造函数 — 自建队列（独立使用场景）；注入墙钟（测试注入确定性时钟）。</summary>
    public InProcessEventConsumer(Func<long>? wallClock = null)
        : this([], wallClock) {
    }

    /// <summary>构造函数 — 注入共享队列（EngineTurnClient 场景：生命周期事件与回合内部事件同队 FIFO 保序）。</summary>
    public InProcessEventConsumer(ConcurrentQueue<BridgeEvent> events, Func<long>? wallClock = null) {
        _events = events;
        _wallClock = wallClock ?? (static () => Environment.TickCount64);
    }

    /// <summary>重置回合内状态（新一轮对话前由宿主调用，防跨回合泄漏；队列不清 — 回合边界由生命周期事件标记）。</summary>
    public void Reset() {
        _fullResponse.Clear();
        _thinkingStartMs = null;
        _thinkingSignalled = false;
    }

    /// <summary>收到文本内容 → token_delta(length)；思考结束时结算 thinking_ended。</summary>
    public void OnText(string content) {
        EndThinkingIfActive();
        if (content.Length == 0)
            return;
        _fullResponse.Append(content);
        _events.Enqueue(new BridgeEvent.TokenDelta(content.Length));
    }

    /// <summary>收到思考内容 — 回合内首个思考发 thinking_started 并记录墙钟（回合内单次信号，Reset 后新回合再发）。</summary>
    public void OnThinking(string thinking) {
        if (thinking.Length == 0 || _thinkingSignalled)
            return;
        _thinkingSignalled = true;
        _thinkingStartMs = _wallClock();
        _events.Enqueue(new BridgeEvent.ThinkingStarted());
    }

    /// <summary>工具调用开始 → tool_call(name, args)；思考结束时结算 thinking_ended。</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments) {
        EndThinkingIfActive();
        _events.Enqueue(new BridgeEvent.ToolCall(toolName, arguments ?? string.Empty));
    }

    /// <summary>工具调用结束 → tool_result(截断摘要 + error 标记 + 结构化补丁投影)。</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) {
        _events.Enqueue(new BridgeEvent.ToolResult(BuildSummary(resultText, isError), ProjectPatch(patch)));
    }

    /// <summary>工具调用进度 — TUI 不消费，忽略（对齐 bridge 形态）。</summary>
    public void OnToolProgress(string toolName, string progressType, string? progressMessage) {
    }

    /// <summary>检测到循环输出 — TUI 不消费，忽略（对齐 bridge 形态）。</summary>
    public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) {
    }

    /// <summary>计时摘要 — TUI 不消费，忽略（对齐 bridge 形态）。</summary>
    public void OnTimingSummary(string summary) {
    }

    /// <summary>流式响应完成 → 思考结算 + assistant_message(完整文本 + Markdown 投影；无语法快路径降级纯文本)。</summary>
    public void OnDone(TokenUsage? usage, string? modelId) {
        EndThinkingIfActive();
        if (_fullResponse.Length == 0)
            return;
        var fullText = _fullResponse.ToString();
        _events.Enqueue(new BridgeEvent.AssistantMessage(fullText, MarkdigProjector.ProjectOptional(fullText)));
    }

    /// <summary>排空事件队列 — 主循环每 tick 调用，取走全部待处理事件（ConcurrentQueue 多生产单消费安全）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() {
        List<BridgeEvent>? drained = null;
        while (_events.TryDequeue(out var bridgeEvent))
            (drained ??= []).Add(bridgeEvent);
        return (IReadOnlyList<BridgeEvent>?)drained ?? [];
    }

    /// <summary>写入一条事件 — 无界 Channel TryWrite 永远成功（回合线程不阻塞）。</summary>
    private void WriteEvent(BridgeEvent bridgeEvent) => _events.Enqueue(bridgeEvent);

    /// <summary>思考结束结算（进入产出或回合结束时调用，幂等）。</summary>
    private void EndThinkingIfActive() {
        if (_thinkingStartMs is not long start)
            return;
        _thinkingStartMs = null;
        WriteEvent(new BridgeEvent.ThinkingEnded(Math.Max(0, _wallClock() - start)));
    }

    /// <summary>工具结果摘要：error 前缀 + 截断。</summary>
    private static string BuildSummary(string? resultText, bool isError) {
        var text = resultText ?? string.Empty;
        var truncated = text.Length > ToolResultSummaryLimit
            ? string.Concat(text.AsSpan(0, ToolResultSummaryLimit), "...")
            : text;
        return isError ? $"[error] {truncated}" : truncated;
    }

    /// <summary>引擎 StructuredPatchHunk[] → Core 零依赖 DiffHunk[] 渲染投影（空/ null → null 维持纯文本摘要形态）。</summary>
    private static DiffHunk[]? ProjectPatch(StructuredPatchHunk[]? patch) =>
        patch is null || patch.Length == 0 ? null
            : patch.Select(static hunk => new DiffHunk {
                Header = hunk.Header,
                Lines = hunk.Lines.Select(static line => new CoreDiffLine {
                    Kind = (DiffLineKind)(byte)line.Type,
                    Content = line.Content,
                }).ToArray(),
            }).ToArray();
}
