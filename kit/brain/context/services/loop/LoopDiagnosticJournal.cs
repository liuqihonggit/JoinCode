namespace Core.Context;

/// <summary>
/// 日志簿命令 — Channel 中的消息类型
/// </summary>
public interface IJournalCommand;

/// <summary>
/// 记入一条日志簿条目的命令
/// </summary>
public sealed record JournalRecordCommand(JournalEntry Entry) : IJournalCommand;

/// <summary>
/// 计入一条循环异常记录的命令
/// </summary>
public sealed record JournalAnomalyCommand(LoopAnomalyRecord Anomaly) : IJournalCommand;

/// <summary>
/// 重置日志簿状态的命令
/// </summary>
public sealed record JournalResetCommand() : IJournalCommand;

/// <summary>
/// 循环诊断日志簿 — 信息熵检测器的日志伙伴
/// 职责：
///   1. 每条日志分配 traceId，维护当前会话的 traceId 滑动窗口
///   2. Guardian 触发时，收集窗口内所有 traceId 形成追踪链
///   3. 写一条 loop_anomaly 诊断日志，包含：触发层、对话轮次、工具调用次数、追踪链、熵值等
/// 医生模式读取 loop_anomaly 日志，用追踪链回溯完整上下文来优化代码
///
/// Actor 化：继承 ActorBase&lt;IJournalCommand, Unit&gt;，Consumer 线程独占滑动窗口，消除 AsyncLock。
/// </summary>
public sealed class LoopDiagnosticJournal : ActorBase<IJournalCommand, Unit>
{
    private readonly int _traceWindowCapacity;
    private readonly LinkedList<JournalEntry> _traceWindow = [];
    private readonly ILogger? _logger;
    private int _windowCount;

    /// <summary>
    /// 初始化循环诊断日志簿，指定追踪窗口容量和可选日志记录器
    /// </summary>
    /// <param name="traceWindowCapacity">追踪窗口最大容量（最少 5）</param>
    /// <param name="logger">可选日志记录器</param>
    public LoopDiagnosticJournal(int traceWindowCapacity = 50, ILogger? logger = null)
        : base(new ActorBackpressure(Capacity: 256, FullMode: BoundedChannelFullMode.DropOldest))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(traceWindowCapacity, 5);
        _traceWindowCapacity = traceWindowCapacity;
        _logger = logger;
    }

    /// <summary>
    /// 记录一条正常日志，分配 traceId，异步加入滑动窗口
    /// 前台调用：只入队，不阻塞
    /// </summary>
    public JournalEntry Record(string eventType, string sessionId, int conversationTurn, int toolCallCount, Dictionary<string, string>? data = null)
    {
        var entry = new JournalEntry
        {
            TraceId = Guid.NewGuid().ToString("N")[..12],
            EventType = eventType,
            SessionId = sessionId,
            Timestamp = DateTimeOffset.UtcNow,
            ConversationTurn = conversationTurn,
            ToolCallCount = toolCallCount,
            Data = data ?? new Dictionary<string, string>()
        };

        TrySend(new JournalRecordCommand(entry));
        return entry;
    }

    /// <summary>
    /// Guardian 触发时调用 — 收集追踪链，生成 loop_anomaly 诊断日志
    /// 前台调用：只入队，不阻塞。返回的 anomaly 中 TraceChain 可能为空（后台尚未处理完窗口）
    /// 但 traceId 已立即分配，后续后台处理会补全追踪链
    /// </summary>
    public LoopAnomalyRecord OnLoopDetected(
        string detectorLayer,
        string sessionId,
        int conversationTurn,
        int toolCallCount,
        int triggerCount,
        string reason,
        double? entropy = null,
        string? textSnippet = null)
    {
        var anomalyTraceId = Guid.NewGuid().ToString("N")[..12];

        var anomaly = new LoopAnomalyRecord
        {
            TraceId = anomalyTraceId,
            DetectorLayer = detectorLayer,
            SessionId = sessionId,
            ConversationTurn = conversationTurn,
            ToolCallCount = toolCallCount,
            TriggerCount = triggerCount,
            Reason = reason,
            Entropy = entropy,
            TextSnippet = textSnippet,
            TraceChain = [],
            Timestamp = DateTimeOffset.UtcNow
        };

        TrySend(new JournalAnomalyCommand(anomaly));

        return anomaly;
    }

    /// <summary>
    /// 重置日志簿状态
    /// </summary>
    public void Reset()
    {
        TrySend(new JournalResetCommand());
    }

    /// <summary>
    /// 当前窗口内追踪条目数（近似值，后台线程更新）
    /// </summary>
    public int WindowCount => Volatile.Read(ref _windowCount);

    /// <summary>
    /// 处理日志簿命令 — Consumer 线程独占，按命令类型分派到对应处理逻辑
    /// </summary>
    /// <param name="command">日志簿命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(IJournalCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case JournalRecordCommand(var entry):
                AddToWindowCore(entry);
                break;

            case JournalAnomalyCommand(var anomaly):
                await ProcessAnomalyAsync(anomaly).ConfigureAwait(false);
                break;

            case JournalResetCommand:
                _traceWindow.Clear();
                Volatile.Write(ref _windowCount, 0);
                break;
        }
    }

    /// <summary>
    /// Consumer 线程发生异常时的回调 — 记录警告日志
    /// </summary>
    /// <param name="ex">异常对象</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "[LoopDiagnosticJournal] 后台消费者异常");
    }

    private async Task ProcessAnomalyAsync(LoopAnomalyRecord anomaly)
    {
        var traceChain = _traceWindow.Select(e => e.TraceId).ToList();

        var fullAnomaly = anomaly with { TraceChain = traceChain };

        _logger?.LogWarning(
            "[LoopDiagnosticJournal] loop_anomaly: 层={Layer}, 轮次={Turn}, 工具调用={ToolCalls}, 触发次数={Trigger}, 熵={Entropy}, 追踪链={ChainCount}条, traceId={TraceId}",
            fullAnomaly.DetectorLayer, fullAnomaly.ConversationTurn, fullAnomaly.ToolCallCount,
            fullAnomaly.TriggerCount, fullAnomaly.Entropy?.ToString("F3") ?? "N/A",
            traceChain.Count, fullAnomaly.TraceId);

        var anomalyEntry = new JournalEntry
        {
            TraceId = fullAnomaly.TraceId,
            EventType = "loop_anomaly",
            SessionId = fullAnomaly.SessionId,
            Timestamp = DateTimeOffset.UtcNow,
            ConversationTurn = fullAnomaly.ConversationTurn,
            ToolCallCount = fullAnomaly.ToolCallCount,
            Data = fullAnomaly.ToDiagnosticData()
        };

        AddToWindowCore(anomalyEntry);
    }

    private void AddToWindowCore(JournalEntry entry)
    {
        _traceWindow.AddLast(entry);
        while (_traceWindow.Count > _traceWindowCapacity)
        {
            _traceWindow.RemoveFirst();
        }
        Volatile.Write(ref _windowCount, _traceWindow.Count);
    }
}

/// <summary>
/// 日志簿条目 — 滑动窗口中的一条日志记录
/// </summary>
public sealed record JournalEntry
{
    /// <summary>追踪标识，用于关联同一逻辑链路上的日志条目</summary>
    public required string TraceId { get; init; }
    /// <summary>事件类型，如 loop_anomaly、guardian_detect 等</summary>
    public required string EventType { get; init; }
    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }
    /// <summary>时间戳</summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>对话轮次</summary>
    public required int ConversationTurn { get; init; }
    /// <summary>工具调用次数</summary>
    public required int ToolCallCount { get; init; }
    /// <summary>附加诊断数据键值对</summary>
    public required Dictionary<string, string> Data { get; init; }
}

/// <summary>
/// 循环异常记录 — Guardian 触发时生成的诊断记录
/// 包含完整上下文信息，供医生模式回溯分析
/// </summary>
public sealed record LoopAnomalyRecord
{
    /// <summary>异常追踪标识</summary>
    public required string TraceId { get; init; }
    /// <summary>触发检测器层名（OutputLoop、LogicFingerprint、ToolCallSequence、ShannonEntropy）</summary>
    public required string DetectorLayer { get; init; }
    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }
    /// <summary>对话轮次</summary>
    public required int ConversationTurn { get; init; }
    /// <summary>工具调用次数</summary>
    public required int ToolCallCount { get; init; }
    /// <summary>累计触发次数</summary>
    public required int TriggerCount { get; init; }
    /// <summary>触发原因描述</summary>
    public required string Reason { get; init; }
    /// <summary>触发时的 Shannon 熵值（若由 Shannon 检测器触发）</summary>
    public required double? Entropy { get; init; }
    /// <summary>触发时的文本片段（截断到 200 字符以内）</summary>
    public required string? TextSnippet { get; init; }
    /// <summary>追踪链 — 触发前窗口内所有日志条目的 TraceId 列表</summary>
    public required IReadOnlyList<string> TraceChain { get; init; }
    /// <summary>时间戳</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// 转换为 DiagnosticLogEntry.Data 格式，供写入 JSONL
    /// </summary>
    public Dictionary<string, string> ToDiagnosticData()
    {
        var data = new Dictionary<string, string>
        {
            ["detector_layer"] = DetectorLayer,
            ["conversation_turn"] = ConversationTurn.ToString(),
            ["tool_call_count"] = ToolCallCount.ToString(),
            ["trigger_count"] = TriggerCount.ToString(),
            ["reason"] = Reason,
            ["trace_chain"] = string.Join(",", TraceChain),
        };

        if (Entropy.HasValue)
            data["entropy"] = Entropy.Value.ToString("F4");

        if (TextSnippet is not null)
            data["text_snippet"] = TextSnippet.Length > 200 ? TextSnippet[..200] + "..." : TextSnippet;

        return data;
    }
}
