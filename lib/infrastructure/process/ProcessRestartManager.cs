namespace Infrastructure.Subprocess;

/// <summary>
/// 进程重启管理器 — 限制最大重启次数，杀死旧进程并启动新进程，触发前置/后置事件
/// </summary>
public sealed class ProcessRestartManager
{
    private readonly int _maxRestarts;
    private readonly ILogger? _logger;
    private int _restartCount;
    private DateTimeOffset _lastRestartTime = DateTimeOffset.MinValue;

    /// <summary>已重启次数</summary>
    public int RestartCount => Volatile.Read(ref _restartCount);
    /// <summary>最大允许重启次数</summary>
    public int MaxRestarts => _maxRestarts;
    /// <summary>最近一次重启时间（尚未重启过则返回 null）</summary>
    public DateTimeOffset? LastRestartTime => _lastRestartTime == DateTimeOffset.MinValue ? null : _lastRestartTime;
    /// <summary>是否仍可重启 — 重启次数未达上限时为 true</summary>
    public bool CanRestart => _restartCount < _maxRestarts;

    /// <summary>重启前事件 — 杀死旧进程前触发</summary>
    public event EventHandler<ProcessRestartingEventArgs>? BeforeRestart;
    /// <summary>重启后事件 — 新进程启动后触发</summary>
    public event EventHandler<ProcessRestartedEventArgs>? AfterRestart;

    /// <summary>
    /// 构造进程重启管理器
    /// </summary>
    /// <param name="maxRestarts">最大允许重启次数，默认 3</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ProcessRestartManager(int maxRestarts = 3, ILogger? logger = null)
    {
        _maxRestarts = maxRestarts;
        _logger = logger;
    }

    /// <summary>
    /// 重启进程 — 杀死当前进程并通过 spawnFunc 启动新进程，达上限时抛 InvalidOperationException
    /// </summary>
    /// <param name="currentProcess">当前需要重启的进程</param>
    /// <param name="spawnFunc">新进程生成函数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新启动的交互式进程</returns>
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

    /// <summary>
    /// 重置重启计数 — 将重启次数和最近重启时间清零
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _restartCount, 0);
        _lastRestartTime = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// 进程重启前事件参数 — 携带当前重启次数和旧进程 ID
/// </summary>
public sealed class ProcessRestartingEventArgs : EventArgs
{
    /// <summary>当前重启次数（含本次）</summary>
    public required int RestartCount { get; init; }
    /// <summary>旧进程 ID</summary>
    public required int OldProcessId { get; init; }
}

/// <summary>
/// 进程重启后事件参数 — 携带当前重启次数和新进程 ID
/// </summary>
public sealed class ProcessRestartedEventArgs : EventArgs
{
    /// <summary>当前重启次数（含本次）</summary>
    public required int RestartCount { get; init; }
    /// <summary>新进程 ID</summary>
    public required int NewProcessId { get; init; }
}
