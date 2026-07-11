
namespace Core.CostTracking;

[Register]
public sealed partial class AnalyticsService : IAnalyticsService, IDisposable
{
    private readonly ConcurrentQueue<AnalyticsEvent> _events = new();
    private readonly ConcurrentDictionary<string, ITelemetrySpan> _agentSpans = new();
    [Inject] private readonly ILogger<AnalyticsService>? _logger;
    private readonly IFileOperationService? _fileOperationService;
    private readonly string? _storagePath;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly CancellationTokenSource _disposeCts = new();
    private volatile int _disposed;

    public AnalyticsService(
        IFileOperationService? fileOperationService = null,
        ILogger<AnalyticsService>? logger = null,
        string? storagePath = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _fileOperationService = fileOperationService;
        _logger = logger;
        _storagePath = storagePath;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(storagePath) && fileOperationService != null)
        {
            _ = Task.Run(() => LoadHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
    }

    public void TrackEvent(AnalyticsEventType type, string name, Dictionary<string, JsonElement>? data = null, string? agentName = null)
    {
        if (_disposed != 0) return;

        var eventId = Guid.NewGuid().ToString("N")[..8];

        var analyticsEvent = new AnalyticsEvent
        {
            EventId = eventId,
            Type = type,
            Name = name,
            AgentName = agentName,
            Data = data ?? new Dictionary<string, JsonElement>(),
            Timestamp = _clock.GetUtcNow()
        };

        _events.Enqueue(analyticsEvent);

        _logger?.LogDebug("[Analytics] 事件: {EventType} - {EventName}", type, name);

        if (!string.IsNullOrEmpty(_storagePath) && _disposed == 0)
        {
            _ = Task.Run(() => SaveHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }

        TrimEventsIfNeeded();
    }

    public void TrackToolCall(string toolName, bool success, double durationMs, Dictionary<string, JsonElement>? data = null, string? agentName = null)
    {
        TrackEvent(
            success ? AnalyticsEventType.ToolSuccess : AnalyticsEventType.ToolError,
            toolName,
            new Dictionary<string, JsonElement>(data ?? new())
            {
                ["duration_ms"] = JsonSerializer.SerializeToElement(durationMs, CostTrackingJsonContext.Default.Double),
                ["success"] = JsonSerializer.SerializeToElement(success, CostTrackingJsonContext.Default.Boolean)
            },
            agentName);

        if (_telemetryService != null)
        {
            var durationHistogram = _telemetryService.GetHistogram("analytics.tool.duration", "ms", "Tool call duration");
            durationHistogram.Record(durationMs, new Dictionary<string, string> { ["tool"] = toolName, ["success"] = success.ToString() });

            var callCounter = _telemetryService.GetCounter("analytics.tool.calls", "count", "Tool call count");
            callCounter.Add(1, new Dictionary<string, string> { ["tool"] = toolName, ["success"] = success.ToString() });
        }
    }

    public void TrackToolError(string toolName, string errorMessage, Dictionary<string, JsonElement>? data = null, string? agentName = null)
    {
        TrackEvent(
            AnalyticsEventType.ToolError,
            toolName,
            new Dictionary<string, JsonElement>(data ?? new())
            {
                ["error"] = JsonSerializer.SerializeToElement(errorMessage, CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null)
        {
            var errorCounter = _telemetryService.GetCounter("analytics.tool.errors", "count", "Tool error count");
            errorCounter.Add(1, new Dictionary<string, string> { ["tool"] = toolName });
        }
    }

    public void TrackAgentStart(string agentName, string? sessionId = null)
    {
        TrackEvent(
            AnalyticsEventType.AgentStart,
            $"agent_{agentName}_start",
            new Dictionary<string, JsonElement>
            {
                ["session_id"] = JsonSerializer.SerializeToElement(sessionId ?? Guid.NewGuid().ToString("N")[..8], CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null)
        {
            var span = _telemetryService.StartSpan($"agent.{agentName}", TelemetrySpanKind.Server);
            span.SetTag("agent.name", agentName);
            if (!string.IsNullOrEmpty(sessionId))
            {
                span.SetTag("agent.session_id", sessionId);
            }
            var spanKey = $"{agentName}:{sessionId ?? string.Empty}";
            _agentSpans[spanKey] = span;
        }
    }

    public void TrackAgentComplete(string agentName, bool success, double durationMs, string? sessionId = null)
    {
        TrackEvent(
            AnalyticsEventType.AgentComplete,
            $"agent_{agentName}_complete",
            new Dictionary<string, JsonElement>
            {
                ["success"] = JsonSerializer.SerializeToElement(success, CostTrackingJsonContext.Default.Boolean),
                ["duration_ms"] = JsonSerializer.SerializeToElement(durationMs, CostTrackingJsonContext.Default.Double),
                ["session_id"] = JsonSerializer.SerializeToElement(sessionId ?? string.Empty, CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null)
        {
            var spanKey = $"{agentName}:{sessionId ?? string.Empty}";
            if (_agentSpans.TryRemove(spanKey, out var span))
            {
                span.SetStatus(success ? TelemetryStatusCode.Ok : TelemetryStatusCode.Error);
                span.SetTag("agent.duration_ms", durationMs);
                span.Dispose();
            }

            var agentDuration = _telemetryService.GetHistogram("analytics.agent.duration", "ms", "Agent execution duration");
            agentDuration.Record(durationMs, new Dictionary<string, string> { ["agent"] = agentName, ["success"] = success.ToString() });
        }
    }

    public List<ToolUsageStatistics> GetToolUsageStatistics(int? days = null)
    {
        var cutoffDate = days.HasValue ? _clock.GetUtcNow().AddDays(-days.Value) : DateTime.MinValue;

        var toolEvents = _events
            .Where(e => (e.Type == AnalyticsEventType.ToolCall ||
                        e.Type == AnalyticsEventType.ToolSuccess ||
                        e.Type == AnalyticsEventType.ToolError) &&
                        e.Timestamp >= cutoffDate)
            .ToList();

        var grouped = toolEvents
            .GroupBy(e => e.Name)
            .Select(g => new ToolUsageStatistics
            {
                ToolName = g.Key,
                CallCount = g.Count(),
                SuccessCount = g.Count(e => e.IsSuccess == true || e.Type == AnalyticsEventType.ToolSuccess),
                ErrorCount = g.Count(e => e.IsSuccess == false || e.Type == AnalyticsEventType.ToolError),
                AverageDurationMs = g.Where(e => e.DurationMs.HasValue).Average(e => e.DurationMs ?? 0),
                LastCallAt = g.Max(e => e.Timestamp)
            })
            .OrderByDescending(s => s.CallCount)
            .ToList();

        return grouped;
    }

    public UsageStatisticsReport GetUsageReport(int? days = null)
    {
        var cutoffDate = days.HasValue ? _clock.GetUtcNow().AddDays(-days.Value) : DateTime.MinValue;

        var events = _events.Where(e => e.Timestamp >= cutoffDate).ToList();
        var toolEvents = events.Where(e => e.Type == AnalyticsEventType.ToolCall ||
                                          e.Type == AnalyticsEventType.ToolSuccess ||
                                          e.Type == AnalyticsEventType.ToolError).ToList();

        var totalToolCalls = toolEvents.Count;
        var successfulToolCalls = toolEvents.Count(e => e.IsSuccess == true || e.Type == AnalyticsEventType.ToolSuccess);
        var errorCount = events.Count(e => e.Type == AnalyticsEventType.ToolError || e.Type == AnalyticsEventType.SystemError);

        var dailyStats = events
            .GroupBy(e => e.Timestamp.Date)
            .ToDictionary(
                g => g.Key,
                g => new DailyStatistics
                {
                    Date = g.Key,
                    EventCount = g.Count(),
                    ToolCalls = g.Count(e => e.Type == AnalyticsEventType.ToolCall ||
                                            e.Type == AnalyticsEventType.ToolSuccess ||
                                            e.Type == AnalyticsEventType.ToolError),
                    ErrorCount = g.Count(e => e.Type == AnalyticsEventType.ToolError || e.Type == AnalyticsEventType.SystemError),
                    ActiveAgents = g.Select(e => e.AgentName).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count()
                });

        return new UsageStatisticsReport
        {
            TotalEvents = events.Count,
            TotalToolCalls = totalToolCalls,
            ToolSuccessRate = totalToolCalls > 0 ? (double)successfulToolCalls / totalToolCalls * 100 : 0,
            AverageToolDurationMs = toolEvents.Where(e => e.DurationMs.HasValue).Average(e => e.DurationMs ?? 0),
            TopTools = GetToolUsageStatistics(days).Take(10).ToList(),
            DailyStats = dailyStats,
            ErrorRate = events.Count > 0 ? (double)errorCount / events.Count * 100 : 0
        };
    }

    public List<AnalyticsEvent> GetEventHistory(AnalyticsEventType? type = null, int limit = WorkflowConstants.Analytics.DefaultEventHistoryLimit)
    {
        var events = _events.AsEnumerable();

        if (type.HasValue)
        {
            events = events.Where(e => e.Type == type.Value);
        }

        return events
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToList();
    }

    public void ClearHistory(int? olderThanDays = null)
    {
        if (olderThanDays.HasValue)
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-olderThanDays.Value);

            var newEvents = new ConcurrentQueue<AnalyticsEvent>();
            foreach (var e in _events.Where(e => e.Timestamp >= cutoffDate))
            {
                newEvents.Enqueue(e);
            }

            while (_events.TryDequeue(out _)) { }

            foreach (var e in newEvents)
            {
                _events.Enqueue(e);
            }

            _logger?.LogInformation("已清除 {Days} 天前的分析数据", olderThanDays.Value);
        }
        else
        {
            while (_events.TryDequeue(out _)) { }
            _logger?.LogInformation("已清除所有分析数据");
        }

        if (!string.IsNullOrEmpty(_storagePath) && _fileOperationService != null)
        {
            _ = Task.Run(() => SaveHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
    }

    public async Task<string> ExportDataAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var events = _events.AsEnumerable();

        if (startDate.HasValue)
        {
            events = events.Where(e => e.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            events = events.Where(e => e.Timestamp <= endDate.Value);
        }

        var data = events.OrderBy(e => e.Timestamp).ToList();

        var export = new AnalyticsExportData
        {
            ExportTime = _clock.GetUtcNow(),
            StartDate = startDate,
            EndDate = endDate,
            EventCount = data.Count,
            Events = data
        };

        return JsonSerializer.Serialize(export, CostTrackingIndentedJsonContext.Default.AnalyticsExportData);
    }

    #region Private Methods

    private void TrimEventsIfNeeded()
    {
        const int maxEvents = WorkflowConstants.Analytics.MaxEvents;

        while (_events.Count > maxEvents && _events.TryDequeue(out _)) { }
    }

    private async Task SaveHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_storagePath) || _fileOperationService == null) return;

        try
        {
            var events = _events.ToList();
            var json = JsonSerializer.Serialize(events, CostTrackingIndentedJsonContext.Default.ListAnalyticsEvent);

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                _logger?.LogError("保存分析数据失败: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "保存分析数据失败");
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_storagePath) || _fileOperationService == null) return;

        try
        {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return;
            }

            var events = JsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.ListAnalyticsEvent);

            if (events != null)
            {
                foreach (var e in events.OrderBy(e => e.Timestamp))
                {
                    _events.Enqueue(e);
                }

                _logger?.LogInformation("已加载 {Count} 条历史分析数据", events.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "加载分析数据失败");
        }
    }

    #endregion

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
