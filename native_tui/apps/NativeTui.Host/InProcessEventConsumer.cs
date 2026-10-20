namespace NativeTui.Host;

/// <summary>
/// 进程内事件直连消费器 — IEventConsumer 的 native_tui 宿主实现（第四实现：CLI DX / GUI / bridge / 进程内直连），
/// ADR 0143 阶段二零协议税核心：引擎回调实时构造内存 BridgeEvent record 推入 Channel
/// （Actor 邮箱模型：回合线程生产，主循环 tick 排空消费，无共享锁），不经 JSON 序列化。
/// 语义与 engine BridgeEventConsumer 完全对齐（唯一差异：WriteEvent 序列化 stdout → Channel 写内存对象）：
/// OnText → token_delta；OnThinking 回合内单次信号 + 墙钟差结算 thinking_ended；
/// OnToolStart/OnToolEnd → tool_call/tool_result（摘要截断 200 + error 前缀）；
/// OnDone → assistant_message(完整文本)；OnToolProgress/OnLoopDetected/OnTimingSummary 不消费。
/// </summary>
public sealed class InProcessEventConsumer : IResettableEventConsumer, IDisposable {
    /// <summary>工具结果摘要截断上限（对齐 engine BridgeEventConsumer 的 200 字符显示截断）。</summary>
    private const int ToolResultSummaryLimit = 200;

    private readonly Channel<BridgeEvent> _events;
    private readonly Func<long> _wallClock;
    private readonly StringBuilder _fullResponse = new();
    private long? _thinkingStartMs;
    private bool _thinkingSignalled;

    /// <summary>构造函数 — 注入墙钟（测试注入确定性时钟）；Channel 无界保证回合线程 TryWrite 永不阻塞。</summary>
    public InProcessEventConsumer(Func<long>? wallClock = null) {
        _events = Channel.CreateUnbounded<BridgeEvent>(new UnboundedChannelOptions {
            SingleReader = true,
        });
        _wallClock = wallClock ?? (static () => Environment.TickCount64);
    }

    /// <summary>重置回合内状态（新一轮对话前由宿主调用，防跨回合泄漏；Channel 不清 — 回合边界由生命周期事件标记）。</summary>
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
        _events.Writer.TryWrite(new BridgeEvent.TokenDelta(content.Length));
    }

    /// <summary>收到思考内容 — 回合内首个思考发 thinking_started 并记录墙钟（回合内单次信号，Reset 后新回合再发）。</summary>
    public void OnThinking(string thinking) {
        if (thinking.Length == 0 || _thinkingSignalled)
            return;
        _thinkingSignalled = true;
        _thinkingStartMs = _wallClock();
        _events.Writer.TryWrite(new BridgeEvent.ThinkingStarted());
    }

    /// <summary>工具调用开始 → tool_call(name, args)；思考结束时结算 thinking_ended。</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments) {
        EndThinkingIfActive();
        _events.Writer.TryWrite(new BridgeEvent.ToolCall(toolName, arguments ?? string.Empty));
    }

    /// <summary>工具调用结束 → tool_result(截断摘要 + error 标记)。</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) {
        _events.Writer.TryWrite(new BridgeEvent.ToolResult(BuildSummary(resultText, isError)));
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

    /// <summary>流式响应完成 → 思考结算 + assistant_message(完整文本)。</summary>
    public void OnDone(TokenUsage? usage, string? modelId) {
        EndThinkingIfActive();
        if (_fullResponse.Length > 0)
            _events.Writer.TryWrite(new BridgeEvent.AssistantMessage(_fullResponse.ToString()));
    }

    /// <summary>排空事件队列 — 主循环每 tick 调用，取走全部待处理事件（单读者约定下线程安全）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() {
        List<BridgeEvent>? drained = null;
        while (_events.Reader.TryRead(out var bridgeEvent))
            (drained ??= []).Add(bridgeEvent);
        return (IReadOnlyList<BridgeEvent>?)drained ?? [];
    }

    /// <summary>写入一条事件 — 无界 Channel TryWrite 永远成功（回合线程不阻塞）。</summary>
    private void WriteEvent(BridgeEvent bridgeEvent) => _events.Writer.TryWrite(bridgeEvent);

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

    /// <summary>释放 — 关闭 Channel 写端（主循环排空后读者自然完成）。</summary>
    public void Dispose() => _events.Writer.TryComplete();
}
