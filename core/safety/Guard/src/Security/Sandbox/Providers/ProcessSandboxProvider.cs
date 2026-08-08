namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;
using Infrastructure.Windows.JobObject;

[Register]
public sealed partial class ProcessSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;
    private readonly ConcurrentDictionary<string, WindowsJobObjectSandbox> _jobObjects = new();

    public override SandboxType SandboxType => SandboxType.Process;
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.ProcessIsolation | SandboxCapabilities.TimeLimit | SandboxCapabilities.MemoryLimit;

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

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = effectiveWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        foreach (var (key, value) in env)
        {
            psi.EnvironmentVariables[key] = value;
        }

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"[GRD016] 无法启动沙箱进程");

        if (OperatingSystem.IsWindows() && _jobObjects.TryGetValue(sandboxId, out var jobObject))
        {
            if (!jobObject.AssignProcess(process.Id))
            {
                Logger?.LogWarning("[Sandbox:Process] 将进程 {Pid} 分配到 JobObject 失败 - 沙箱: {Id}", process.Id, sandboxId);
            }
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "[Sandbox:Process] 终止超时进程失败 - Pid: {Pid}", process.Id);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ProviderExecutionResult
            {
                StandardOutput = stdout,
                StandardError = stderr,
                ExitCode = -1,
                Success = false,
                TimedOut = true
            };
        }

        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);

        return new ProviderExecutionResult
        {
            StandardOutput = standardOutput,
            StandardError = standardError,
            ExitCode = process.ExitCode,
            Success = process.ExitCode == 0,
            TimedOut = false
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

    internal bool HasJobObject(string sandboxId) => _jobObjects.ContainsKey(sandboxId);

    public bool TryAssignProcessToJobObject(string sandboxId, int processId)
    {
        if (_jobObjects.TryGetValue(sandboxId, out var jobObject))
        {
            return jobObject.AssignProcess(processId);
        }
        return false;
    }

    public override Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct)
    {
        return ExecuteInSandboxAsync(sandboxId, command, workingDirectory, timeoutMs, ct)
            .ContinueWith(t => (ProviderExecutionResult?)t.Result, ct);
    }
}
