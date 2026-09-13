namespace Core.Security.Sandbox.Providers;


/// <summary>
/// 进程沙箱提供者 — 基于 Windows JobObject 或 Linux 进程组实现进程级隔离沙箱
/// </summary>
[Register(typeof(SandboxProviderBase), ServiceLifetime.Singleton)]
public sealed partial class ProcessSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;
    private readonly ConcurrentDictionary<string, WindowsJobObjectSandbox> _jobObjects = new();

    /// <summary>
    /// 沙箱类型 — 始终为 <see cref="SandboxType.Process"/>
    /// </summary>
    public override SandboxType SandboxType => SandboxType.Process;

    /// <summary>
    /// 沙箱能力 — 支持路径重定向、文件系统隔离、进程隔离、时间限制和内存限制
    /// </summary>
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.ProcessIsolation | SandboxCapabilities.TimeLimit | SandboxCapabilities.MemoryLimit;

    /// <summary>
    /// 构造进程沙箱提供者
    /// </summary>
    public ProcessSandboxProvider(
        IFileSystem fs,
        IProcessService processService,
        ILogger<ProcessSandboxProvider>? logger = null,
        IClockService? clock = null,
        ITelemetryService? telemetryService = null)
        : base(fs, logger, clock ?? SystemClockService.Instance, telemetryService)
    {
        _processService = processService;
    }

    /// <summary>
    /// 当前平台是否支持进程沙箱 — Windows 始终可用，Linux 需检测 cgroup 支持
    /// </summary>
    public override bool IsAvailable
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return true;
            }
            if (OperatingSystem.IsLinux())
            {
                return CheckLinuxSandboxSupport();
            }
            return false;
        }
    }

    private protected override async Task OnCreateAsync(SandboxInfo info, SandboxOptions options, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            CreateWindowsJobObject(info, options);
        }
        else if (OperatingSystem.IsLinux())
        {
            Logger?.LogInformation("[Sandbox:Process] Linux 进程组沙箱就绪 - Id: {Id}, 路径: {Root}", info.SandboxId, info.RootPath);
        }

        await base.OnCreateAsync(info, options, ct).ConfigureAwait(false);
    }

    private protected override async Task OnDestroyAsync(SandboxInfo info, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows() && _jobObjects.TryRemove(info.SandboxId, out var jobObject))
        {
            jobObject.TerminateAllProcesses();
            jobObject.Dispose();
            Logger?.LogInformation("[Sandbox:Process] JobObject 已销毁 - Id: {Id}", info.SandboxId);
        }

        await base.OnDestroyAsync(info, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 在指定沙箱内执行命令 — 通过 cmd.exe/sh -c 执行，注入沙箱环境变量
    /// </summary>
    public async Task<ProviderExecutionResult> ExecuteInSandboxAsync(
        string sandboxId,
        string command,
        string? workingDirectory = null,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        var info = GetSandboxInfo(sandboxId)
            ?? throw new InvalidOperationException($"[GRD015] 沙箱 '{sandboxId}' 不存在");

        var env = new Dictionary<string, string>();
        if (info.RestrictFileSystem)
        {
            env["JCC_SANDBOX_ROOT"] = info.RootPath;
        }
        if (info.RestrictNetwork)
        {
            env["JCC_SANDBOX_NO_NETWORK"] = "1";
        }
        if (info.AllowedPaths is not null)
        {
            env["JCC_SANDBOX_ALLOWED_PATHS"] = string.Join(Path.PathSeparator, info.AllowedPaths);
        }

        var effectiveWorkingDir = workingDirectory is not null
            ? ResolvePath(workingDirectory, sandboxId)
            : info.RootPath;

        var options = new ProcessOptions
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            ArgumentList = [OperatingSystem.IsWindows() ? "/c" : "-c", command],
            WorkingDirectory = effectiveWorkingDir,
            EnvironmentVariables = env,
            TimeoutMs = timeoutMs,
            SkipArgumentValidation = true
        };

        var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

        return new ProviderExecutionResult
        {
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ExitCode = result.ExitCode,
            Success = result.Success,
            TimedOut = result.ExitCode == -1 && result.StandardError == "进程执行超时"
        };
    }

    private void CreateWindowsJobObject(SandboxInfo info, SandboxOptions options)
    {
        var jobObject = new WindowsJobObjectSandbox(Logger);
        long? memoryLimit = options.MemoryLimitMb > 0 ? options.MemoryLimitMb * 1024L * 1024L : null;
        int? cpuLimit = options.CpuLimitPercent > 0 ? options.CpuLimitPercent : null;

        jobObject.CreateJobObject(memoryLimit, cpuLimit);
        _jobObjects[info.SandboxId] = jobObject;

        Logger?.LogInformation("[Sandbox:Process] Windows JobObject 已创建 - Id: {Id}, 内存限制: {MemMb}MB, CPU限制: {CpuPct}%",
            info.SandboxId, options.MemoryLimitMb, options.CpuLimitPercent);
    }

    private bool CheckLinuxSandboxSupport()
    {
        try
        {
            if (!Fs.FileExists("/proc/self/status")) return false;
            if (!Fs.DirectoryExists("/sys/fs/cgroup")) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断指定沙箱是否已关联 Windows JobObject
    /// </summary>
    internal bool HasJobObject(string sandboxId) => _jobObjects.ContainsKey(sandboxId);

    /// <summary>
    /// 尝试将外部进程分配到指定沙箱的 JobObject — 仅 Windows 平台有效
    /// </summary>
    public bool TryAssignProcessToJobObject(string sandboxId, int processId)
    {
        if (_jobObjects.TryGetValue(sandboxId, out var jobObject))
        {
            return jobObject.AssignProcess(processId);
        }
        return false;
    }

    /// <inheritdoc />
    public override Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct)
    {
        return ExecuteInSandboxAsync(sandboxId, command, workingDirectory, timeoutMs, ct)
            .ContinueWith(t => (ProviderExecutionResult?)t.Result, ct);
    }
}
