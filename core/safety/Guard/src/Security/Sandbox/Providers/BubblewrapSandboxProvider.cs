namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;

[Register(typeof(SandboxProviderBase), ServiceLifetime.Singleton)]
public sealed partial class BubblewrapSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;

    public override SandboxType SandboxType => SandboxType.Bubblewrap;
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.NetworkIsolation | SandboxCapabilities.ProcessIsolation | SandboxCapabilities.UserNamespace;

    public BubblewrapSandboxProvider(
        IFileSystem fs,
        IProcessService processService,
        ILogger<BubblewrapSandboxProvider>? logger = null,
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
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Fs.FileExists(Path.Combine(dir, "bwrap")))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<ProviderExecutionResult> ExecuteInSandboxAsync(
        string sandboxId,
        string command,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        var info = GetSandboxInfo(sandboxId)
            ?? throw new InvalidOperationException($"[GRD012] Bubblewrap 沙箱 '{sandboxId}' 不存在");

        var rootPath = Path.GetFullPath(info.RootPath);

        var bwrapArgs = new List<string> { "--unshare-all", "--die-with-parent" };

        if (!info.RestrictNetwork)
        {
            bwrapArgs.Add("--share-net");
        }

        bwrapArgs.AddRange(new[] { "--bind", rootPath, "/workspace" });
        bwrapArgs.AddRange(new[] { "--proc", "/proc" });
        bwrapArgs.AddRange(new[] { "--dev", "/dev" });
        bwrapArgs.AddRange(new[] { "--tmpfs", "/tmp" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/usr", "/usr" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/lib", "/lib" });
        if (Fs.DirectoryExists("/lib64"))
            bwrapArgs.AddRange(new[] { "--ro-bind", "/lib64", "/lib64" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/bin", "/bin" });
        if (Fs.DirectoryExists("/sbin"))
            bwrapArgs.AddRange(new[] { "--ro-bind", "/sbin", "/sbin" });

        if (info.AllowedPaths is not null)
        {
            foreach (var allowed in info.AllowedPaths)
            {
                var fullAllowed = Path.GetFullPath(allowed);
                if (Fs.DirectoryExists(fullAllowed))
                {
                    bwrapArgs.AddRange(new[] { "--bind", fullAllowed, fullAllowed });
                }
                else if (Fs.FileExists(fullAllowed))
                {
                    bwrapArgs.AddRange(new[] { "--ro-bind", fullAllowed, fullAllowed });
                }
            }
        }

        bwrapArgs.AddRange(new[] { "--", "/bin/sh", "-c", ShellCommandEscape.EscapeForSingleQuotedShell(command) });

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "bwrap",
            ArgumentList = bwrapArgs,
            TimeoutMs = timeoutMs
        }, ct).ConfigureAwait(false);

        return new ProviderExecutionResult
        {
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ExitCode = result.ExitCode,
            Success = result.Success,
            TimedOut = !result.Success && result.ExitCode == -1
        };
    }
}
