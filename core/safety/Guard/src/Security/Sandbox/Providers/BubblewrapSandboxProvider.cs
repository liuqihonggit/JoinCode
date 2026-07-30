namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;

[Register]
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
                var result = _processService.ExecuteAsync(new ProcessOptions
                {
                    FileName = "bwrap",
                    Arguments = "--version",
                    TimeoutMs = 3000
                }, CancellationToken.None).GetAwaiter().GetResult();

                return result.Success;
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
            ?? throw new InvalidOperationException($"Bubblewrap 沙箱 '{sandboxId}' 不存在");

        var rootPath = Path.GetFullPath(info.RootPath);

        var bwrapArgs = new StringBuilder(512);
        bwrapArgs.Append("--unshare-all");
        bwrapArgs.Append(" --die-with-parent");

        if (!info.RestrictNetwork)
        {
            bwrapArgs.Append(" --share-net");
        }

        bwrapArgs.Append($" --bind {rootPath} /workspace");
        bwrapArgs.Append(" --proc /proc");
        bwrapArgs.Append(" --dev /dev");
        bwrapArgs.Append(" --tmpfs /tmp");
        bwrapArgs.Append(" --ro-bind /usr /usr");
        bwrapArgs.Append(" --ro-bind /lib /lib");
        bwrapArgs.Append(" --ro-bind /lib64 /lib64 2>/dev/null");
        bwrapArgs.Append(" --ro-bind /bin /bin");
        bwrapArgs.Append(" --ro-bind /sbin /sbin 2>/dev/null");

        if (info.AllowedPaths is not null)
        {
            foreach (var allowed in info.AllowedPaths)
            {
                var fullAllowed = Path.GetFullPath(allowed);
                if (Fs.DirectoryExists(fullAllowed))
                {
                    bwrapArgs.Append($" --bind {fullAllowed} {fullAllowed}");
                }
                else if (Fs.FileExists(fullAllowed))
                {
                    bwrapArgs.Append($" --ro-bind {fullAllowed} {fullAllowed}");
                }
            }
        }

        bwrapArgs.Append($" -- /bin/sh -c {ShellCommandEscape.EscapeForSingleQuotedShell(command)}");

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "bwrap",
            Arguments = bwrapArgs.ToString(),
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
