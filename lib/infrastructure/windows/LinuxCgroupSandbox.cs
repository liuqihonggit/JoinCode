namespace Infrastructure.Windows.JobObject;

// cgroup 是 Linux 内核虚拟文件系统，用 SafeFileIO 统一 FileShare.ReadWrite 避免并发冲突
// Directory.*/File.Exists 保留直接调用（cgroup 是内核接口，不适合 IFileSystem 抽象）
#pragma warning disable JCC9001, JCC9002

/// <summary>
/// Linux cgroup 沙箱 — 通过 cgroup v2 限制子进程内存与进程数，并在释放时终止全部子进程
/// </summary>
public sealed class LinuxCgroupSandbox : IAsyncDisposable
{
    private readonly ILogger? _logger;
    private string? _cgroupPath;
    private bool _ownsCgroup;

    /// <summary>
    /// 构造 Linux cgroup 沙箱
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public LinuxCgroupSandbox(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 创建 cgroup 并可选设置内存与进程数限制
    /// </summary>
    /// <param name="name">cgroup 名称，省略时按进程 ID 自动生成</param>
    /// <param name="memoryLimitBytes">内存上限（字节），null 或 &lt;=0 表示不限制</param>
    /// <param name="pidsMax">进程数上限，null 或 &lt;=0 表示不限制</param>
    /// <returns>创建成功返回 true；非 Linux 或无可写 cgroup 路径返回 false</returns>
    public bool CreateCgroup(string? name = null, long? memoryLimitBytes = null, int? pidsMax = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var cgroupName = name ?? $"jcc-sat-{Environment.ProcessId}";
        var basePath = FindWritableCgroupPath();
        if (basePath is null)
        {
            _logger?.LogWarning("[LinuxCgroup] 找不到可写的 cgroup 路径");
            return false;
        }

        _cgroupPath = Path.Combine(basePath, cgroupName);

        try
        {
            Directory.CreateDirectory(_cgroupPath);
            _ownsCgroup = true;

            if (memoryLimitBytes.HasValue && memoryLimitBytes.Value > 0)
            {
                SafeFileIO.WriteAllText(Path.Combine(_cgroupPath, "memory.max"), memoryLimitBytes.Value.ToString());
            }

            if (pidsMax.HasValue && pidsMax.Value > 0)
            {
                SafeFileIO.WriteAllText(Path.Combine(_cgroupPath, "pids.max"), pidsMax.Value.ToString());
            }

            _logger?.LogInformation("[LinuxCgroup] cgroup 已创建: {Path}", _cgroupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LinuxCgroup] 创建 cgroup 失败: {Path}", _cgroupPath);
            _cgroupPath = null;
            _ownsCgroup = false;
            return false;
        }
    }

    /// <summary>
    /// 将指定进程加入当前 cgroup
    /// </summary>
    /// <param name="processId">进程 ID</param>
    /// <returns>加入成功返回 true；非 Linux 或未创建 cgroup 返回 false</returns>
    public bool AssignProcess(int processId)
    {
        if (!OperatingSystem.IsLinux() || _cgroupPath is null)
        {
            return false;
        }

        try
        {
            SafeFileIO.WriteAllText(Path.Combine(_cgroupPath, "cgroup.procs"), processId.ToString());
            _logger?.LogInformation("[LinuxCgroup] 进程 {Pid} 已加入 cgroup", processId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LinuxCgroup] 将进程 {Pid} 加入 cgroup 失败", processId);
            return false;
        }
    }

    /// <summary>
    /// 终止当前 cgroup 中的所有进程
    /// </summary>
    /// <returns>终止成功返回 true；非 Linux 或未创建 cgroup 返回 false</returns>
    public bool KillAllProcesses()
    {
        if (!OperatingSystem.IsLinux() || _cgroupPath is null)
        {
            return false;
        }

        try
        {
            var killPath = Path.Combine(_cgroupPath, "cgroup.kill");
            if (File.Exists(killPath))
            {
                SafeFileIO.WriteAllText(killPath, "1");
                _logger?.LogInformation("[LinuxCgroup] 已通过 cgroup.kill 终止所有进程");
                return true;
            }

            var procsPath = Path.Combine(_cgroupPath, "cgroup.procs");
            if (File.Exists(procsPath))
            {
                var pidsText = SafeFileIO.ReadAllText(procsPath).Trim();
                if (pidsText.Length > 0)
                {
                    foreach (var pidStr in pidsText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(pidStr, out var pid) && pid != Environment.ProcessId)
                        {
                            try { System.Diagnostics.Process.GetProcessById(pid).Kill(); }
                            catch (Exception killEx) { _logger?.LogDebug(killEx, "[LinuxCgroup] 终止进程 {Pid} 失败，可能已退出", pid); }
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[LinuxCgroup] 终止 cgroup 进程失败");
            return false;
        }
    }

    private static string? FindWritableCgroupPath()
    {
        var candidates = new[]
        {
            "/sys/fs/cgroup",
            "/sys/fs/cgroup/user.slice"
        };

        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    var testPath = Path.Combine(path, $"jcc-probe-{Environment.ProcessId}");
                    Directory.CreateDirectory(testPath);
                    Directory.Delete(testPath, false);
                    return path;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[LinuxCgroup] cgroup 路径 {path} 不可写: {ex.Message}");
                    continue;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 异步释放资源 — 终止 cgroup 内所有进程并删除 cgroup 目录
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        if (_cgroupPath is not null && _ownsCgroup)
        {
            KillAllProcesses();

            try
            {
                if (Directory.Exists(_cgroupPath))
                {
                    Directory.Delete(_cgroupPath, false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[LinuxCgroup] 删除 cgroup 目录失败: {Path}", _cgroupPath);
            }

            _cgroupPath = null;
            _ownsCgroup = false;
        }

        return ValueTask.CompletedTask;
    }
}
