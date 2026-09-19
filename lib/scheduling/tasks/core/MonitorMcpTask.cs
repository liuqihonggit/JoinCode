
namespace Core.Scheduling.Tasks;

/// <summary>
/// MCP 监控任务执行器接口 — 提供 MCP 服务器监控的启动、停止、查询与事件订阅能力。
/// </summary>
public interface IMonitorMcpTaskExecutor {
    /// <summary>
    /// 异步启动对指定 MCP 服务器的监控。
    /// </summary>
    /// <param name="config">监控配置,包含服务器名、轮询间隔、事件过滤等。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>监控会话唯一标识。</returns>
    Task<string> StartMonitoringAsync(McpMonitorConfig config, CancellationToken ct = default);

    /// <summary>
    /// 异步停止指定监控会话。
    /// </summary>
    /// <param name="monitorId">监控会话唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    Task StopMonitoringAsync(string monitorId, CancellationToken ct = default);

    /// <summary>
    /// 异步获取所有活跃监控会话的状态列表。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>活跃监控状态只读列表。</returns>
    Task<IReadOnlyList<McpMonitorStatus>> GetActiveMonitorsAsync(CancellationToken ct = default);

    /// <summary>
    /// 监控事件 — 当 MCP 服务器发生 tools_update/resources_update 等事件时触发。
    /// </summary>
    event EventHandler<McpMonitorEventArgs>? MonitorEvent;
}

/// <summary>
/// MCP 监控配置 — 描述监控的服务器名、轮询间隔、事件过滤与重连策略。
/// </summary>
public sealed partial class McpMonitorConfig {
    /// <summary>目标 MCP 服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>事件过滤器列表,为空表示接收所有事件。</summary>
    public List<string> EventFilters { get; init; } = [];
    /// <summary>轮询间隔,默认 5 秒。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>最大事件数,达到上限后停止上报,默认 100。</summary>
    public int MaxEvents { get; init; } = 100;
    /// <summary>是否自动重连,默认 true。</summary>
    public bool AutoReconnect { get; init; } = true;

    private FrozenSet<string> _eventFilterSet = FrozenSet<string>.Empty;
    private bool _eventFilterSetInitialized;
    /// <summary>
    /// 事件过滤器集合 — 延迟初始化的 FrozenSet,供 O(1) 查询。
    /// </summary>
    public FrozenSet<string> EventFilterSet {
        get {
            if (!_eventFilterSetInitialized) {
                _eventFilterSet = EventFilters.ToFrozenSet();
                _eventFilterSetInitialized = true;
            }
            return _eventFilterSet;
        }
    }
}

/// <summary>
/// MCP 监控状态 — 描述单个监控会话的当前状态与统计信息。
/// </summary>
public sealed partial class McpMonitorStatus {
    /// <summary>监控会话唯一标识。</summary>
    public required string MonitorId { get; init; }
    /// <summary>监控的 MCP 服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>监控会话当前状态。</summary>
    public required MonitorState State { get; init; }
    /// <summary>监控启动时间。</summary>
    public DateTime StartedAt { get; init; }
    /// <summary>已接收事件数。</summary>
    public int EventsReceived { get; init; }
    /// <summary>最近一次事件时间,可选。</summary>
    public DateTime? LastEventAt { get; init; }
}

/// <summary>
/// MCP 监控会话状态枚举。
/// </summary>
public enum MonitorState {
    /// <summary>启动中 — 尚未完成首次连接。</summary>
    [EnumValue("starting")] Starting = 0,
    /// <summary>运行中 — 正常轮询中。</summary>
    [EnumValue("running")] Running = 1,
    /// <summary>已停止 — 主动停止或异常退出。</summary>
    [EnumValue("stopped")] Stopped = 3,
    /// <summary>错误 — 连接失败或运行异常。</summary>
    [EnumValue("error")] Error = 4
}

/// <summary>
/// 监控会话事件 — 触发状态转换的事件（ADR 0040 事件枚举）
/// </summary>
internal enum MonitorSessionEvent {
    /// <summary>启动成功 — Starting → Running</summary>
    Started,
    /// <summary>出错 — Starting/Running → Error</summary>
    Fail,
    /// <summary>重连恢复 — Error → Running</summary>
    Recover,
    /// <summary>停止 — Running/Starting/Error → Stopped</summary>
    Stop,
}

/// <summary>
/// MCP 监控事件参数 — 当监控检测到 tools_update/resources_update 等事件时触发。
/// </summary>
public sealed partial class McpMonitorEventArgs : EventArgs {
    /// <summary>监控会话唯一标识。</summary>
    public required string MonitorId { get; init; }
    /// <summary>监控的 MCP 服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>事件类型,如 tools_update、resources_update。</summary>
    public required string EventType { get; init; }
    /// <summary>事件数据字典,键为数据名,值为 JSON 元素。</summary>
    public required Dictionary<string, JsonElement> Data { get; init; }
    /// <summary>事件时间戳,默认 UTC 当前时间。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// MCP 监控任务执行器 — 轮询 MCP 服务器的 tools/resources 变更,通过状态机管理会话生命周期(Starting→Running→Stopped/Error),
/// 支持自动重连、事件过滤与遥测上报。
/// </summary>
[Register(typeof(IMonitorMcpTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class MonitorMcpTaskExecutor : IMonitorMcpTaskExecutor, IAsyncDisposable {
    private readonly IMcpToolRegistry _mcpToolRegistry;
    private readonly ILogger<MonitorMcpTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly AsyncLock _sessionLock = new();
    private int _monitorIdCounter;
    private int _disposed;

    /// <summary>
    /// 监控事件 — 当 MCP 服务器发生 tools_update/resources_update 等事件时触发。
    /// </summary>
    public event EventHandler<McpMonitorEventArgs>? MonitorEvent;

    /// <summary>
    /// 构造 MCP 监控任务执行器。
    /// </summary>
    /// <param name="mcpToolRegistry">MCP 工具注册表,用于解析远程 MCP 客户端。</param>
    /// <param name="logger">日志记录器,可选。</param>
    /// <param name="telemetryService">遥测服务,可选,用于记录监控操作指标。</param>
    /// <param name="clock">时钟服务,可选,默认使用系统时钟。</param>
    public MonitorMcpTaskExecutor(IMcpToolRegistry mcpToolRegistry, ILogger<MonitorMcpTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null) {
        _mcpToolRegistry = mcpToolRegistry;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc/>
    public async Task<string> StartMonitoringAsync(McpMonitorConfig config, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(config);

        var monitorId = $"monitor-{Interlocked.Increment(ref _monitorIdCounter):D4}";
        var session = new MonitorSession(monitorId, config);

        using var guard = await _sessionLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_sessionLock.Name}' 等待超时");

        _sessions[monitorId] = session;


        _ = Task.Run(() => RunMonitorLoopAsync(session, ct));

        RecordMonitorMetrics("start", config.ServerName, true);
        return monitorId;
    }

    /// <inheritdoc/>
    public async Task StopMonitoringAsync(string monitorId, CancellationToken ct = default) {
        using var guard = await _sessionLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_sessionLock.Name}' 等待超时");

        if (_sessions.TryRemove(monitorId, out var session)) {
            await session.DisposeAsync().ConfigureAwait(false);
            RecordMonitorMetrics("stop", session.Config.ServerName, true);
        }

    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<McpMonitorStatus>> GetActiveMonitorsAsync(CancellationToken ct = default) {
        using var guard = await _sessionLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_sessionLock.Name}' 等待超时");

        return _sessions.Values.Select(s => s.ToStatus()).ToList();

    }

    /// <summary>
    /// 异步释放所有监控会话与锁资源。
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await CleanupSessionsAsync().ConfigureAwait(false);
        _sessionLock.Dispose();
    }

    /// <summary>清理所有监控会话（在锁保护下执行）</summary>
    private async Task CleanupSessionsAsync() {
        using var guard = await _sessionLock.TryLockAsync().ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_sessionLock.Name}' 等待超时");
        foreach (var session in _sessions.Values) {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
    }

    private async Task RunMonitorLoopAsync(MonitorSession session, CancellationToken externalCt) {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, session.Cts.Token);

        try {
            var client = await ResolveMcpClientAsync(session.Config.ServerName).ConfigureAwait(false);

            if (client is null) {
                session.Trigger(MonitorSessionEvent.Fail);
                _logger?.LogError("Failed to resolve MCP client for server {ServerName}", session.Config.ServerName);
                return;
            }

            session.Trigger(MonitorSessionEvent.Started);

            while (!linkedCts.Token.IsCancellationRequested) {
                try {
                    await PollMcpServerAsync(session, client, linkedCts.Token).ConfigureAwait(false);
                    await Task.Delay(session.Config.PollInterval, linkedCts.Token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) when (session.Config.AutoReconnect) {
                    session.Trigger(MonitorSessionEvent.Fail);
                    _logger?.LogWarning(ex, "Monitor {MonitorId} encountered error, attempting reconnect", session.MonitorId);

                    var reconnected = await TryReconnectAsync(session, linkedCts.Token).ConfigureAwait(false);
                    if (!reconnected) break;

                    session.Trigger(MonitorSessionEvent.Recover);
                } catch (Exception ex) {
                    session.Trigger(MonitorSessionEvent.Fail);
                    _logger?.LogError(ex, "Monitor {MonitorId} failed", session.MonitorId);
                    break;
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            session.Trigger(MonitorSessionEvent.Fail);
            _logger?.LogError(ex, "Monitor {MonitorId} loop crashed", session.MonitorId);
        } finally {
            if (session.State != MonitorState.Error) {
                session.Trigger(MonitorSessionEvent.Stop);
            }
        }
    }

    private async Task<IMcpClient?> ResolveMcpClientAsync(string serverName) {
        try {
            var clients = await _mcpToolRegistry.GetAllRemoteClientsAsync().ConfigureAwait(false);
            return clients.TryGetValue(serverName, out var client) ? client : null;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to resolve MCP client for {ServerName}", serverName);
            return null;
        }
    }

    private async Task PollMcpServerAsync(MonitorSession session, IMcpClient client, CancellationToken ct) {
        if (!client.IsConnected) {
            if (session.Config.AutoReconnect) {
                await client.ConnectAsync(ct).ConfigureAwait(false);
            } else {
                return;
            }
        }

        var toolsResult = await client.ListToolsAsync(ct).ConfigureAwait(false);

        if (toolsResult.Success && toolsResult.GetData().Count > 0) {
            OnMonitorEvent(session, "tools_update", new Dictionary<string, JsonElement> {
                ["toolCount"] = JsonElementHelper.FromInt32(toolsResult.GetData().Count),
                ["tools"] = JsonElementHelper.FromObject(toolsResult.GetData().Select(t => t.Name).ToList(), SchedulingJsonContext.Default.ListString)
            });
        }

        var resourcesResult = await client.ListResourcesAsync(ct).ConfigureAwait(false);

        if (resourcesResult.Success && resourcesResult.GetData().Count > 0) {
            OnMonitorEvent(session, "resources_update", new Dictionary<string, JsonElement> {
                ["resourceCount"] = JsonElementHelper.FromInt32(resourcesResult.GetData().Count)
            });
        }
    }

    private async Task<bool> TryReconnectAsync(MonitorSession session, CancellationToken ct) {
        for (var i = 0; i < 3; i++) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), ct).ConfigureAwait(false);
                var client = await ResolveMcpClientAsync(session.Config.ServerName).ConfigureAwait(false);
                if (client is not null) {
                    await client.ConnectAsync(ct).ConfigureAwait(false);
                    return true;
                }
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "MCP 客户端连接失败: {Server}", session.Config.ServerName);
            }
        }

        return false;
    }

    private void OnMonitorEvent(MonitorSession session, string eventType, Dictionary<string, JsonElement> data) {
        if (session.Config.EventFilterSet.Count > 0 && !session.Config.EventFilterSet.Contains(eventType)) {
            return;
        }

        if (session.EventsReceived >= session.Config.MaxEvents) {
            return;
        }

        Interlocked.Increment(ref session.EventsReceivedField);
        session.LastEventAt = _clock.GetUtcNow();

        var args = new McpMonitorEventArgs {
            MonitorId = session.MonitorId,
            ServerName = session.Config.ServerName,
            EventType = eventType,
            Data = data
        };

        MonitorEvent?.Invoke(this, args);
    }

    private void RecordMonitorMetrics(string operation, string serverName, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.monitor.count", new Dictionary<string, string> { ["operation"] = operation, ["server"] = serverName, ["success"] = isSuccess.ToString() }, "count", "MCP monitor operation count");
}

[FsmStateMachine(typeof(MonitorState), typeof(MonitorSessionEvent), MonitorState.Starting)]
[Transition(MonitorState.Starting, MonitorSessionEvent.Started, MonitorState.Running)]
[Transition(MonitorState.Starting, MonitorSessionEvent.Fail, MonitorState.Error)]
[Transition(MonitorState.Starting, MonitorSessionEvent.Stop, MonitorState.Stopped)]
[Transition(MonitorState.Running, MonitorSessionEvent.Fail, MonitorState.Error)]
[Transition(MonitorState.Running, MonitorSessionEvent.Stop, MonitorState.Stopped)]
[Transition(MonitorState.Error, MonitorSessionEvent.Recover, MonitorState.Running)]
[Transition(MonitorState.Error, MonitorSessionEvent.Stop, MonitorState.Stopped)]
internal sealed partial class MonitorSession : IAsyncDisposable {
    private readonly Fsm<MonitorState, MonitorSessionEvent> _fsm;
    private int _disposed;

    public string MonitorId { get; }
    public McpMonitorConfig Config { get; }
    public MonitorState State => _fsm.CurrentState;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public int EventsReceivedField;
    public int EventsReceived => Volatile.Read(ref EventsReceivedField);
    public DateTime? LastEventAt { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public MonitorSession(string monitorId, McpMonitorConfig config) {
        MonitorId = monitorId;
        Config = config;
        _fsm = new Fsm<MonitorState, MonitorSessionEvent>(_fsmSortedKeys, _fsmRules, MonitorState.Starting);
        _fsm.StateChanged += (_, e) => FsmDispatchEvent(e);
    }

    /// <summary>触发事件 — 查转换表合法则转,非法静默忽略(保持原直接赋值语义)</summary>
    public void Trigger(MonitorSessionEvent evt) => _fsm.TryTrigger(evt);

    public McpMonitorStatus ToStatus() {
        return new McpMonitorStatus {
            MonitorId = MonitorId,
            ServerName = Config.ServerName,
            State = State,
            StartedAt = StartedAt,
            EventsReceived = EventsReceived,
            LastEventAt = LastEventAt
        };
    }

    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        Cts.Cancel();
        Cts.Dispose();
        return ValueTask.CompletedTask;
    }
}