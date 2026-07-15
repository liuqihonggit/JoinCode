using System.Diagnostics;

namespace Infrastructure.Shell;

/// <summary>
/// Shell 进程看护服务 — 周期性检测僵尸进程
/// 在 Windows 上额外监听系统睡眠/唤醒事件，唤醒后立即检查
/// </summary>
[Register]
public sealed class ShellProcessWatchdog : IShellProcessWatchdog
{
    private readonly ConcurrentDictionary<int, Action<int>> _callbacks = new();
    private Timer? _healthCheckTimer;

    public ShellProcessWatchdog()
    {
        _healthCheckTimer = new Timer(CheckAllProcesses, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Register(int processId, Action<int> onProcessDied)
    {
        ArgumentNullException.ThrowIfNull(onProcessDied);
        _callbacks[processId] = onProcessDied;
    }

    public void Unregister(int processId)
    {
        _callbacks.TryRemove(processId, out _);
    }

    /// <summary>
    /// 通知系统从睡眠中恢复 — 由外部调用（如 Windows SystemEvents 或平台特定代码）
    /// </summary>
    public void NotifySystemResumed()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            CheckAllProcesses(null);
        });
    }

    private void CheckAllProcesses(object? state)
    {
        foreach (var (pid, callback) in _callbacks)
        {
            if (!IsProcessAlive(pid))
            {
                _callbacks.TryRemove(pid, out _);
                callback(pid);
            }
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
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;
        _callbacks.Clear();
    }
}
