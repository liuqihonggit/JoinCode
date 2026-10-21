namespace JoinCode.Adapters;

/// <summary>
/// bridge 事件消费器 — IEventConsumer 的 TUI bridge 实现（第三实现：CLI DX/CliEventConsumer、GUI、bridge）：
/// 引擎回调实时映射为 ADR 0141 事件行写 stdout（newline-delimited JSON）。
/// OnText → token_delta + OnDone 时 assistant_message(完整文本) + token_usage(用量汇总)；
/// OnThinking 首次 → thinking_started、进入产出（OnText/OnToolStart）→ thinking_ended(墙钟差)；
/// OnToolStart/OnToolEnd → tool_call/tool_result（结果带原始输出预截断投影）。
/// 回合生命周期事件（turn_started/completed/interrupted）由 BridgeServeLoop 负责，本类只管回合内部事件。
/// </summary>
public sealed class BridgeEventConsumer : IResettableEventConsumer {
    /// <summary>工具结果摘要截断上限（对齐 CliEventConsumer 的 200 字符显示截断）。</summary>
    private const int ToolResultSummaryLimit = 200;

    /// <summary>工具结果原始输出预览行数上限（对齐基准 terminal.ts MAX_LINES_TO_SHOW=3 — 超出折叠）。</summary>
    private const int ToolResultPreviewLines = 3;

    /// <summary>工具结果原始输出假定折行宽度（协议无终端宽度回传 — 保守取 80 列，对齐基准 wrapWidth 语义）。</summary>
    private const int ToolResultAssumedWrapWidth = 80;

    /// <summary>工具结果原始输出预截断字符上限（对齐基准 maxChars = MAX_LINES × wrapWidth × 4 —
    /// 防 64MB 原始输出（如二进制 dump）撑爆 JSON 事件行）。</summary>
    private const int ToolResultRawOutputLimit = ToolResultPreviewLines * ToolResultAssumedWrapWidth * 4;

    private readonly TextWriter _stdout;
    private readonly Func<long> _wallClock;
    private readonly StringBuilder _fullResponse = new();
    private long? _thinkingStartMs;
    private bool _thinkingSignalled;

    /// <summary>构造函数 — 注入事件输出口与墙钟（测试注入确定性时钟）。</summary>
    public BridgeEventConsumer(TextWriter stdout, Func<long>? wallClock = null) {
        _stdout = stdout;
        _wallClock = wallClock ?? (static () => Environment.TickCount64);
    }

    /// <summary>重置状态（新一轮对话前由 BridgeServeLoop 调用，防跨回合泄漏）。</summary>
    public void Reset() {
        _fullResponse.Clear();
        _thinkingStartMs = null;
        _thinkingSignalled = false;
    }

    /// <summary>收到文本内容 → token_delta(length, text)（M8-1 逐字流式 — 携带增量文本，TUI StreamingTextItem 消费）；思考结束时结算 thinking_ended。</summary>
    public void OnText(string content) {
        EndThinkingIfActive();
        if (content.Length == 0)
            return;
        _fullResponse.Append(content);
        WriteEvent(new Cli.Bridge.BridgeEvent.TokenDelta(content.Length, content));
    }

    /// <summary>收到思考内容 — 回合内首个思考发 thinking_started 并记录墙钟（回合内单次信号，Reset 后新回合再发）。</summary>
    public void OnThinking(string thinking) {
        if (thinking.Length == 0 || _thinkingSignalled)
            return;
        _thinkingSignalled = true;
        _thinkingStartMs = _wallClock();
        WriteEvent(new Cli.Bridge.BridgeEvent.ThinkingStarted());
    }

    /// <summary>工具调用开始 → tool_call(name, args)；思考结束时结算 thinking_ended。</summary>
    public void OnToolStart(string toolName, string? toolCallId, string? arguments) {
        EndThinkingIfActive();
        WriteEvent(new Cli.Bridge.BridgeEvent.ToolCall(toolName, arguments ?? string.Empty));
    }

    /// <summary>工具调用结束 → tool_result(截断摘要 + error 标记 + 结构化补丁投影 + 原始输出预截断投影)。</summary>
    public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) {
        var (rawOutput, hiddenLines) = ProjectRawOutput(resultText);
        WriteEvent(new Cli.Bridge.BridgeEvent.ToolResult(BuildSummary(resultText, isError), ProjectPatch(patch), rawOutput, hiddenLines));
    }

    /// <summary>工具调用进度 — TUI 不消费，忽略。</summary>
    public void OnToolProgress(string toolName, string progressType, string? progressMessage) {
    }

    /// <summary>检测到循环输出 — TUI 不消费，忽略。</summary>
    public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) {
    }

    /// <summary>计时摘要 — TUI 不消费，忽略。</summary>
    public void OnTimingSummary(string summary) {
    }

    /// <summary>流式响应完成 → 思考结算 + assistant_message(完整文本) + token_usage(用量汇总 — usage null 不发)。</summary>
    public void OnDone(TokenUsage? usage, string? modelId) {
        EndThinkingIfActive();
        if (_fullResponse.Length > 0)
            WriteEvent(new Cli.Bridge.BridgeEvent.AssistantMessage(_fullResponse.ToString()));
        WriteTokenUsageIfAvailable(usage);
    }

    /// <summary>子代理启动 → subagent_started（DSG041 批3 — 显式实现：DIM 默认实现经类类型调用不可见）。</summary>
    public void OnSubagentStarted(string subagentId, string agentType, string description) {
        WriteEvent(new Cli.Bridge.BridgeEvent.SubagentStarted(subagentId, agentType, description));
    }

    /// <summary>子代理工具活动 → subagent_tool_use（原地更新组条目不逐条刷屏）。</summary>
    public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) {
        WriteEvent(new Cli.Bridge.BridgeEvent.SubagentToolUse(subagentId, toolName, summary, tokenGain));
    }

    /// <summary>子代理完成 → subagent_finished（条目定格，isError 聚合组头；tokenTotal 完成态权威校正）。</summary>
    public void OnSubagentFinished(string subagentId, bool isError, int tokenTotal) {
        WriteEvent(new Cli.Bridge.BridgeEvent.SubagentFinished(subagentId, isError, tokenTotal));
    }

    /// <summary>上下文余量 → context_usage（M8-7 — 每回合 Done 后 footer 告警；显式实现：DIM 默认实现经类类型调用不可见）。</summary>
    public void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) {
        WriteEvent(new Cli.Bridge.BridgeEvent.ContextUsage(promptTokens, contextWindowTokens, percentLeft));
    }

    /// <summary>后台任务状态更新 → background_task（M8-8 — 事件泵直推；显式实现：DIM 默认实现经类类型调用不可见，
    /// 缺显式实现时泵经 IEventConsumer 调用会静默落入接口空实现 — DSG041 批3 先例）。</summary>
    public void OnBackgroundTask(string kind, string taskId, string description, string state) {
        WriteEvent(new Cli.Bridge.BridgeEvent.BackgroundTask(kind, taskId, description, state));

    /// <summary>回合用量汇总 → token_usage 事件（OnDone 收尾发射；usage null 不发 — 无数据不噪音；
    /// context 余量走 context_usage 独立事件（OnContextUsage — 上游 M8-7 权威数据源））。</summary>
    private void WriteTokenUsageIfAvailable(TokenUsage? usage) {
        if (usage is null) {
            return;
        }

        WriteEvent(new Cli.Bridge.BridgeEvent.TokenUsage(usage.PromptTokens, usage.CompletionTokens));    }

    /// <summary>回合用量汇总 → token_usage 事件（OnDone 收尾发射；usage null 不发 — 无数据不噪音；
    /// context 余量走 context_usage 独立事件（OnContextUsage — 上游 M8-7 权威数据源））。</summary>
    private void WriteTokenUsageIfAvailable(TokenUsage? usage) {
        if (usage is null) {
            return;
        }

        WriteEvent(new Cli.Bridge.BridgeEvent.TokenUsage(usage.PromptTokens, usage.CompletionTokens));
    }

    /// <summary>思考结束结算（进入产出或回合结束时调用，幂等）。</summary>
    private void EndThinkingIfActive() {
        if (_thinkingStartMs is not long start)
            return;
        _thinkingStartMs = null;
        WriteEvent(new Cli.Bridge.BridgeEvent.ThinkingEnded(Math.Max(0, _wallClock() - start)));
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
    /// renderTruncatedContent：单行结果不投影（summary 已完整表达，旧 JSON golden 零破坏）；
    /// 多行超预览上限（3+1 行）折叠，hiddenLines = 估算总行数 − 预览上限（预截断时对齐基准
    /// estimatedRemaining 的 ceil(总长/折行宽) 估算）；超长预截断（960 字符上限）防 64MB
    /// 原始输出撑爆 JSON 事件行；折叠外仅多 1 行时全显（基准特例 — 4 行直显比折叠提示省一行）。
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

    /// <summary>引擎 StructuredPatchHunk[] → BridgeDiffHunk[] 协议投影（渲染子集 header+lines[kind,content]；
    /// 空/null → null，WhenWritingNull 不输出字段 — 与 native_tui InProcessEventConsumer.ProjectPatch 语义一致）。</summary>
    private static Cli.Bridge.BridgeEvent.BridgeDiffHunk[]? ProjectPatch(StructuredPatchHunk[]? patch) =>
        patch is null || patch.Length == 0 ? null
            : patch.Select(static hunk => new Cli.Bridge.BridgeEvent.BridgeDiffHunk {
                Header = hunk.Header,
                Lines = hunk.Lines.Select(static line => new Cli.Bridge.BridgeEvent.BridgeDiffLine {
                    Kind = (Cli.Bridge.BridgeEvent.BridgeDiffLineKind)(byte)line.Type,
                    Content = line.Content,
                }).ToArray(),
            }).ToArray();

    /// <summary>写一条事件行（单次 Write 保证行原子性 + LF + Flush，协议统一 \n；外层配 TextWriter.Synchronized）。</summary>
    private void WriteEvent(Cli.Bridge.BridgeEvent bridgeEvent) {
        _stdout.Write(Cli.Bridge.BridgeProtocol.SerializeEvent(bridgeEvent) + "\n");
        _stdout.Flush();
    }
}
