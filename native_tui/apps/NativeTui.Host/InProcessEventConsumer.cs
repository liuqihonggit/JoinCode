namespace NativeTui.Host;

/// <summary>
/// 进程内事件直连消费器 — IEventConsumer 的 native_tui 宿主实现（第四实现：CLI DX / GUI / bridge / 进程内直连），
/// ADR 0143 阶段二零协议税核心：引擎回调实时构造内存 BridgeEvent record 推入并发队列
/// （Actor 邮箱模型：回合线程生产，主循环 tick 排空消费，无共享锁，对齐 Core BridgeEventPump 的 ConcurrentQueue 先例），
/// 不经 JSON 序列化。语义与 engine BridgeEventConsumer 完全对齐（唯一差异：WriteEvent 序列化 stdout → 队列写内存对象）：
/// OnText → token_delta；OnThinking 回合内单次信号 + 墙钟差结算 thinking_ended；
/// OnToolStart/OnToolEnd → tool_call/tool_result（摘要截断 200 + error 前缀 + StructuredPatch → DiffHunk 投影 +

/// 原始输出预截断投影 raw_output/hidden_lines）；OnDone → assistant_message(完整文本) + token_usage(用量汇总)；
/// OnBackgroundTask → background_task(M8-8 常驻面板 upsert)。/// OnToolProgress/OnLoopDetected/OnTimingSummary 不消费。
/// </summary>
public sealed class InProcessEventConsumer : IResettableEventConsumer {
    /// <summary>工具结果摘要截断上限（对齐 engine BridgeEventConsumer 的 200 字符显示截断）。</summary>
    private const int ToolResultSummaryLimit = 200;

    /// <summary>工具结果原始输出预览行数上限（对齐基准 terminal.ts MAX_LINES_TO_SHOW=3 — 超出折叠）。</summary>
    private const int ToolResultPreviewLines = 3;

    /// <summary>工具结果原始输出假定折行宽度（进程内路径无终端宽度回传 — 保守取 80 列，对齐基准 wrapWidth 语义）。</summary>
    private const int ToolResultAssumedWrapWidth = 80;

    /// <summary>工具结果原始输出预截断字符上限（对齐基准 maxChars = MAX_LINES × wrapWidth × 4 —
    /// 防 64MB 原始输出（如二进制 dump）撑爆内存事件链与渲染管线；与 engine BridgeEventConsumer 常量镜像）。</summary>
    private const int ToolResultRawOutputLimit = ToolResultPreviewLines * ToolResultAssumedWrapWidth * 4;

    private readonly ConcurrentQueue<BridgeEvent> _events;
    private readonly Func<long> _wallClock;
    private readonly StringBuilder _fullResponse = new();
    private readonly StringBuilder _thinkingContent = new();
    private long? _thinkingStartMs;
    private bool _thinkingSignalled;
    private int _thinkingTotalMs;
    private int _messageId;

    /// <summary>构造函数 — 自建队列（独立使用场景）；注入墙钟（测试注入确定性时钟）。</summary>
    public InProcessEventConsumer(Func<long>? wallClock = null)
        : this([], wallClock) {
    }

    /// <summary>构造函数 — 注入共享队列（EngineTurnClient 场景：生命周期事件与回合内部事件同队 FIFO 保序）。</summary>
    public InProcessEventConsumer(ConcurrentQueue<BridgeEvent> events, Func<long>? wallClock = null) {
        _events = events;
        _wallClock = wallClock ?? (static () => Environment.TickCount64);
    }

    /// <summary>重置回合内状态（新一轮对话前由宿主调用，防跨回合泄漏；队列不清 — 回合边界由生命周期事件标记）。
    /// 消息序号不重置（跨回合递增 — 会话级 MessageId）。</summary>
    public void Reset() {
        _fullResponse.Clear();
        _thinkingContent.Clear();
        _thinkingStartMs = null;
        _thinkingSignalled = false;
        _thinkingTotalMs = 0;
    }

    /// <summary>收到文本内容 → token_delta(length, text)（M8-1 逐字流式 — 携带增量文本供 StreamingTextItem 原地追加）；思考结束时结算 thinking_ended。</summary>
    public void OnText(string content) {
        EndThinkingIfActive();
        if (content.Length == 0)
            return;
        _fullResponse.Append(content);
        _events.Enqueue(new BridgeEvent.TokenDelta(content.Length, content));
    }

    /// <summary>收到思考内容 — 回合内首个思考发 thinking_started（回合内单次信号，Reset 后新回合再发）；
    /// 多段思考（think→text→think）每段重启计时（时长累计语义）；内容全量累积（TASK062 CoT）。</summary>
    public void OnThinking(string thinking) {
        if (thinking.Length > 0) {
            if (!_thinkingSignalled) {
                _thinkingSignalled = true;
                _events.Enqueue(new BridgeEvent.ThinkingStarted());
            }

            if (_thinkingStartMs is null) {
                _thinkingStartMs = _wallClock();
            }

            _thinkingContent.Append(thinking);
        }
    }

    /// <summary>工具调用开始 → tool_call(name, args)；思考结束时结算 thinking_ended。</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments) {
        EndThinkingIfActive();
        _events.Enqueue(new BridgeEvent.ToolCall(toolName, arguments ?? string.Empty));
    }

    /// <summary>工具调用结束 → tool_result(截断摘要 + error 标记 + 结构化补丁投影 + 原始输出预截断投影)。</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) {
        var (rawOutput, hiddenLines) = ProjectRawOutput(resultText);
        _events.Enqueue(new BridgeEvent.ToolResult(BuildSummary(resultText, isError), ProjectPatch(patch), rawOutput, hiddenLines));
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

    /// <summary>流式响应完成 → 思考结算 + assistant_message(完整文本 + Markdown 投影；无语法快路径降级纯文本)
    /// + token_usage(用量汇总 — usage null 不发；与 engine BridgeEventConsumer 行序镜像：assistant 前、收尾发)。</summary>
    public void OnDone(TokenUsage? usage, string? modelId) {
        EndThinkingIfActive();
        if (_fullResponse.Length > 0) {
            var fullText = _fullResponse.ToString();
            // TASK062 结构化组装 — MessageId 会话级递增（Ctrl+O 定位/transcript 对账）+
            // 思考总时长（多段累计）+ CoT 全文（Ctrl+O 展开渲染；空 → null 不输出字段）
            var thinking = _thinkingContent.ToString();
            _events.Enqueue(new BridgeEvent.AssistantMessage(
                fullText,
                MarkdigProjector.ProjectOptional(fullText),
                MessageId: ++_messageId,
                ThinkingDurationMs: _thinkingTotalMs,
                ThinkingContent: thinking.Length > 0 ? thinking : null));
        }

        EnqueueTokenUsageIfAvailable(usage);
    }

    /// <summary>子代理启动 → SubagentStarted（DSG041 批4 — 显式实现：DIM 默认实现经类类型调用不可见；
    /// ReplBridgeDriver 路由已就绪，分组渲染零改动验收面）。</summary>
    public void OnSubagentStarted(string subagentId, string agentType, string description) {
        _events.Enqueue(new BridgeEvent.SubagentStarted(subagentId, agentType, description));
    }

    /// <summary>子代理工具活动 → SubagentToolUse（原地更新组条目 + 活动日志环形缓冲）。</summary>
    public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) {
        _events.Enqueue(new BridgeEvent.SubagentToolUse(subagentId, toolName, summary, tokenGain));
    }

    /// <summary>子代理完成 → SubagentFinished（条目定格，isError 聚合组头 ● 红；tokenTotal 完成态权威校正）。</summary>
    public void OnSubagentFinished(string subagentId, bool isError, int tokenTotal) {
        _events.Enqueue(new BridgeEvent.SubagentFinished(subagentId, isError, tokenTotal));
    }

    /// <summary>上下文余量 → ContextUsage（M8-7 TASK064 P1 — 引擎回调三参直传内存事件；
    /// percentLeft 引擎侧算好，ReplBridgeDriver 路由 footer 告警。接口同步由引擎侧 IEventConsumer DIM 完成）。</summary>
    public void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) {
        _events.Enqueue(new BridgeEvent.ContextUsage(promptTokens, contextWindowTokens, percentLeft));
    }

    /// <summary>后台任务状态更新 → BackgroundTask（M8-8 — 四参直传内存事件；(kind,taskId) upsert 常驻面板条目，
    /// 终态驻留。接口同步由引擎侧 IEventConsumer DIM 完成 — 签名一致自动成为实现，未就位时孤立方法编译即过）。</summary>
    public void OnBackgroundTask(string kind, string taskId, string description, string state) {
        _events.Enqueue(new BridgeEvent.BackgroundTask(kind, taskId, description, state));
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

    /// <summary>回合用量汇总 → TokenUsage 事件（OnDone 收尾发射；usage null 不发 — 无数据不噪音；
    /// ContextTokens/ContextLimitTokens 引擎暂无 context window 数据源，null 通道留待接线）。</summary>
    private void EnqueueTokenUsageIfAvailable(TokenUsage? usage) {
        if (usage is null) {
            return;
        }

        WriteEvent(new BridgeEvent.TokenUsage(usage.PromptTokens, usage.CompletionTokens));
    }

    /// <summary>思考结束结算（进入产出或回合结束时调用，幂等）— 段时长累计入回合思考总时长（TASK062）。</summary>
    private void EndThinkingIfActive() {
        if (_thinkingStartMs is not long start)
            return;
        _thinkingStartMs = null;
        var duration = Math.Max(0, _wallClock() - start);
        _thinkingTotalMs += (int)Math.Min(int.MaxValue, duration);
        WriteEvent(new BridgeEvent.ThinkingEnded(duration));
    }

    /// <summary>工具结果摘要：error 前缀 + 截断。</summary>
    private static string BuildSummary(string? resultText, bool isError) {
        var text = resultText ?? string.Empty;
        var truncated = text.Length > ToolResultSummaryLimit
            ? string.Concat(text.AsSpan(0, ToolResultSummaryLimit), "...")
            : text;
        return isError ? $"[error] {truncated}" : truncated;
    }

    /// <summary>
    /// 工具结果原始输出投影（raw_output + hidden_lines）— 多行折叠语义，对齐基准 terminal.ts
    /// renderTruncatedContent：单行结果不投影（summary 已完整表达，旧事件形态零变化）；
    /// 多行超预览上限（3+1 行）折叠，hiddenLines = 估算总行数 − 预览上限（预截断时对齐基准
    /// estimatedRemaining 的 ceil(总长/折行宽) 估算）；超长预截断（960 字符上限）防 64MB
    /// 原始输出撑爆内存事件链；折叠外仅多 1 行时全显（基准特例 — 4 行直显比折叠提示省一行）。
    /// 与 engine BridgeEventConsumer.ProjectRawOutput 逐行为镜像（Core 零依赖，双端各自维护）。
    /// </summary>
    private static (string? RawOutput, int HiddenLines) ProjectRawOutput(string? resultText) {
        var text = resultText?.TrimEnd();
        if (string.IsNullOrEmpty(text) || text.AsSpan().IndexOf('\n') < 0) {
            return (null, 0);
        }

        var preTruncated = text.Length > ToolResultRawOutputLimit;
        var content = preTruncated ? text[..ToolResultRawOutputLimit] : text;
        var contentLines = 1 + content.AsSpan().Count('\n');
        var estimatedTotalLines = preTruncated
            ? Math.Max(contentLines, (text.Length + ToolResultAssumedWrapWidth - 1) / ToolResultAssumedWrapWidth)
            : contentLines;
        var hiddenLines = estimatedTotalLines - ToolResultPreviewLines;
        return hiddenLines <= 1
            ? (content, 0)
            : (content, hiddenLines);
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
