namespace Core.CostTracking;

/// <summary>
/// 分析服务 — 跟踪工具调用、代理执行等分析事件，支持持久化、查询与导出
/// </summary>
[Register(typeof(IAnalyticsService), ServiceLifetime.Singleton)]
public sealed partial class AnalyticsService : ServiceEntity, IAnalyticsService, IDisposable {
    private ImmutableList<AnalyticsEvent> _events = ImmutableList<AnalyticsEvent>.Empty;
    private ImmutableDictionary<AnalyticsEventType, ImmutableList<AnalyticsEvent>> _byType = ImmutableDictionary<AnalyticsEventType, ImmutableList<AnalyticsEvent>>.Empty;
    private ImmutableDictionary<DateTime, ImmutableList<AnalyticsEvent>> _byDate = ImmutableDictionary<DateTime, ImmutableList<AnalyticsEvent>>.Empty;
    private ImmutableDictionary<string, ITelemetrySpan> _agentSpans = ImmutableDictionary<string, ITelemetrySpan>.Empty;
    private readonly ILogger<AnalyticsService>? _logger;
    private readonly IFileOperationService? _fileOperationService;
    private readonly string? _storagePath;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>
    /// 构造分析服务实例
    /// </summary>
    /// <param name="fileOperationService">文件操作服务（可选，提供时启用历史持久化）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="storagePath">历史数据存储路径（可选）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    public AnalyticsService(
        IFileOperationService? fileOperationService = null,
        ILogger<AnalyticsService>? logger = null,
        string? storagePath = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null) {
        _fileOperationService = fileOperationService;
        _logger = logger;
        _storagePath = storagePath;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;

        if (!string.IsNullOrEmpty(storagePath) && fileOperationService != null) {
            _ = Task.Run(() => LoadHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 跟踪分析事件
    /// </summary>
    /// <param name="type">事件类型</param>
    /// <param name="name">事件名称</param>
    /// <param name="data">事件附加数据字典（可选）</param>
    /// <param name="agentName">代理名称（可选）</param>
    public void TrackEvent(AnalyticsEventType type, string name, Dictionary<string, JsonElement>? data = null, string? agentName = null) {
        if (_disposed != 0) return;

        var eventId = Guid.NewGuid().ToString("N")[..8];

        var analyticsEvent = new AnalyticsEvent {
            EventId = eventId,
            Type = type,
            Name = name,
            AgentName = agentName,
            Data = data ?? new Dictionary<string, JsonElement>(),
            Timestamp = _clock.GetUtcNow()
        };

        AddEventToIndices(analyticsEvent);

        _logger?.LogDebug("[Analytics] 事件: {EventType} - {EventName}", type, name);

        if (!string.IsNullOrEmpty(_storagePath) && _disposed == 0) {
            _ = Task.Run(() => SaveHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }

        TrimEventsIfNeeded();
    }

    /// <summary>
    /// 跟踪工具调用事件 — 记录调用结果与耗时，并上报遥测指标
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="success">是否调用成功</param>
    /// <param name="durationMs">调用耗时（毫秒）</param>
    /// <param name="data">附加数据字典（可选）</param>
    /// <param name="agentName">代理名称（可选）</param>
    public void TrackToolCall(string toolName, bool success, double durationMs, Dictionary<string, JsonElement>? data = null, string? agentName = null) {
        TrackEvent(
            success ? AnalyticsEventType.ToolSuccess : AnalyticsEventType.ToolError,
            toolName,
            new Dictionary<string, JsonElement>(data ?? new()) {
                ["duration_ms"] = JsonSerializer.SerializeToElement(durationMs, CostTrackingJsonContext.Default.Double),
                ["success"] = JsonSerializer.SerializeToElement(success, CostTrackingJsonContext.Default.Boolean)
            },
            agentName);

        if (_telemetryService != null) {
            var durationHistogram = _telemetryService.GetHistogram("analytics.tool.duration", "ms", "Tool call duration");
            durationHistogram.Record(durationMs, new Dictionary<string, string> { ["tool"] = toolName, ["success"] = success.ToString() });

            var callCounter = _telemetryService.GetCounter("analytics.tool.calls", "count", "Tool call count");
            callCounter.Add(1, new Dictionary<string, string> { ["tool"] = toolName, ["success"] = success.ToString() });
        }
    }

    /// <summary>
    /// 跟踪工具调用错误事件
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="data">附加数据字典（可选）</param>
    /// <param name="agentName">代理名称（可选）</param>
    public void TrackToolError(string toolName, string errorMessage, Dictionary<string, JsonElement>? data = null, string? agentName = null) {
        TrackEvent(
            AnalyticsEventType.ToolError,
            toolName,
            new Dictionary<string, JsonElement>(data ?? new()) {
                ["error"] = JsonSerializer.SerializeToElement(errorMessage, CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null) {
            var errorCounter = _telemetryService.GetCounter("analytics.tool.errors", "count", "Tool error count");
            errorCounter.Add(1, new Dictionary<string, string> { ["tool"] = toolName });
        }
    }

    /// <summary>
    /// 跟踪代理启动事件 — 同时在遥测服务中开启代理执行 span
    /// </summary>
    /// <param name="agentName">代理名称</param>
    /// <param name="sessionId">会话标识（可选）</param>
    public void TrackAgentStart(string agentName, string? sessionId = null) {
        TrackEvent(
            AnalyticsEventType.AgentStart,
            $"agent_{agentName}_start",
            new Dictionary<string, JsonElement> {
                ["session_id"] = JsonSerializer.SerializeToElement(sessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId, CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null) {
            var span = _telemetryService.StartSpan($"agent.{agentName}", TelemetrySpanKind.Server);
            span.SetTag("agent.name", agentName);
            if (!string.IsNullOrEmpty(sessionId)) {
                span.SetTag("agent.session_id", sessionId);
            }
            var spanKey = $"{agentName}:{sessionId ?? string.Empty}";
            ImmutableInterlocked.Update(ref _agentSpans, static (d, arg) => d.SetItem(arg.key, arg.span), (key: spanKey, span));
        }
    }

    /// <summary>
    /// 异步跟踪代理完成事件 — 结束对应遥测 span 并记录执行耗时
    /// </summary>
    /// <param name="agentName">代理名称</param>
    /// <param name="success">是否执行成功</param>
    /// <param name="durationMs">执行耗时（毫秒）</param>
    /// <param name="sessionId">会话标识（可选）</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task TrackAgentCompleteAsync(string agentName, bool success, double durationMs, string? sessionId = null) {
        TrackEvent(
            AnalyticsEventType.AgentComplete,
            $"agent_{agentName}_complete",
            new Dictionary<string, JsonElement> {
                ["success"] = JsonSerializer.SerializeToElement(success, CostTrackingJsonContext.Default.Boolean),
                ["duration_ms"] = JsonSerializer.SerializeToElement(durationMs, CostTrackingJsonContext.Default.Double),
                ["session_id"] = JsonSerializer.SerializeToElement(sessionId ?? string.Empty, CostTrackingJsonContext.Default.String)
            },
            agentName);

        if (_telemetryService != null) {
            var spanKey = $"{agentName}:{sessionId ?? string.Empty}";
            if (_agentSpans.TryGetValue(spanKey, out var span)) {
                ImmutableInterlocked.Update(ref _agentSpans, static (d, k) => d.Remove(k), spanKey);
                span.SetStatus(success ? TelemetryStatusCode.Ok : TelemetryStatusCode.Error);
                span.SetTag("agent.duration_ms", durationMs);
                await span.DisposeAsync().ConfigureAwait(false);
            }

            var agentDuration = _telemetryService.GetHistogram("analytics.agent.duration", "ms", "Agent execution duration");
            agentDuration.Record(durationMs, new Dictionary<string, string> { ["agent"] = agentName, ["success"] = success.ToString() });
        }
    }

    /// <summary>
    /// 获取工具使用统计信息
    /// </summary>
    /// <param name="days">统计天数（可选，默认统计全部历史）</param>
    /// <returns>按调用次数降序排列的工具使用统计列表</returns>
    public List<ToolUsageStatistics> GetToolUsageStatistics(int? days = null) {
        var cutoffDate = days.HasValue ? _clock.GetUtcNow().AddDays(-days.Value) : DateTime.MinValue;

        var toolEvents = GetToolEvents()
            .Where(e => e.Timestamp >= cutoffDate)
            .ToList();

        var grouped = toolEvents
            .GroupBy(e => e.Name)
            .Select(g => new ToolUsageStatistics {
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

    /// <summary>
    /// 获取使用情况综合报告 — 包含事件总数、工具调用成功率、日均统计等
    /// </summary>
    /// <param name="days">统计天数（可选，默认统计全部历史）</param>
    /// <returns>使用情况综合报告</returns>
    public UsageStatisticsReport GetUsageReport(int? days = null) {
        var cutoffDate = days.HasValue ? _clock.GetUtcNow().AddDays(-days.Value) : DateTime.MinValue;

        var events = GetEventsSince(cutoffDate).ToList();
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
                g => new DailyStatistics {
                    Date = g.Key,
                    EventCount = g.Count(),
                    ToolCalls = g.Count(e => e.Type == AnalyticsEventType.ToolCall ||
                                            e.Type == AnalyticsEventType.ToolSuccess ||
                                            e.Type == AnalyticsEventType.ToolError),
                    ErrorCount = g.Count(e => e.Type == AnalyticsEventType.ToolError || e.Type == AnalyticsEventType.SystemError),
                    ActiveAgents = g.Select(e => e.AgentName).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count()
                });

        return new UsageStatisticsReport {
            TotalEvents = events.Count,
            TotalToolCalls = totalToolCalls,
            ToolSuccessRate = totalToolCalls > 0 ? (double)successfulToolCalls / totalToolCalls * 100 : 0,
            AverageToolDurationMs = toolEvents.Where(e => e.DurationMs.HasValue).Select(e => e.DurationMs ?? 0).DefaultIfEmpty(0).Average(),
            TopTools = GetToolUsageStatistics(days).Take(10).ToList(),
            DailyStats = dailyStats,
            ErrorRate = events.Count > 0 ? (double)errorCount / events.Count * 100 : 0
        };
    }

    /// <summary>
    /// 获取事件历史列表
    /// </summary>
    /// <param name="type">事件类型过滤（可选，默认不过滤）</param>
    /// <param name="limit">返回条数上限</param>
    /// <returns>按时间降序排列的事件列表</returns>
    public List<AnalyticsEvent> GetEventHistory(AnalyticsEventType? type = null, int limit = WorkflowConstants.Analytics.DefaultEventHistoryLimit) {
        var list = type.HasValue && _byType.TryGetValue(type.Value, out var typedList)
            ? typedList
            : _events;

        if (list.Count == 0) return new List<AnalyticsEvent>();
        var take = Math.Min(limit, list.Count);
        var result = new List<AnalyticsEvent>(take);
        for (var i = list.Count - 1; i >= 0 && result.Count < take; i--) {
            result.Add(list[i]);
        }
        return result;
    }

    /// <summary>
    /// 清除历史事件 — 可指定仅清除指定天数之前的数据
    /// </summary>
    /// <param name="olderThanDays">清除该天数之前的数据（可选，默认清除全部）</param>
    public void ClearHistory(int? olderThanDays = null) {
        if (olderThanDays.HasValue) {
            var cutoffDate = _clock.GetUtcNow().AddDays(-olderThanDays.Value);

            ImmutableInterlocked.Update(ref _events, static (list, cutoff) => {
                var builder = ImmutableList.CreateBuilder<AnalyticsEvent>();
                foreach (var e in list) if (e.Timestamp >= cutoff) builder.Add(e);
                return builder.ToImmutable();
            }, cutoffDate);
            RebuildIndices();

            _logger?.LogInformation("已清除 {Days} 天前的分析数据", olderThanDays.Value);
        } else {
            Interlocked.Exchange(ref _events, ImmutableList<AnalyticsEvent>.Empty);
            Interlocked.Exchange(ref _byType, ImmutableDictionary<AnalyticsEventType, ImmutableList<AnalyticsEvent>>.Empty);
            Interlocked.Exchange(ref _byDate, ImmutableDictionary<DateTime, ImmutableList<AnalyticsEvent>>.Empty);
            _logger?.LogInformation("已清除所有分析数据");
        }

        if (!string.IsNullOrEmpty(_storagePath) && _fileOperationService != null) {
            _ = Task.Run(() => SaveHistoryAsync(_disposeCts.Token)).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步导出分析数据为 JSON 字符串
    /// </summary>
    /// <param name="startDate">起始时间过滤（可选）</param>
    /// <param name="endDate">结束时间过滤（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缩进格式化的 JSON 字符串</returns>
    public async Task<string> ExportDataAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default) {
        List<AnalyticsEvent> data;
        if (startDate.HasValue || endDate.HasValue) {
            data = GetEventsInRange(startDate, endDate).OrderBy(e => e.Timestamp).ToList();
        } else {
            data = _events.ToList();
        }

        var export = new AnalyticsExportData {
            ExportTime = _clock.GetUtcNow(),
            StartDate = startDate,
            EndDate = endDate,
            EventCount = data.Count,
            Events = data
        };

        return JsonSerializer.Serialize(export, CostTrackingIndentedJsonContext.Default.AnalyticsExportData);
    }

    #region Private Methods

    /// <summary>
    /// 原子添加事件到三个索引(_events + _byType + _byDate),无锁并行检索安全。
    /// <para>所有列表按 Timestamp 升序排序存储(二分法插入),制造排序条件提升检索效率。</para>
    /// </summary>
    private void AddEventToIndices(AnalyticsEvent e) {
        ImmutableInterlocked.Update(ref _events, static (list, ev) => InsertByTime(list, ev), e);
        ImmutableInterlocked.Update(ref _byType, static (dict, ev) => {
            var list = dict.GetValueOrDefault(ev.Type) ?? ImmutableList<AnalyticsEvent>.Empty;
            return dict.SetItem(ev.Type, InsertByTime(list, ev));
        }, e);
        ImmutableInterlocked.Update(ref _byDate, static (dict, ev) => {
            var date = ev.Timestamp.Date;
            var list = dict.GetValueOrDefault(date) ?? ImmutableList<AnalyticsEvent>.Empty;
            return dict.SetItem(date, InsertByTime(list, ev));
        }, e);
    }

    private static readonly IComparer<AnalyticsEvent> s_timeComparer = Comparer<AnalyticsEvent>.Create(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));

    /// <summary>O(log n) 二分法插入到按 Timestamp 排序的列表。事件几乎按时间顺序到达时插入末尾 O(1)。</summary>
    private static ImmutableList<AnalyticsEvent> InsertByTime(ImmutableList<AnalyticsEvent> list, AnalyticsEvent e) {
        var index = list.BinarySearch(e, s_timeComparer);
        if (index < 0) index = ~index;
        return list.Insert(index, e);
    }

    /// <summary>
    /// 从 _events 快照重建 _byType + _byDate 索引 — ClearHistory/TrimEventsIfNeeded 后调用。
    /// <para>_events 已按 Timestamp 升序存储,遍历时按顺序 Add 到桶内,桶内自动保持 Timestamp 升序。</para>
    /// </summary>
    private void RebuildIndices() {
        var snapshot = _events;
        var byTypeBuilder = ImmutableDictionary.CreateBuilder<AnalyticsEventType, ImmutableList<AnalyticsEvent>>();
        var byDateBuilder = ImmutableDictionary.CreateBuilder<DateTime, ImmutableList<AnalyticsEvent>>();
        foreach (var e in snapshot) {
            byTypeBuilder[e.Type] = (byTypeBuilder.GetValueOrDefault(e.Type) ?? ImmutableList<AnalyticsEvent>.Empty).Add(e);
            var date = e.Timestamp.Date;
            byDateBuilder[date] = (byDateBuilder.GetValueOrDefault(date) ?? ImmutableList<AnalyticsEvent>.Empty).Add(e);
        }
        Interlocked.Exchange(ref _byType, byTypeBuilder.ToImmutable());
        Interlocked.Exchange(ref _byDate, byDateBuilder.ToImmutable());
    }

    /// <summary>
    /// 用 _byType 索引 O(1) 查找工具事件,避免全量扫描 — 并行检索安全
    /// </summary>
    private IEnumerable<AnalyticsEvent> GetToolEvents() {
        foreach (var type in s_toolEventTypes) {
            if (_byType.TryGetValue(type, out var list)) {
                foreach (var e in list) yield return e;
            }
        }
    }

    private static readonly AnalyticsEventType[] s_toolEventTypes = [
        AnalyticsEventType.ToolCall,
        AnalyticsEventType.ToolSuccess,
        AnalyticsEventType.ToolError
    ];

    /// <summary>
    /// 用 _byDate 索引按日期分桶查找 cutoff 之后的事件,避免全量扫描 — 并行检索安全
    /// </summary>
    private IEnumerable<AnalyticsEvent> GetEventsSince(DateTime cutoff) {
        foreach (var kvp in _byDate) {
            if (kvp.Key < cutoff.Date) continue;
            foreach (var e in kvp.Value) {
                if (e.Timestamp >= cutoff) yield return e;
            }
        }
    }

    /// <summary>
    /// 用 _byDate 索引按日期范围查找事件,避免全量扫描 — 并行检索安全
    /// </summary>
    private IEnumerable<AnalyticsEvent> GetEventsInRange(DateTime? start, DateTime? end) {
        var startDate = start?.Date ?? DateTime.MinValue;
        var endDate = end?.Date ?? DateTime.MaxValue;
        foreach (var kvp in _byDate) {
            if (kvp.Key < startDate || kvp.Key > endDate) continue;
            foreach (var e in kvp.Value) {
                if ((start is null || e.Timestamp >= start.Value) &&
                    (end is null || e.Timestamp <= end.Value))
                    yield return e;
            }
        }
    }

    private void TrimEventsIfNeeded() {
        var maxEvents = WorkflowConstants.Analytics.MaxEvents;
        var trimmed = new StrongBox<bool>();

        ImmutableInterlocked.Update(ref _events, static (list, arg) => {
            if (list.Count <= arg.max) return list;
            arg.trimmed.Value = true;
            return list.RemoveRange(0, list.Count - arg.max);
        }, (max: maxEvents, trimmed));

        if (trimmed.Value) RebuildIndices();
    }

    private async Task SaveHistoryAsync(CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(_storagePath) || _fileOperationService == null) return;

        try {
            var events = _events.ToList();
            var json = JsonSerializer.Serialize(events, CostTrackingIndentedJsonContext.Default.ListAnalyticsEvent);

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
            if (!result.Success) {
                _logger?.LogError("保存分析数据失败: {Error}", result.ErrorMessage);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "保存分析数据失败");
        }
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(_storagePath) || _fileOperationService == null) return;

        try {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success) {
                return;
            }

            var events = RelaxedJsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.ListAnalyticsEvent);

            if (events != null) {
                foreach (var e in events.OrderBy(e => e.Timestamp)) {
                    AddEventToIndices(e);
                }

                _logger?.LogInformation("已加载 {Count} 条历史分析数据", events.Count);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "加载分析数据失败");
        }
    }

    #endregion

    /// <summary>
    /// 释放资源 — 取消内部令牌并释放遥测 span
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.CancelAndDisposeSafe(_logger);
        base.Dispose();
    }
}