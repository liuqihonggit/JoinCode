namespace Infrastructure.Shell;

/// <summary>
/// Shell 进程看护命令 — Actor 消息类型
/// </summary>
public interface IShellWatchdogCommand;

/// <summary>
/// 注册进程看护命令 — 携带进程标识与死亡回调
/// </summary>
public sealed record ShellRegisterCmd(int ProcessId, Action<int> OnProcessDied) : IShellWatchdogCommand;

/// <summary>取消注册进程看护命令 — 携带进程标识</summary>
public sealed record ShellUnregisterCmd(int ProcessId) : IShellWatchdogCommand;

/// <summary>周期检查所有注册进程的时钟消息</summary>
public sealed record ShellCheckAllTickCmd : IShellWatchdogCommand;

/// <summary>系统从睡眠恢复通知消息</summary>
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

    /// <summary>
    /// 构造看护服务，启动 30 秒周期检查定时器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public ShellProcessWatchdog(ILogger? logger = null)
        : base()
    {
        _logger = logger;
        _timer = new Timer(_ => TrySend(new ShellCheckAllTickCmd()), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 注册进程看护 — 当进程死亡时调用指定回调
    /// </summary>
    /// <param name="processId">待看护进程的系统标识符</param>
    /// <param name="onProcessDied">进程死亡时调用的回调，参数为进程标识</param>
    public void Register(int processId, Action<int> onProcessDied)
    {
        ArgumentNullException.ThrowIfNull(onProcessDied);
        TrySend(new ShellRegisterCmd(processId, onProcessDied));
    }

    /// <summary>
    /// 取消注册进程看护 — 移除对应进程的死亡回调
    /// </summary>
    /// <param name="processId">待取消看护进程的系统标识符</param>
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

    /// <summary>处理 Shell 进程监控命令</summary>
    /// <param name="command">监控命令</param>
    /// <param name="ct">取消令牌</param>
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

    /// <summary>消费者异常回调 — 记录日志</summary>
    /// <param name="ex">异常对象</param>
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

    /// <summary>
    /// 异步释放资源 — 停止定时器并等待 Actor 队列排空
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
