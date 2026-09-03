
namespace Core.Scheduling.Tasks;

public interface IMonitorMcpTaskExecutor
{
    Task<string> StartMonitoringAsync(McpMonitorConfig config, CancellationToken ct = default);
    Task StopMonitoringAsync(string monitorId, CancellationToken ct = default);
    Task<IReadOnlyList<McpMonitorStatus>> GetActiveMonitorsAsync(CancellationToken ct = default);
    event EventHandler<McpMonitorEventArgs>? MonitorEvent;
}

public sealed partial class McpMonitorConfig
{
    public required string ServerName { get; init; }
    public List<string> EventFilters { get; init; } = [];
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxEvents { get; init; } = 100;
    public bool AutoReconnect { get; init; } = true;

    private FrozenSet<string> _eventFilterSet = FrozenSet<string>.Empty;
    private bool _eventFilterSetInitialized;
    public FrozenSet<string> EventFilterSet
    {
        get
        {
            if (!_eventFilterSetInitialized)
            {
                _eventFilterSet = EventFilters.ToFrozenSet();
                _eventFilterSetInitialized = true;
            }
            return _eventFilterSet;
        }
    }
}

public sealed partial class McpMonitorStatus
{
    public required string MonitorId { get; init; }
    public required string ServerName { get; init; }
    public required MonitorState State { get; init; }
    public DateTime StartedAt { get; init; }
    public int EventsReceived { get; init; }
    public DateTime? LastEventAt { get; init; }
}

public enum MonitorState { [EnumValue("starting")] Starting = 0, [EnumValue("running")] Running = 1, [EnumValue("stopped")] Stopped = 3, [EnumValue("error")] Error = 4 }

/// <summary>
/// 监控会话事件 — 触发状态转换的事件（ADR 0040 事件枚举）
/// </summary>
internal enum MonitorSessionEvent
{
    /// <summary>启动成功 — Starting → Running</summary>
    Started,
    /// <summary>出错 — Starting/Running → Error</summary>
    Fail,
    /// <summary>重连恢复 — Error → Running</summary>
    Recover,
    /// <summary>停止 — Running/Starting/Error → Stopped</summary>
    Stop,
}

public sealed partial class McpMonitorEventArgs : EventArgs
{
    public required string MonitorId { get; init; }
    public required string ServerName { get; init; }
    public required string EventType { get; init; }
    public required Dictionary<string, JsonElement> Data { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[Register(typeof(IMonitorMcpTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class MonitorMcpTaskExecutor : IMonitorMcpTaskExecutor, IAsyncDisposable
{
    private readonly IMcpToolRegistry _mcpToolRegistry;
    private readonly ILogger<MonitorMcpTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly AsyncLock _sessionLock = new();
    private int _monitorIdCounter;
    private int _disposed;

    public event EventHandler<McpMonitorEventArgs>? MonitorEvent;

    public MonitorMcpTaskExecutor(IMcpToolRegistry mcpToolRegistry, ILogger<MonitorMcpTaskExecutor>? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _mcpToolRegistry = mcpToolRegistry;
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<string> StartMonitoringAsync(McpMonitorConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var monitorId = $"monitor-{Interlocked.Increment(ref _monitorIdCounter):D4}";
        var session = new MonitorSession(monitorId, config);

        using var guard = _sessionLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        _sessions[monitorId] = session;
    

        _ = Task.Run(() => RunMonitorLoopAsync(session, ct));

        RecordMonitorMetrics("start", config.ServerName, true);
        return monitorId;
    }

    public async Task StopMonitoringAsync(string monitorId, CancellationToken ct = default)
    {
        using var guard = _sessionLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        if (_sessions.TryRemove(monitorId, out var session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            RecordMonitorMetrics("stop", session.Config.ServerName, true);
        }
    
    }

    public async Task<IReadOnlyList<McpMonitorStatus>> GetActiveMonitorsAsync(CancellationToken ct = default)
    {
        using var guard = _sessionLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        return _sessions.Values.Select(s => s.ToStatus()).ToList();
    
    }

    public async ValueTask DisposeAsync()
    {
        // 防止重复释放 — ServiceProvider 清理时可能多次调用 DisposeAsync
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await CleanupSessionsAsync().ConfigureAwait(false);
        _sessionLock.Dispose();
    }

    /// <summary>清理所有监控会话（在锁保护下执行）</summary>
    private async Task CleanupSessionsAsync()
    {
        using var guard = _sessionLock.TryLock() ?? throw new System.TimeoutException("锁等待超时");
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
    }

    private async Task RunMonitorLoopAsync(MonitorSession session, CancellationToken externalCt)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, session.Cts.Token);

        try
        {
            var client = await ResolveMcpClientAsync(session.Config.ServerName).ConfigureAwait(false);

            if (client is null)
            {
                session.Trigger(MonitorSessionEvent.Fail);
                _logger?.LogError("Failed to resolve MCP client for server {ServerName}", session.Config.ServerName);
                return;
            }

            session.Trigger(MonitorSessionEvent.Started);

            while (!linkedCts.Token.IsCancellationRequested)
            {
                try
                {
                    await PollMcpServerAsync(session, client, linkedCts.Token).ConfigureAwait(false);
                    await Task.Delay(session.Config.PollInterval, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (session.Config.AutoReconnect)
                {
                    session.Trigger(MonitorSessionEvent.Fail);
                    _logger?.LogWarning(ex, "Monitor {MonitorId} encountered error, attempting reconnect", session.MonitorId);

                    var reconnected = await TryReconnectAsync(session, linkedCts.Token).ConfigureAwait(false);
                    if (!reconnected) break;

                    session.Trigger(MonitorSessionEvent.Recover);
                }
                catch (Exception ex)
                {
                    session.Trigger(MonitorSessionEvent.Fail);
                    _logger?.LogError(ex, "Monitor {MonitorId} failed", session.MonitorId);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            session.Trigger(MonitorSessionEvent.Fail);
            _logger?.LogError(ex, "Monitor {MonitorId} loop crashed", session.MonitorId);
        }
        finally
        {
            if (session.State != MonitorState.Error)
            {
                session.Trigger(MonitorSessionEvent.Stop);
            }
        }
    }

    private async Task<IMcpClient?> ResolveMcpClientAsync(string serverName)
    {
        try
        {
            var clients = await _mcpToolRegistry.GetAllRemoteClientsAsync().ConfigureAwait(false);
            return clients.TryGetValue(serverName, out var client) ? client : null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve MCP client for {ServerName}", serverName);
            return null;
        }
    }

    private async Task PollMcpServerAsync(MonitorSession session, IMcpClient client, CancellationToken ct)
    {
        if (!client.IsConnected)
        {
            if (session.Config.AutoReconnect)
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
            }
            else
            {
                return;
            }
        }

        var toolsResult = await client.ListToolsAsync(ct).ConfigureAwait(false);

        if (toolsResult.Success && toolsResult.GetData().Count > 0)
        {
            OnMonitorEvent(session, "tools_update", new Dictionary<string, JsonElement>
            {
                ["toolCount"] = JsonElementHelper.FromInt32(toolsResult.GetData().Count),
                ["tools"] = JsonElementHelper.FromObject(toolsResult.GetData().Select(t => t.Name).ToList(), SchedulingJsonContext.Default.ListString)
            });
        }

        var resourcesResult = await client.ListResourcesAsync(ct).ConfigureAwait(false);

        if (resourcesResult.Success && resourcesResult.GetData().Count > 0)
        {
            OnMonitorEvent(session, "resources_update", new Dictionary<string, JsonElement>
            {
                ["resourceCount"] = JsonElementHelper.FromInt32(resourcesResult.GetData().Count)
            });
        }
    }

    private async Task<bool> TryReconnectAsync(MonitorSession session, CancellationToken ct)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), ct).ConfigureAwait(false);
                var client = await ResolveMcpClientAsync(session.Config.ServerName).ConfigureAwait(false);
                if (client is not null)
                {
                    await client.ConnectAsync(ct).ConfigureAwait(false);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MCP 客户端连接失败: {Server}", session.Config.ServerName);
            }
        }

        return false;
    }

    private void OnMonitorEvent(MonitorSession session, string eventType, Dictionary<string, JsonElement> data)
    {
        if (session.Config.EventFilterSet.Count > 0 && !session.Config.EventFilterSet.Contains(eventType))
        {
            return;
        }

        if (session.EventsReceived >= session.Config.MaxEvents)
        {
            return;
        }

        Interlocked.Increment(ref session.EventsReceivedField);
        session.LastEventAt = _clock.GetUtcNow();

        var args = new McpMonitorEventArgs
        {
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
internal sealed partial class MonitorSession : IAsyncDisposable
{
    private readonly Fsm<MonitorState, MonitorSessionEvent> _fsm;

    public string MonitorId { get; }
    public McpMonitorConfig Config { get; }
    public MonitorState State => _fsm.CurrentState;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public int EventsReceivedField;
    public int EventsReceived => Volatile.Read(ref EventsReceivedField);
    public DateTime? LastEventAt { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public MonitorSession(string monitorId, McpMonitorConfig config)
    {
        MonitorId = monitorId;
        Config = config;
        _fsm = new Fsm<MonitorState, MonitorSessionEvent>(_fsmSortedKeys, _fsmRules, MonitorState.Starting);
        _fsm.StateChanged += (_, e) => FsmDispatchEvent(e);
    }

    /// <summary>触发事件 — 查转换表合法则转,非法静默忽略(保持原直接赋值语义)</summary>
    public void Trigger(MonitorSessionEvent evt) => _fsm.TryTrigger(evt);

    public McpMonitorStatus ToStatus()
    {
        return new McpMonitorStatus
        {
            MonitorId = MonitorId,
            ServerName = Config.ServerName,
            State = State,
            StartedAt = StartedAt,
            EventsReceived = EventsReceived,
            LastEventAt = LastEventAt
        };
    }

    public async ValueTask DisposeAsync()
    {
        Cts.Cancel();
        Cts.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
