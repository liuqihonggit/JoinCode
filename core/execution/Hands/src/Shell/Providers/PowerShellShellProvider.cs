namespace Services.Shell.Providers;

/// <summary>
/// PowerShell 能力描述提供者 — DI 单例，长命缓存
/// </summary>
[Register]
public sealed class PowerShellCapabilityProvider : ShellCapabilityProvider
{
    public const string PowerShellPathEnvVar = "JCC_POWERSHELL_PATH";

    protected override ShellType GetShellType() => ShellType.PowerShell;

    protected override string ResolveShellPath(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(PowerShellPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        var psi = new ProcessStartInfo
        {
            FileName = "where.exe",
            Arguments = "pwsh.exe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (p.ExitCode == 0)
                {
                    var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    if (paths.Length > 0) return paths[0].Trim();
                }
            }
        }
        catch (Exception ex) { logger?.LogDebug(ex, "where.exe pwsh.exe failed"); }

        var commonPath = @"C:\Program Files\PowerShell\7\pwsh.exe";
        if (fs.FileExists(commonPath)) return commonPath;

        logger?.LogWarning("PowerShell not found, falling back to powershell.exe. Set {EnvVar} to specify path.", PowerShellPathEnvVar);
        return "powershell.exe";
    }

    protected override string DetectVersion(string shellPath, ILogger? logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var version = output?.Trim();
            return string.IsNullOrEmpty(version) ? "unknown" : version;
        }
        catch { return "unknown"; }
    }

    protected override bool DetectIsPowerShellCore(string shellPath, string version)
        => shellPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
        || version.StartsWith('7') || version.StartsWith('6');

    protected override string BuildDisplayName(string shellPath, string version)
        => DetectIsPowerShellCore(shellPath, version)
            ? $"PowerShell Core {version}"
            : $"PowerShell Desktop {version}";
}

/// <summary>
/// PowerShell Shell 执行器 — 短命 Entity，每次命令执行创建
/// </summary>
public sealed class PowerShellShellProvider : ShellProviderBase
{
    private string? _currentSandboxTmpDir;

    public bool IsCore => Capability.IsPowerShellCore;

    public PowerShellShellProvider(
        ShellCapability capability,
        IFileSystem fs,
        ILogger? logger = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(capability, fs, logger, toolUseId, spanId) { }

    /// <inheritdoc />
    public override Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command, ShellExecOptions options, CancellationToken cancellationToken = default)
    {
        _currentSandboxTmpDir = options.UseSandbox ? options.SandboxTmpDir : null;

        var cwdFilePath = options.UseSandbox && options.SandboxTmpDir is not null
            ? Path.Combine(options.SandboxTmpDir, $"jcc-pwd-ps-{options.SessionId}")
            : Path.Combine(Path.GetTempPath(), $"jcc-pwd-ps-{options.SessionId}");

        var escapedCwdFilePath = cwdFilePath.Replace("'", "''");

        var cwdTracking = $"\n; $_ec = if ($null -ne $LASTEXITCODE) {{ $LASTEXITCODE }} elseif ($?) {{ 0 }} else {{ 1 }}"
            + $"\n; (Get-Location).Path | Out-File -FilePath '{escapedCwdFilePath}' -Encoding utf8 -NoNewline"
            + "\n; exit $_ec";

        var psCommand = command + cwdTracking;

        var commandString = options.UseSandbox
            ? BuildSandboxEncodedCommand(psCommand)
            : psCommand;

        Logger?.LogDebug("PowerShellShellProvider: built command for session {SessionId}, sandbox={UseSandbox}",
            options.SessionId, options.UseSandbox);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public override string[] GetSpawnArgs(string commandString)
        => ["-NoProfile", "-NonInteractive", "-Command", commandString];

    /// <inheritdoc />
    protected override void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command)
    {
        if (_currentSandboxTmpDir is not null)
        {
            env["TMPDIR"] = _currentSandboxTmpDir;
            env["JCC_TMPDIR"] = _currentSandboxTmpDir;
        }
    }

    private string BuildSandboxEncodedCommand(string psCommand)
    {
        var encoded = EncodePowerShellCommand(psCommand);
        var escapedPath = ShellPath.Replace("'", "'\\''");
        return $"'{escapedPath}' -NoProfile -NonInteractive -EncodedCommand {encoded}";
    }

    internal static string EncodePowerShellCommand(string psCommand)
    {
        var utf16LeBytes = Encoding.Unicode.GetBytes(psCommand);
        return Convert.ToBase64String(utf16LeBytes);
    }
}
