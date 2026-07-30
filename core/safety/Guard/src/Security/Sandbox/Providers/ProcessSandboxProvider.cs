namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;

[Register]
public sealed partial class ProcessSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;

    public override SandboxType SandboxType => SandboxType.Process;
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.ProcessIsolation | SandboxCapabilities.TimeLimit;

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

    public async Task<SandboxExecutionResult> ExecuteInSandboxAsync(
        string sandboxId,
        string command,
        string? workingDirectory = null,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        var info = GetSandboxInfo(sandboxId)
            ?? throw new InvalidOperationException($"沙箱 '{sandboxId}' 不存在");

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

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c {command}",
            WorkingDirectory = effectiveWorkingDir,
            TimeoutMs = timeoutMs,
            EnvironmentVariables = env
        }, ct).ConfigureAwait(false);

        return new SandboxExecutionResult
        {
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ExitCode = result.ExitCode,
            Success = result.Success,
            TimedOut = !result.Success && result.ExitCode == -1
        };
    }

    private bool CheckLinuxSandboxSupport()
    {
        try
        {
            return Fs.FileExists("/proc/self/status");
        }
        catch
        {
            return false;
        }
    }
}

public sealed partial class SandboxExecutionResult
{
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required int ExitCode { get; init; }
    public required bool Success { get; init; }
    public required bool TimedOut { get; init; }
}
