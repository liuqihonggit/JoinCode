
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
    public List<string>? EventFilters { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxEvents { get; init; } = 100;
    public bool AutoReconnect { get; init; } = true;
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

public enum MonitorState { [EnumValue("starting")] Starting, [EnumValue("running")] Running, [EnumValue("paused")] Paused, [EnumValue("stopped")] Stopped, [EnumValue("error")] Error }

public sealed partial class McpMonitorEventArgs : EventArgs
{
    public required string MonitorId { get; init; }
    public required string ServerName { get; init; }
    public required string EventType { get; init; }
    public required Dictionary<string, JsonElement> Data { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[Register(typeof(IMonitorMcpTaskExecutor))]
public sealed partial class MonitorMcpTaskExecutor : IMonitorMcpTaskExecutor, IAsyncDisposable
{
    private readonly IMcpToolRegistry _mcpToolRegistry;
    [Inject] private readonly ILogger<MonitorMcpTaskExecutor>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
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

        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _sessions[monitorId] = session;
        }
        finally
        {
            _sessionLock.Release();
        }

        session.State = MonitorState.Starting;

        _ = RunMonitorLoopAsync(session, ct);

        RecordMonitorMetrics("start", config.ServerName, true);
        return monitorId;
    }

    public async Task StopMonitoringAsync(string monitorId, CancellationToken ct = default)
    {
        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessions.TryRemove(monitorId, out var session))
            {
                await session.DisposeAsync().ConfigureAwait(false);
                RecordMonitorMetrics("stop", session.Config.ServerName, true);
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<IReadOnlyList<McpMonitorStatus>> GetActiveMonitorsAsync(CancellationToken ct = default)
    {
        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _sessions.Values.Select(s => s.ToStatus()).ToList();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 防止重复释放 — ServiceProvider 清理时可能多次调用 DisposeAsync
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var session in _sessions.Values)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            _sessions.Clear();
        }
        finally
        {
            _sessionLock.Release();
            _sessionLock.Dispose();
        }
    }

    private async Task RunMonitorLoopAsync(MonitorSession session, CancellationToken externalCt)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, session.Cts.Token);

        try
        {
            var client = await ResolveMcpClientAsync(session.Config.ServerName).ConfigureAwait(false);

            if (client is null)
            {
                session.State = MonitorState.Error;
                _logger?.LogError("Failed to resolve MCP client for server {ServerName}", session.Config.ServerName);
                return;
            }

            session.State = MonitorState.Running;

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
                    session.State = MonitorState.Error;
                    _logger?.LogWarning(ex, "Monitor {MonitorId} encountered error, attempting reconnect", session.MonitorId);

                    var reconnected = await TryReconnectAsync(session, linkedCts.Token).ConfigureAwait(false);
                    if (!reconnected) break;

                    session.State = MonitorState.Running;
                }
                catch (Exception ex)
                {
                    session.State = MonitorState.Error;
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
            session.State = MonitorState.Error;
            _logger?.LogError(ex, "Monitor {MonitorId} loop crashed", session.MonitorId);
        }
        finally
        {
            if (session.State != MonitorState.Error)
            {
                session.State = MonitorState.Stopped;
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
                ["toolCount"] = JsonElementHelper.FromInt32(toolsResult.Data!.Count),
                ["tools"] = JsonElementHelper.FromObject(toolsResult.Data!.Select(t => t.Name).ToList(), SchedulingJsonContext.Default.ListString)
            });
        }

        var resourcesResult = await client.ListResourcesAsync(ct).ConfigureAwait(false);

        if (resourcesResult.Success && resourcesResult.GetData().Count > 0)
        {
            OnMonitorEvent(session, "resources_update", new Dictionary<string, JsonElement>
            {
                ["resourceCount"] = JsonElementHelper.FromInt32(resourcesResult.Data!.Count)
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
                System.Diagnostics.Trace.WriteLine($"MCP client connect failed for server '{session.Config.ServerName}': {ex.Message}");
            }
        }

        return false;
    }

    private void OnMonitorEvent(MonitorSession session, string eventType, Dictionary<string, JsonElement> data)
    {
        if (session.Config.EventFilters is not null && session.Config.EventFilters.Count > 0 &&
            !session.Config.EventFilters.Contains(eventType))
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

internal sealed class MonitorSession : IAsyncDisposable
{
    public string MonitorId { get; }
    public McpMonitorConfig Config { get; }
    public MonitorState State { get; set; } = MonitorState.Starting;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public int EventsReceivedField;
    public int EventsReceived => Volatile.Read(ref EventsReceivedField);
    public DateTime? LastEventAt { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public MonitorSession(string monitorId, McpMonitorConfig config)
    {
        MonitorId = monitorId;
        Config = config;
    }

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
