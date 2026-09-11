
namespace Core.Summary;

/// <summary>
/// 离开摘要模板替换所需的数据
/// </summary>
internal sealed record SummaryTemplateData(
    DateTime AwayTime,
    DateTime ReturnTime,
    TimeSpan Duration,
    int ToolCallCount,
    int MessageCount,
    int ErrorCount,
    string KeyEventsText,
    string ErrorDetailsText,
    string PendingText);

/// <summary>
/// 离开摘要命令 — Actor 消息类型
/// </summary>
public interface IAwaySummaryCommand;

public sealed record MarkAwayCmd(TaskCompletionSource Tcs) : IAwaySummaryCommand;
public sealed record GenerateSummaryCmd(TaskCompletionSource<AwaySummaryResult> Tcs) : IAwaySummaryCommand;
public sealed record TrackEventCmd(AwayEvent Event) : IAwaySummaryCommand;
public sealed record AutoSaveTickCmd : IAwaySummaryCommand;

[Register(typeof(IAwaySummaryService), ServiceLifetime.Singleton)]
public sealed partial class AwaySummaryService : ActorBase<IAwaySummaryCommand, Unit>, IAwaySummaryService, IDisposable
{
    private readonly AwaySummaryOptions _options;
    private readonly ILogger<AwaySummaryService>? _logger;
    private readonly IClockService _clock;
    private int _disposed;

    private long _awaySinceTicks;
    private Timer? _autoSaveTimer;
    private readonly Queue<AwayEvent> _events = new();

    public bool IsAway => Volatile.Read(ref _awaySinceTicks) != 0;
    public DateTime? AwaySince => Volatile.Read(ref _awaySinceTicks) is { } ticks && ticks != 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;

    public AwaySummaryService(
        AwaySummaryOptions? options = null,
        ILogger<AwaySummaryService>? logger = null,
        IClockService? clock = null)
        : base()
    {
        _options = options ?? new AwaySummaryOptions();
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task MarkAwayAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new MarkAwayCmd(tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<AwaySummaryResult> GenerateSummaryAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<AwaySummaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new GenerateSummaryCmd(tcs), cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task TrackEventAsync(AwayEvent awayEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awayEvent);
        if (Volatile.Read(ref _awaySinceTicks) == 0) return;
        await SendAsync(new TrackEventCmd(awayEvent), cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(IAwaySummaryCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case MarkAwayCmd mark:
                {
                    var now = _clock.GetUtcNow();
                    Volatile.Write(ref _awaySinceTicks, now.Ticks);
                    _events.Clear();

                    _autoSaveTimer?.Dispose();
                    _autoSaveTimer = new Timer(_ => TrySend(new AutoSaveTickCmd()), null, _options.AutoSaveInterval, _options.AutoSaveInterval);

                    _logger?.LogInformation("用户离开标记: {Time}", now);
                    mark.Tcs.TrySetResult();
                }
                break;

            case GenerateSummaryCmd gen:
                {
                    var result = GenerateSummaryCore();
                    gen.Tcs.TrySetResult(result);
                }
                break;

            case TrackEventCmd track:
                {
                    if (Volatile.Read(ref _awaySinceTicks) == 0) return;
                    while (_events.Count >= _options.MaxEventsToTrack)
                    {
                        _events.Dequeue();
                    }
                    _events.Enqueue(track.Event);
                }
                break;

            case AutoSaveTickCmd:
                await AutoSaveEventsAsync(ct).ConfigureAwait(false);
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[AwaySummary] 消费者异常");
    }

    private AwaySummaryResult GenerateSummaryCore()
    {
        try
        {
            var awayTicks = Volatile.Read(ref _awaySinceTicks);
            if (awayTicks == 0)
            {
                return new AwaySummaryResult
                {
                    Success = false,
                    Summary = "用户未标记为离开状态",
                    AwayTime = _clock.GetUtcNow(),
                    ReturnTime = _clock.GetUtcNow(),
                    Duration = TimeSpan.Zero,
                    TotalEvents = 0,
                    ToolCallCount = 0,
                    MessageCount = 0,
                    ErrorCount = 0,
                    ErrorMessage = "用户未标记为离开状态"
                };
            }

            var awayTime = new DateTime(awayTicks, DateTimeKind.Utc);
            var returnTime = _clock.GetUtcNow();
            var duration = returnTime - awayTime;
            var events = _events.ToArray();

            var toolCallCount = events.Count(e => e.Type == AwayEventType.ToolCall);
            var messageCount = events.Count(e => e.Type == AwayEventType.Message);
            var errorCount = events.Count(e => e.Type == AwayEventType.Error);

            var keyEvents = events
                .Where(e => e.Type != AwayEventType.Error)
                .OrderByDescending(e => e.Timestamp)
                .Take(20)
                .ToList();

            var errors = events
                .Where(e => e.Type == AwayEventType.Error)
                .OrderBy(e => e.Timestamp)
                .ToList();

            var summary = BuildSummary(
                awayTime,
                returnTime,
                duration,
                toolCallCount,
                messageCount,
                errorCount,
                keyEvents,
                errors);

            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
            Volatile.Write(ref _awaySinceTicks, 0);

            _logger?.LogInformation(
                "离开摘要已生成: 时长={Duration}, 事件数={Total}, 工具调用={Tools}, 消息={Msgs}, 错误={Errors}",
                duration, events.Length, toolCallCount, messageCount, errorCount);

            return new AwaySummaryResult
            {
                Success = true,
                Summary = summary,
                AwayTime = awayTime,
                ReturnTime = returnTime,
                Duration = duration,
                TotalEvents = events.Length,
                ToolCallCount = toolCallCount,
                MessageCount = messageCount,
                ErrorCount = errorCount,
                KeyEvents = keyEvents,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "生成离开摘要失败");
            return new AwaySummaryResult
            {
                Success = false,
                Summary = string.Empty,
                AwayTime = AwaySince ?? _clock.GetUtcNow(),
                ReturnTime = _clock.GetUtcNow(),
                Duration = TimeSpan.Zero,
                TotalEvents = 0,
                ToolCallCount = 0,
                MessageCount = 0,
                ErrorCount = 0,
                ErrorMessage = ex.Message
            };
        }
    }

    private string BuildSummary(
        DateTime awayTime,
        DateTime returnTime,
        TimeSpan duration,
        int toolCallCount,
        int messageCount,
        int errorCount,
        IReadOnlyList<AwayEvent> keyEvents,
        IReadOnlyList<AwayEvent> errors)
    {
        var keyEventsText = keyEvents.Count > 0
            ? string.Join("\n", keyEvents.Select(e => $"- [{e.Timestamp:HH:mm:ss}] {e.Description}"))
            : "无关键事件";

        var errorDetailsText = errors.Count > 0
            ? string.Join("\n", errors.Select(e => $"- [{e.Timestamp:HH:mm:ss}] {e.Description}"))
            : "无错误";

        var pendingItems = keyEvents
            .Where(e => e.Metadata.ContainsKey("pending") && e.Metadata["pending"] == "true")
            .Select(e => $"- {e.Description}")
            .ToList();
        var pendingText = pendingItems.Count > 0
            ? string.Join("\n", pendingItems)
            : "无待处理事项";

        var templateData = new SummaryTemplateData(
            awayTime, returnTime, duration, toolCallCount, messageCount, errorCount,
            keyEventsText, errorDetailsText, pendingText);

        var summary = ReplaceTemplatePlaceholders(_options.SummaryTemplate, templateData);

        if (summary.Length > _options.MaxSummaryLength)
        {
            summary = summary[.._options.MaxSummaryLength] + "\n... (摘要已截断)";
        }

        return summary;
    }

    /// <summary>
    /// 将模板占位符替换为实际值（拆分链式调用以满足 JCC6010）
    /// </summary>
    private static string ReplaceTemplatePlaceholders(string template, SummaryTemplateData data)
    {
        return template
            .Replace("{AwayTime}", data.AwayTime.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{ReturnTime}", data.ReturnTime.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{Duration}", DurationFormatter.Format(data.Duration, new DurationFormatOptions { UseAbbreviations = false }))
            .Replace("{ToolCallCount}", data.ToolCallCount.ToString())
            .Replace("{MessageCount}", data.MessageCount.ToString())
            .Replace("{ErrorCount}", data.ErrorCount.ToString())
            .Replace("{KeyEvents}", data.KeyEventsText)
            .Replace("{ErrorDetails}", data.ErrorDetailsText)
            .Replace("{PendingItems}", data.PendingText);
    }

    private async Task AutoSaveEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug("自动保存离开事件: {Count} 个", _events.Count);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "自动保存离开事件失败");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        try
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AwaySummary] Dispose 超时");
        }
    }
}
