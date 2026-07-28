

namespace McpToolHandlers;

/// <summary>
/// Sleep 工具处理器 - 延迟执行
/// </summary>
[McpToolHandler(ToolCategory.Sleep)]
public partial class SleepToolHandlers
{
    [Inject] private readonly ILogger<SleepToolHandlers>? _logger;

    public SleepToolHandlers(ILogger<SleepToolHandlers>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 休眠/延迟指定时间
    /// </summary>
    [McpTool(SystemToolNameConstants.Sleep, "Sleep/delay for a specified duration", MessageRoleConstants.System)]
    public async Task<ToolResult> SleepAsync(
        [McpToolParameter("Sleep duration (seconds), max 1800 (30 minutes)")] int duration_seconds,
        [McpToolParameter("Reason for sleep (optional)", Required = false)] string? reason = null,
        [McpToolParameter("Periodic wake interval (seconds, optional, 0 means no wake, default 0)", Required = false)] int tick_interval_seconds = 0,
        CancellationToken cancellationToken = default)
    {
        if (duration_seconds <= 0)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepDurationMustBePositive))
                .Build();
        }

        const int maxDuration = 1800;
        if (duration_seconds > maxDuration)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepDurationTooLarge, maxDuration))
                .Build();
        }

        if (tick_interval_seconds < 0)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepTickIntervalCannotBeNegative))
                .Build();
        }

        if (tick_interval_seconds > 0 && tick_interval_seconds > duration_seconds)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepTickIntervalTooLarge))
                .Build();
        }

        try
        {
            var tickIntervalDisplay = tick_interval_seconds > 0
                ? L.T(StringKey.SleepTickIntervalSeconds, tick_interval_seconds)
                : L.T(StringKey.SleepTickIntervalNone);

            _logger?.LogInformation(L.T(StringKey.SleepStartLog),
                duration_seconds,
                reason ?? L.T(StringKey.SleepReasonUnspecified),
                tickIntervalDisplay);

            var startTime = DateTime.UtcNow;
            var remainingSeconds = duration_seconds;
            var tickCount = 0;

            if (tick_interval_seconds > 0)
            {
                while (remainingSeconds > 0)
                {
                    var waitSeconds = Math.Min(tick_interval_seconds, remainingSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
                    remainingSeconds -= waitSeconds;
                    tickCount++;

                    if (remainingSeconds > 0)
                    {
                        _logger?.LogInformation(L.T(StringKey.SleepTickLog, tickCount, remainingSeconds));
                    }
                }
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(duration_seconds), cancellationToken).ConfigureAwait(false);
            }

            var actualDuration = DateTime.UtcNow - startTime;

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.SleepCompleted));
            response.AppendLine(L.T(StringKey.SleepPlannedDuration, duration_seconds));
            response.AppendLine(L.T(StringKey.SleepActualDuration, actualDuration.TotalSeconds));

            if (tick_interval_seconds > 0)
            {
                response.AppendLine(L.T(StringKey.SleepTickInterval, tick_interval_seconds));
                response.AppendLine(L.T(StringKey.SleepTickCount, tickCount));
            }

            if (!string.IsNullOrEmpty(reason))
            {
                response.AppendLine(L.T(StringKey.SleepReason, reason));
            }

            return McpResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation(L.T(StringKey.SleepCancelledLog));
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.SleepFailedLog));
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepFailed, ex.Message))
                .Build();
        }
    }

    /// <summary>
    /// 等待特定时间点
    /// </summary>
    [McpTool(SystemToolNameConstants.SleepUntil, "Wait until a specific time point", MessageRoleConstants.System)]
    public async Task<ToolResult> SleepUntilAsync(
        [McpToolParameter("Target time (format: HH:mm or yyyy-MM-dd HH:mm:ss)")] string target_time,
        [McpToolParameter("Timezone offset (hours, optional, default local time)", Required = false)] int? timezone_offset_hours = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target_time))
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepTargetTimeCannotBeEmpty))
                .Build();
        }

        DateTime targetDateTime;

        // 尝试解析时间
        if (DateTime.TryParseExact(target_time, "HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var timeOnly))
        {
            // 今天的这个时间
            var now = DateTime.Now;
            targetDateTime = new DateTime(now.Year, now.Month, now.Day,
                timeOnly.Hour, timeOnly.Minute, 0);

            // 如果今天的时间已过，设置为明天
            if (targetDateTime <= now)
            {
                targetDateTime = targetDateTime.AddDays(1);
            }
        }
        else if (DateTime.TryParse(target_time, out var fullDateTime))
        {
            targetDateTime = fullDateTime;
        }
        else
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepTimeParseFailed, target_time))
                .Build();
        }

        // 应用时区偏移
        if (timezone_offset_hours.HasValue)
        {
            targetDateTime = targetDateTime.AddHours(-timezone_offset_hours.Value);
        }

        var waitDuration = targetDateTime - DateTime.UtcNow;

        // 检查是否已过期
        if (waitDuration <= TimeSpan.Zero)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepTargetTimeExpired, targetDateTime.ToString("yyyy-MM-dd HH:mm:ss")))
                .Build();
        }

        // 检查最大等待时间（30分钟）
        const int maxWaitSeconds = 1800;
        if (waitDuration.TotalSeconds > maxWaitSeconds)
        {
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.SleepWaitTooLong, waitDuration.TotalMinutes, maxWaitSeconds / 60))
                .Build();
        }

        try
        {
            _logger?.LogInformation(L.T(StringKey.SleepUntilStartLog),
                targetDateTime, waitDuration.TotalSeconds);

            await Task.Delay(waitDuration, cancellationToken);

            return McpResultBuilder.Success()
                .WithText(L.T(StringKey.SleepUntilReached, targetDateTime.ToString("yyyy-MM-dd HH:mm:ss")))
                .Build();
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation(L.T(StringKey.SleepUntilCancelledLog));
            throw;
        }
    }
}
