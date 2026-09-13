namespace Core.Context;

/// <summary>
/// 日志簿命令 — Channel 中的消息类型
/// </summary>
public interface IJournalCommand;

public sealed record JournalRecordCommand(JournalEntry Entry) : IJournalCommand;
public sealed record JournalAnomalyCommand(LoopAnomalyRecord Anomaly) : IJournalCommand;
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
public sealed class LoopDiagnosticJournal : ActorBase<IJournalCommand, Unit>, IDisposable
{
    private readonly int _traceWindowCapacity;
    private readonly LinkedList<JournalEntry> _traceWindow = [];
    private readonly ILogger? _logger;
    private int _windowCount;

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
    /// 同步释放 — 保留 IDisposable 兼容现有 using 调用方。内部调 DisposeAsync 并等待 Consumer 退出。
    /// </summary>
    public void Dispose()
    {
        try
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LoopDiagnosticJournal] Dispose 超时");
        }
    }

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
    public required string TraceId { get; init; }
    public required string EventType { get; init; }
    public required string SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required int ConversationTurn { get; init; }
    public required int ToolCallCount { get; init; }
    public required Dictionary<string, string> Data { get; init; }
}

/// <summary>
/// 循环异常记录 — Guardian 触发时生成的诊断记录
/// 包含完整上下文信息，供医生模式回溯分析
/// </summary>
public sealed record LoopAnomalyRecord
{
    public required string TraceId { get; init; }
    public required string DetectorLayer { get; init; }
    public required string SessionId { get; init; }
    public required int ConversationTurn { get; init; }
    public required int ToolCallCount { get; init; }
    public required int TriggerCount { get; init; }
    public required string Reason { get; init; }
    public required double? Entropy { get; init; }
    public required string? TextSnippet { get; init; }
    public required IReadOnlyList<string> TraceChain { get; init; }
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
