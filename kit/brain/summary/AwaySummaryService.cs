
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

/// <summary>
/// 标记用户离开命令
/// </summary>
/// <param name="Tcs">完成信号源，用于异步等待命令处理完成。</param>
public sealed record MarkAwayCmd(TaskCompletionSource Tcs) : IAwaySummaryCommand;

/// <summary>
/// 生成离开摘要命令
/// </summary>
/// <param name="Tcs">完成信号源，携带生成的离开摘要结果。</param>
public sealed record GenerateSummaryCmd(TaskCompletionSource<AwaySummaryResult> Tcs) : IAwaySummaryCommand;

/// <summary>
/// 跟踪事件命令
/// </summary>
/// <param name="Event">要跟踪的离开事件。</param>
public sealed record TrackEventCmd(AwayEvent Event) : IAwaySummaryCommand;

/// <summary>
/// 自动保存定时触发命令
/// </summary>
public sealed record AutoSaveTickCmd : IAwaySummaryCommand;

/// <summary>
/// 离开摘要服务 — 基于 Actor 模型管理用户离开期间的摘要生成与事件跟踪
/// </summary>
[Register(typeof(IAwaySummaryService), ServiceLifetime.Singleton)]
public sealed partial class AwaySummaryService : ActorBase<IAwaySummaryCommand, Unit>, IAwaySummaryService
{
    private readonly AwaySummaryOptions _options;
    private readonly ILogger<AwaySummaryService>? _logger;
    private readonly IClockService _clock;
    private int _disposed;

    private long _awaySinceTicks;
    private Timer? _autoSaveTimer;
    private readonly Queue<AwayEvent> _events = new();

    /// <summary>
    /// 获取一个值，指示用户当前是否处于离开状态。
    /// </summary>
    public bool IsAway => Volatile.Read(ref _awaySinceTicks) != 0;

    /// <summary>
    /// 获取用户离开时刻；若用户未离开，返回 <c>null</c>。
    /// </summary>
    public DateTime? AwaySince => Volatile.Read(ref _awaySinceTicks) is { } ticks && ticks != 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;

    /// <summary>
    /// 初始化 <see cref="AwaySummaryService"/> 的新实例。
    /// </summary>
    /// <param name="options">离开摘要配置选项；为 <c>null</c> 时使用默认配置。</param>
    /// <param name="logger">日志记录器；为 <c>null</c> 时不记录日志。</param>
    /// <param name="clock">时钟服务；为 <c>null</c> 时使用系统时钟。</param>
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


    /// <summary>
    /// 异步标记用户离开，启动离开期间的事件跟踪与自动保存。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task MarkAwayAsync(CancellationToken ct = default)
    {
        var tcs = TcsFactory.Create();
        await SendAsync(new MarkAwayCmd(tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 异步生成离开摘要，汇总离开期间的事件、错误与待处理事项。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>包含汇总结果的 <see cref="AwaySummaryResult"/> 任务。</returns>
    public async Task<AwaySummaryResult> GenerateSummaryAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<AwaySummaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new GenerateSummaryCmd(tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 异步跟踪一个离开事件；若用户未处于离开状态则忽略。
    /// </summary>
    /// <param name="awayEvent">要跟踪的离开事件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task TrackEventAsync(AwayEvent awayEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(awayEvent);
        if (Volatile.Read(ref _awaySinceTicks) == 0) return;
        await SendAsync(new TrackEventCmd(awayEvent), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理 Actor 收到的离开摘要命令。
    /// </summary>
    /// <param name="command">要处理的命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的值任务。</returns>
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

    /// <summary>
    /// 消费者发生异常时的回调处理，记录错误日志。
    /// </summary>
    /// <param name="ex">消费者抛出的异常。</param>
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

    private async Task AutoSaveEventsAsync(CancellationToken ct)
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

    /// <summary>
    /// 异步释放本服务持有的资源，包括自动保存定时器与 Actor 异步释放。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
