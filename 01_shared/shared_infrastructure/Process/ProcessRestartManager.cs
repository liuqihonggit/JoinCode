namespace Infrastructure.Subprocess;

public sealed class ProcessRestartManager
{
    private readonly int _maxRestarts;
    private readonly ILogger? _logger;
    private int _restartCount;
    private DateTimeOffset _lastRestartTime = DateTimeOffset.MinValue;

    public int RestartCount => Volatile.Read(ref _restartCount);
    public int MaxRestarts => _maxRestarts;
    public DateTimeOffset? LastRestartTime => _lastRestartTime == DateTimeOffset.MinValue ? null : _lastRestartTime;
    public bool CanRestart => _restartCount < _maxRestarts;

    public event EventHandler<ProcessRestartingEventArgs>? BeforeRestart;
    public event EventHandler<ProcessRestartedEventArgs>? AfterRestart;

    public ProcessRestartManager(int maxRestarts = 3, ILogger? logger = null)
    {
        _maxRestarts = maxRestarts;
        _logger = logger;
    }

    public async Task<IInteractiveProcess> RestartAsync(
        IInteractiveProcess currentProcess,
        Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currentProcess);
        ArgumentNullException.ThrowIfNull(spawnFunc);

        if (!CanRestart)
        {
            throw new InvalidOperationException(
                $"已达到最大重启次数 ({_maxRestarts})，不再重启");
        }

        var newCount = Interlocked.Increment(ref _restartCount);
        _lastRestartTime = DateTimeOffset.UtcNow;

        _logger?.LogWarning("[ProcessRestart] 重启进程 (restart={Restart}/{Max}, pid={Pid})",
            newCount, _maxRestarts, currentProcess.Id);

        BeforeRestart?.Invoke(this, new ProcessRestartingEventArgs
        {
            RestartCount = newCount,
            OldProcessId = currentProcess.Id,
        });

        try
        {
            currentProcess.Kill();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ProcessRestart] 杀死旧进程失败 (pid={Pid})", currentProcess.Id);
        }

        try
        {
            await currentProcess.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ProcessRestart] 释放旧进程资源失败");
        }

        var newProcess = await spawnFunc(ct).ConfigureAwait(false);

        _logger?.LogInformation("[ProcessRestart] 新进程已启动 (pid={Pid})", newProcess.Id);

        AfterRestart?.Invoke(this, new ProcessRestartedEventArgs
        {
            RestartCount = newCount,
            NewProcessId = newProcess.Id,
        });

        return newProcess;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _restartCount, 0);
        _lastRestartTime = DateTimeOffset.MinValue;
    }
}

public sealed class ProcessRestartingEventArgs : EventArgs
{
    public required int RestartCount { get; init; }
    public required int OldProcessId { get; init; }
}

public sealed class ProcessRestartedEventArgs : EventArgs
{
    public required int RestartCount { get; init; }
    public required int NewProcessId { get; init; }
}
