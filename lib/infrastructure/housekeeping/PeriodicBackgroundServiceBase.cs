namespace Infrastructure;

/// <summary>
/// 周期性后台服务基类 — 封装 IHostedService + IAsyncDisposable 的通用模板
/// 子类只需提供 InitialDelay、Interval、ExecuteAsync 三个抽象成员
/// </summary>
public abstract class PeriodicBackgroundServiceBase : IHostedService, IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>首次执行前的初始延迟</summary>
    protected abstract TimeSpan InitialDelay { get; }

    /// <summary>每次执行之间的间隔</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>时钟服务,用于基于 TimeProvider 的延迟</summary>
    protected abstract IClockService Clock { get; }

    /// <summary>可选日志记录器</summary>
    protected abstract ILogger? Logger { get; }

    /// <summary>服务名称,用于日志标识</summary>
    protected abstract string ServiceName { get; }

    /// <summary>
    /// 启动周期性后台服务,创建取消令牌并启动循环任务
    /// </summary>
    /// <param name="cancellationToken">启动取消令牌</param>
    /// <returns>表示启动完成的任务</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        Logger?.LogDebug("{ServiceName}已启动，{Delay}后执行首次操作", ServiceName, InitialDelay);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止周期性后台服务,取消循环任务并等待其退出
    /// </summary>
    /// <param name="cancellationToken">停止取消令牌</param>
    /// <returns>表示停止完成的任务</returns>
    [SuppressMessage("Threading", "VSTHRD003:Avoid awaiting foreign tasks", Justification = "IHostedService.StopAsync必须等待循环任务退出,标准模式")]
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Logger?.LogDebug("{ServiceName}已停止", ServiceName);
    }

    /// <summary>
    /// 释放资源,取消并释放取消令牌
    /// </summary>
    /// <returns>表示释放完成的任务</returns>
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

    /// <summary>
    /// 子类实现的周期执行逻辑
    /// </summary>
    /// <param name="cancellationToken">循环取消令牌</param>
    /// <returns>表示单次执行完成的任务</returns>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
