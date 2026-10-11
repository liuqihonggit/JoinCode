namespace Core.Context;

/// <summary>
/// 撤回服务实现 — 操作 ISessionStore + IPromptStore
/// <para>无独立数据源，委托 SessionStore 执行撤回操作</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal sealed class RewindService : IRewindService {
    private readonly ISessionStore _sessionStore;
    private readonly IPromptStore _promptStore;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger _logger;

    /// <summary>构造撤回服务</summary>
    public RewindService(
        ISessionStore sessionStore,
        IPromptStore promptStore,
        ITelemetryService? telemetryService,
        ILogger logger) {
        _sessionStore = sessionStore;
        _promptStore = promptStore;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public RewindResult RewindLastTurn() {
        var removed = _sessionStore.TrimLastTurn();
        _logger.LogInformation("撤回最后一轮对话 (SP-3)，移除 {Count} 条消息，剩余 {Remaining} 条",
            removed, _sessionStore.Count);

        _telemetryService?.RecordCount("context.rewind.count", new() { ["kind"] = "last_turn" }, "count", "Context rewind count");

        return RewindResult.Ok(RewindKind.TrimLastTurn, removed, _sessionStore.Count);
    }

    /// <inheritdoc />
    public RewindResult RewindToMessageIndex(int messageIndex) {
        if (messageIndex < 0 || messageIndex > _sessionStore.Count) {
            return RewindResult.Fail(
                $"消息索引 {messageIndex} 超出范围 [0, {_sessionStore.Count}]");
        }

        var removed = _sessionStore.TruncateTo(messageIndex);
        _logger.LogInformation("撤回到消息索引 {Index} (SP-5)，移除 {Count} 条消息，剩余 {Remaining} 条",
            messageIndex, removed, _sessionStore.Count);

        return RewindResult.Ok(RewindKind.TruncateToIndex, removed, _sessionStore.Count);
    }

    /// <inheritdoc />
    public RewindResult RewindToStart() {
        var removed = _sessionStore.Count;
        _sessionStore.CompactInPlace([]);
        _promptStore.ResetCache();

        _logger.LogInformation("撤回到会话初始状态 (SP-0)，移除 {Count} 条消息，前缀保留", removed);

        return RewindResult.Ok(RewindKind.ClearHistory, removed, 0);
    }
}
