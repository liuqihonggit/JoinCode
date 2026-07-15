namespace Services.Shell.Providers;

/// <summary>
/// PowerShell Shell 提供者 — 对齐 TS PowerShellProvider
/// 支持 CWD 追踪、退出码捕获、EncodedCommand（沙箱模式）
/// 自动检测 PowerShell 版本（Desktop 5.1 vs Core 7+）并路由到最佳路径
/// </summary>
[Register]
public sealed class PowerShellShellProvider : IShellProvider
{
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private string? _currentSandboxTmpDir;

    public ShellProviderType Type => ShellProviderType.PowerShell;
    public string ShellPath { get; }
    public bool Detached => false;
    public string Version { get; }

    /// <summary>
    /// 是否为 PowerShell Core (7+) — 对齐 TS powershellDetection isPwsh
    /// </summary>
    public bool IsCore { get; }

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_POWERSHELL_PATH
    /// </summary>
    public const string PowerShellPathEnvVar = "JCC_POWERSHELL_PATH";

    public PowerShellShellProvider(IFileSystem fs, string? shellPath = null, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        (ShellPath, Version, IsCore) = ResolvePowerShell(shellPath);
    }

    /// <inheritdoc />
    public Task<ShellExecCommandResult> BuildExecCommandAsync(
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

        _logger?.LogDebug("PowerShellShellProvider: built command for session {SessionId}, sandbox={UseSandbox}",
            options.SessionId, options.UseSandbox);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public string[] GetSpawnArgs(string commandString)
        => ["-NoProfile", "-NonInteractive", "-Command", commandString];

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_currentSandboxTmpDir is not null)
        {
            env["TMPDIR"] = _currentSandboxTmpDir;
            env["JCC_TMPDIR"] = _currentSandboxTmpDir;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
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

    /// <summary>
    /// 解析 PowerShell 路径和版本 — 对齐 TS powershellDetection
    /// 优先级: JCC_POWERSHELL_PATH → pwsh.exe (Core 7+) → powershell.exe (Desktop 5.1)
    /// 同时检测版本号，用于提示词注入和命令语法路由
    /// </summary>
    private (string Path, string Version, bool IsCore) ResolvePowerShell(string? shellPath)
    {
        var envPath = Environment.GetEnvironmentVariable(PowerShellPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && _fs.FileExists(envPath))
        {
            var (ver, isCore) = DetectVersion(envPath);
            return (envPath, ver, isCore);
        }

        var pwshPath = @"C:\Program Files\PowerShell\7\pwsh.exe";
        if (_fs.FileExists(pwshPath))
        {
            var (ver, _) = DetectVersion(pwshPath);
            return (pwshPath, ver, true);
        }

        var desktopVer = DetectVersion("powershell.exe");
        return ("powershell.exe", desktopVer.Version, desktopVer.IsCore);
    }

    /// <summary>
    /// 检测 PowerShell 版本 — 对齐 TS powershellDetection
    /// 通过 `$PSVersionTable.PSVersion` 获取精确版本号
    /// </summary>
    private static (string Version, bool IsCore) DetectVersion(string shellPath)
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

            using var process = Process.Start(psi);
            if (process is null) return ("unknown", false);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var version = output.Trim();
            if (string.IsNullOrEmpty(version)) return ("unknown", false);

            var isCore = shellPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
                || version.StartsWith('7') || version.StartsWith('6');

            return (version, isCore);
        }
        catch
        {
            return ("unknown", false);
        }
    }
}
