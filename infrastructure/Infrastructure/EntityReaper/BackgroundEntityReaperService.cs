namespace Infrastructure.EntityReaper;

/// <summary>
/// 后台实体回收调度服务 — IHostedService，定期调用 EntityReaper.ScanOnce()
/// 启动后延迟 30 秒执行首次扫描，之后按 ScanInterval 循环扫描
/// </summary>
public sealed class BackgroundEntityReaperService : IHostedService, IAsyncDisposable
{
    private readonly IEntityReaper _reaper;
    private readonly IClockService _clock;
    private readonly EntityReaperConfig _config;
    private readonly ILogger<BackgroundEntityReaperService>? _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    public BackgroundEntityReaperService(
        IEntityReaper reaper,
        IClockService clock,
        EntityReaperConfig? config = null,
        ILogger<BackgroundEntityReaperService>? logger = null)
    {
        _reaper = reaper;
        _clock = clock;
        _config = config ?? new EntityReaperConfig();
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        _logger?.LogDebug("后台实体回收服务已启动，{Delay}后执行首次扫描", InitialDelay);
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

        _logger?.LogDebug("后台实体回收服务已停止");
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

            _reaper.ScanOnce();

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_config.ScanInterval, _clock.TimeProvider, cancellationToken).ConfigureAwait(false);
                _reaper.ScanOnce();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "后台实体回收循环异常退出");
        }
    }
}
