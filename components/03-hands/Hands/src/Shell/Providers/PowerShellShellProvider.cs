namespace Services.Shell.Providers;

/// <summary>
/// PowerShell Shell 提供者 — 对齐 TS PowerShellProvider
/// 支持 CWD 追踪、退出码捕获、EncodedCommand（沙箱模式）
/// 自动检测 PowerShell 版本（Desktop 5.1 vs Core 7+）并路由到最佳路径
/// </summary>
[Register]
public sealed class PowerShellShellProvider : ShellProviderBase
{
    private string? _currentSandboxTmpDir;

    public override ShellProviderType Type => ShellProviderType.PowerShell;
    public override bool Detached => false;

    /// <summary>
    /// 是否为 PowerShell Core (7+) — 对齐 TS powershellDetection isPwsh
    /// </summary>
    public bool IsCore { get; }

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_POWERSHELL_PATH
    /// </summary>
    public const string PowerShellPathEnvVar = "JCC_POWERSHELL_PATH";

    public PowerShellShellProvider(IFileSystem fs, string? shellPath = null, ILogger? logger = null)
        : base(fs, shellPath, logger)
    {
        IsCore = DetectIsCore();
    }

    /// <inheritdoc />
    protected override string ResolveShellPath()
    {
        var envPath = ResolveFromEnvVar(PowerShellPathEnvVar);
        if (envPath is not null) return envPath;

        var pwshFromPath = FindExecutable("pwsh.exe", excludeCurrentDir: false);
        if (pwshFromPath is not null) return pwshFromPath;

        var commonPath = FindInCommonPaths(@"C:\Program Files\PowerShell\7\pwsh.exe");
        if (commonPath is not null) return commonPath;

        return "powershell.exe";
    }

    /// <inheritdoc />
    protected override string DetectVersion()
    {
        var output = ExecuteShellCommand(
            ShellPath,
            "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"");

        var version = output?.Trim();
        return string.IsNullOrEmpty(version) ? "unknown" : version;
    }

    /// <summary>
    /// 检测是否为 PowerShell Core — 基于 ShellPath 或版本号判断
    /// </summary>
    private bool DetectIsCore()
    {
        if (ShellPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Version.StartsWith('7') || Version.StartsWith('6'))
            return true;

        return false;
    }

    /// <inheritdoc />
    public override Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command,
        ShellExecOptions options,
        CancellationToken cancellationToken = default)
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

    /// <summary>
    /// 沙箱模式下构建 EncodedCommand — 对齐 TS encodePowerShellCommand
    /// 使用 base64 UTF-16LE 编码避免引号损坏
    /// </summary>
    private string BuildSandboxEncodedCommand(string psCommand)
    {
        var encoded = EncodePowerShellCommand(psCommand);
        var escapedPath = ShellPath.Replace("'", "'\\''");
        return $"'{escapedPath}' -NoProfile -NonInteractive -EncodedCommand {encoded}";
    }

    /// <summary>
    /// Base64 UTF-16LE 编码 — 对齐 TS encodePowerShellCommand
    /// </summary>
    internal static string EncodePowerShellCommand(string psCommand)
    {
        var utf16LeBytes = Encoding.Unicode.GetBytes(psCommand);
        return Convert.ToBase64String(utf16LeBytes);
    }
}
