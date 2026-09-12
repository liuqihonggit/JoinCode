namespace Infrastructure;

/// <summary>
/// 周期性后台服务基类 — 封装 IHostedService + IAsyncDisposable 的通用模板
/// 子类只需提供 InitialDelay、Interval、ExecuteAsync 三个抽象成员
/// </summary>
public abstract class PeriodicBackgroundServiceBase : IHostedService, IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    protected abstract TimeSpan InitialDelay { get; }
    protected abstract TimeSpan Interval { get; }
    protected abstract IClockService Clock { get; }
    protected abstract ILogger? Logger { get; }
    protected abstract string ServiceName { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        Logger?.LogDebug("{ServiceName}已启动，{Delay}后执行首次操作", ServiceName, InitialDelay);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _loopTask.ConfigureAwait(true);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
        }

        Logger?.LogDebug("{ServiceName}已停止", ServiceName);
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
            await Task.Delay(InitialDelay, Clock.TimeProvider, cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Interval, Clock.TimeProvider, cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "{ServiceName}循环异常退出", ServiceName);
        }
    }

    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
