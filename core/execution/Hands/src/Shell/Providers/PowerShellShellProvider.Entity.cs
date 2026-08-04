namespace Services.Shell.Providers;

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
