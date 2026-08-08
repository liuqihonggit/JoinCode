namespace Services.SystemActuator;

/// <summary>
/// PowerShell 系统执行器 — 合并原 PowerShellProvider + PowerShellCapabilityProvider
/// </summary>
public sealed class PowerShellSystemActuator : SystemActuatorBase
{
    public const string PowerShellPathEnvVar = "JCC_POWERSHELL_PATH";

    private string? _currentSandboxTmpDir;

    public bool IsCore => Capability.IsPowerShellCore;

    public PowerShellSystemActuator(
        IFileSystem fs,
        ILogger? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(SystemActuatorKind.PowerShell, fs, logger, sandboxManager, preventSleepService, config, toolUseId, spanId) { }

    /// <summary>
    /// 检测 PowerShell 能力并注册到基类静态缓存
    /// </summary>
    public static SystemActuatorCapability CreateCapability(IFileSystem fs, ILogger? logger = null)
    {
        var shellPath = ResolveShellPathStatic(fs, logger);
        var version = DetectVersionStatic(shellPath, logger);
        var isCore = shellPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith('7') || version.StartsWith('6');
        var displayName = isCore ? $"PowerShell Core {version}" : $"PowerShell Desktop {version}";

        var capability = new SystemActuatorCapability
        {
            Kind = SystemActuatorKind.PowerShell,
            ShellPath = shellPath,
            Version = version,
            DisplayName = displayName,
            Detached = false,
            IsPowerShellCore = isCore,
        };

        SystemActuatorBase.RegisterCapability(capability);
        return capability;
    }

    private static string ResolveShellPathStatic(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(PowerShellPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("pwsh.exe");
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

    private static string DetectVersionStatic(string shellPath, ILogger? logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var version = output?.Trim();
            return string.IsNullOrEmpty(version) ? "unknown" : version;
        }
        catch { return "unknown"; }
    }

    /// <inheritdoc />
    public override Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken cancellationToken = default)
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

        Logger?.LogDebug("PowerShellSystemActuator: built command for session {SessionId}, sandbox={UseSandbox}",
            options.SessionId, options.UseSandbox);

        return Task.FromResult(new SystemActuatorExecCommandResult
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
