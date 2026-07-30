namespace Infrastructure.Housekeeping;

/// <summary>
/// 后台家政清理调度服务 — 对齐 TS startBackgroundHousekeeping
/// 启动后延迟 10 分钟执行首次清理，之后每 24 小时循环执行
/// 使用标记文件节流，24 小时内不重复执行
/// </summary>
public sealed class BackgroundHousekeepingService : IHostedService, IAsyncDisposable
{
    private readonly IHousekeepingService _housekeeping;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly ILogger<BackgroundHousekeepingService>? _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly string JccDir = WorkflowConstants.Paths.JccDirectory;
    private static readonly string MarkerFilePath = Path.Combine(
        WorkflowConstants.Paths.JccDirectory, ".housekeeping-last-run");

    /// <summary>
    /// 延迟首次执行时间 — 对齐 TS DELAY_VERY_SLOW_OPERATIONS (10分钟)
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 循环执行间隔 — 对齐 TS RECURRING_CLEANUP_INTERVAL_MS (24小时)
    /// </summary>
    private static readonly TimeSpan RecurringInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// 标记文件有效期 — 24小时内不重复执行
    /// </summary>
    private static readonly TimeSpan MarkerValidity = TimeSpan.FromHours(24);

    public BackgroundHousekeepingService(
        IHousekeepingService housekeeping,
        IFileSystem fs,
        IClockService clock,
        ILogger<BackgroundHousekeepingService>? logger = null)
    {
        _housekeeping = housekeeping;
        _fs = fs;
        _clock = clock;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        _logger?.LogDebug("后台家政清理服务已启动，{Delay}后执行首次清理", InitialDelay);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // 后台循环任务由 StartAsync 启动，StopAsync 等待其完成是安全的
                await _loopTask.ConfigureAwait(true);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
        }

        _logger?.LogDebug("后台家政清理服务已停止");
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        return ValueTask.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialDelay, _clock.TimeProvider, cancellationToken).ConfigureAwait(false);

            await ExecuteCleanupAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RecurringInterval, _clock.TimeProvider, cancellationToken).ConfigureAwait(false);
                await ExecuteCleanupAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "后台家政清理循环异常退出");
        }
    }

    private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        if (IsRecentlyRun()) return;

        try
        {
            var count = await _housekeeping.RunAllCleanupAsync(currentSessionId: "", cancellationToken).ConfigureAwait(false);
            WriteMarkerFile();

            if (count > 0)
            {
                _logger?.LogDebug("后台家政清理执行完成，清理 {Count} 项", count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "后台家政清理执行失败");
        }
    }

    /// <summary>
    /// 检查标记文件判断是否近期已执行 — 24小时内不重复
    /// </summary>
    private bool IsRecentlyRun()
    {
        try
        {
            if (!_fs.FileExists(MarkerFilePath)) return false;

            var lastRun = _fs.GetLastWriteTimeUtc(MarkerFilePath);
            return _clock.GetUtcNow() - lastRun < MarkerValidity;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 写入标记文件记录本次执行时间
    /// </summary>
    private void WriteMarkerFile()
    {
        try
        {
            if (!_fs.DirectoryExists(JccDir))
            {
                _fs.CreateDirectory(JccDir);
            }

            _fs.WriteAllText(MarkerFilePath, _clock.GetUtcNow().ToString("O"));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "写入家政清理标记文件失败");
        }
    }
}
