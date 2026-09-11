namespace Infrastructure.Shell;

/// <summary>
/// Shell 进程看护命令 — Actor 消息类型
/// </summary>
public interface IShellWatchdogCommand;

public sealed record ShellRegisterCmd(int ProcessId, Action<int> OnProcessDied) : IShellWatchdogCommand;

public sealed record ShellUnregisterCmd(int ProcessId) : IShellWatchdogCommand;

public sealed record ShellCheckAllTickCmd : IShellWatchdogCommand;

public sealed record ShellSystemResumedCmd : IShellWatchdogCommand;

/// <summary>
/// Shell 进程看护服务 — Actor 化：Consumer 线程独占 _callbacks，消除 ConcurrentDictionary。
/// <para>Timer 周期检查改为 TrySend(ShellCheckAllTickCmd) 自消息，Consumer 串行处理。</para>
/// <para>系统唤醒通知改为 TrySend(ShellSystemResumedCmd)，Consumer 内部延迟 2s 后检查。</para>
/// </summary>
[Register(typeof(IShellProcessWatchdog), ServiceLifetime.Singleton)]
public sealed class ShellProcessWatchdog : ActorBase<IShellWatchdogCommand, Unit>, IShellProcessWatchdog
{
    private readonly Timer _timer;
    private readonly ILogger? _logger;
    private int _disposed;

    private readonly Dictionary<int, Action<int>> _callbacks = new();

    public ShellProcessWatchdog(ILogger? logger = null)
        : base()
    {
        _logger = logger;
        _timer = new Timer(_ => TrySend(new ShellCheckAllTickCmd()), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Register(int processId, Action<int> onProcessDied)
    {
        ArgumentNullException.ThrowIfNull(onProcessDied);
        TrySend(new ShellRegisterCmd(processId, onProcessDied));
    }

    public void Unregister(int processId)
    {
        TrySend(new ShellUnregisterCmd(processId));
    }

    /// <summary>
    /// 通知系统从睡眠中恢复 — 由外部调用（如 Windows SystemEvents 或平台特定代码）
    /// </summary>
    public void NotifySystemResumed()
    {
        TrySend(new ShellSystemResumedCmd());
    }

    protected override async ValueTask HandleAsync(IShellWatchdogCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case ShellRegisterCmd reg:
                _callbacks[reg.ProcessId] = reg.OnProcessDied;
                break;
            case ShellUnregisterCmd unreg:
                _callbacks.Remove(unreg.ProcessId);
                break;
            case ShellCheckAllTickCmd:
                CheckAllProcesses();
                break;
            case ShellSystemResumedCmd:
                await Task.Delay(2000, ct).ConfigureAwait(false);
                CheckAllProcesses();
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "[ShellWatchdog] 消费者异常");
    }

    private void CheckAllProcesses()
    {
        var deadPids = new List<int>();
        foreach (var (pid, callback) in _callbacks)
        {
            if (!IsProcessAlive(pid))
            {
                deadPids.Add(pid);
                callback(pid);
            }
        }
        foreach (var pid in deadPids)
        {
            _callbacks.Remove(pid);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        try
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ShellWatchdog] Dispose 超时");
        }
    }
}
