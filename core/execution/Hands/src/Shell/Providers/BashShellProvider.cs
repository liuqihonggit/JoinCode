namespace Services.Shell.Providers;

/// <summary>
/// Bash 能力描述提供者 — DI 单例，长命缓存
/// 版本/路径只检测一次，后续调用返回缓存
/// </summary>
[Register]
public sealed class BashCapabilityProvider : ShellCapabilityProvider
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    public BashCapabilityProvider(IFileSystem fs, IEnvironmentProbeService? probeService = null, ILogger<BashCapabilityProvider>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    protected override ShellType GetShellType() => ShellType.Bash;
    protected override bool IsDetached() => true;

    protected override string ResolveShellPath(IFileSystem fs, ILogger? logger)
    {
        var envPath = ResolveFromEnvVarShared(fs, BashShellProvider.GitBashPathEnvVar);
        if (envPath is not null) return envPath;

        var gitPath = FindExecutableShared(fs, logger, "git.exe", excludeCurrentDir: true);
        if (gitPath is not null)
        {
            var bashFromGit = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(gitPath))!,
                "bin", "bash.exe");
            if (fs.FileExists(bashFromGit))
                return bashFromGit;
        }

        var commonPath = FindInCommonPathsShared(fs,
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe");
        if (commonPath is not null) return commonPath;

        logger?.LogWarning("Git Bash not found, falling back to cmd.exe. Set {EnvVar} to specify bash path.", BashShellProvider.GitBashPathEnvVar);
        return "cmd.exe";
    }

    protected override string DetectVersion(string shellPath, ILogger? logger)
    {
        if (shellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
            return "cmd-fallback";

        var output = ExecuteShellCommandShared(shellPath, "--version");
        if (output is null) return "unknown";

        var match = Regex.Match(output, @"version\s+(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    protected override string BuildDisplayName(string shellPath, string version)
        => shellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            ? "CMD (Bash fallback)"
            : $"Git Bash {version}";

    #region 共享工具方法（避免实例化 ShellProviderBase 才能调用）

    private static string? ResolveFromEnvVarShared(IFileSystem fs, string envVarName)
    {
        var envPath = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath))
            return envPath;
        return null;
    }

    private static string? FindExecutableShared(IFileSystem fs, ILogger? logger, string executable, bool excludeCurrentDir = true)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = executable,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0) return null;

            var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (!excludeCurrentDir)
                return paths.Length > 0 ? paths[0].Trim() : null;

            var cwd = fs.GetCurrentDirectory().ToLowerInvariant();
            foreach (var candidate in paths)
            {
                var normalized = Path.GetFullPath(candidate.Trim()).ToLowerInvariant();
                var dir = Path.GetDirectoryName(normalized)!;
                if (!dir.Equals(cwd, StringComparison.OrdinalIgnoreCase) &&
                    !normalized.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return candidate.Trim();
            }
            return null;
        }
        catch { return null; }
    }

    private static string? FindInCommonPathsShared(IFileSystem fs, params string[] paths)
    {
        foreach (var p in paths)
            if (fs.FileExists(p)) return p;
        return null;
    }

    private static string? ExecuteShellCommandShared(string fileName, string arguments, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(timeoutMs);
            return process.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    #endregion
}

/// <summary>
/// Bash Shell 执行器 — 短命 Entity，每次命令执行创建
/// 引用 BashCapabilityProvider 提供的 ShellCapability
/// </summary>
public sealed class BashShellProvider : ShellProviderBase
{
    private readonly IEnvironmentProbeService? _probeService;
    private string? _snapshotFilePath;

    public const string GitBashPathEnvVar = "JCC_GIT_BASH_PATH";
    public const string ShellPrefixEnvVar = "JCC_SHELL_PREFIX";

    private static readonly string SnapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppDataConstants.AppDataFolder, "shell-snapshots");

    private const int MaxSnapshotCount = 200;

    public BashShellProvider(
        ShellCapability capability,
        IFileSystem fs,
        IEnvironmentProbeService? probeService = null,
        ILogger? logger = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(capability, fs, logger, toolUseId, spanId)
    {
        _probeService = probeService;
        _snapshotFilePath = TryCreateSnapshot(fs, logger);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        CleanupSnapshot();
    }

    private void CleanupSnapshot()
    {
        if (_snapshotFilePath is null) return;
        try
        {
            if (Fs.FileExists(_snapshotFilePath))
            {
                Fs.DeleteFile(_snapshotFilePath);
                Logger?.LogDebug("已清理当前会话快照: {Path}", _snapshotFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "清理当前会话快照失败: {Path}", _snapshotFilePath);
        }
    }

    /// <inheritdoc />
    public override Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command, ShellExecOptions options, CancellationToken cancellationToken = default)
    {
        var tmpDir = Path.GetTempPath();
        var shellTmpDir = ConvertToPosixPath(tmpDir);

        var shellCwdFilePath = options.UseSandbox && options.SandboxTmpDir is not null
            ? PosixJoin(options.SandboxTmpDir, $"cwd-{options.SessionId}")
            : PosixJoin(shellTmpDir, $"jcc-{options.SessionId}-cwd");

        var cwdFilePath = options.UseSandbox && options.SandboxTmpDir is not null
            ? PosixJoin(options.SandboxTmpDir, $"cwd-{options.SessionId}")
            : Path.Combine(tmpDir, $"jcc-{options.SessionId}-cwd");

        var normalizedCommand = RewriteWindowsNullRedirect(command);

        var commandParts = new List<string>(5);

        if (_snapshotFilePath is not null && Fs.FileExists(_snapshotFilePath))
        {
            var posixSnapshotPath = ConvertToPosixPath(_snapshotFilePath);
            commandParts.Add($"source {ShellQuote(posixSnapshotPath)} 2>/dev/null || true");
        }

        var disableExtglobCmd = GetDisableExtglobCommand();
        if (disableExtglobCmd is not null)
            commandParts.Add(disableExtglobCmd);

        commandParts.Add($"eval {ShellQuote(normalizedCommand)}");
        commandParts.Add($"pwd -P >| {ShellQuote(shellCwdFilePath)}");

        var commandString = string.Join(" && ", commandParts);

        var shellPrefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(shellPrefix))
            commandString = $"{shellPrefix} {ShellQuote(commandString)}";

        Logger?.LogDebug("BashShellProvider: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public override string[] GetSpawnArgs(string commandString)
        => _snapshotFilePath is not null ? ["-c", commandString] : ["-c", "-l", commandString];

    /// <inheritdoc />
    protected override void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command)
    {
        if (Environment.GetEnvironmentVariable("SHELL") is null)
            env["SHELL"] = ShellPath;
    }

    private static string? GetDisableExtglobCommand()
    {
        var prefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(prefix))
            return "{ shopt -u extglob || setopt NO_EXTENDED_GLOB; } >/dev/null 2>&1 || true";
        return "shopt -u extglob 2>/dev/null || true";
    }

    private static string RewriteWindowsNullRedirect(string command)
    {
        if (!OperatingSystem.IsWindows()) return command;
        return Regex.Replace(command, @"2>\s*nul\b", "2>/dev/null", RegexOptions.IgnoreCase);
    }

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private string ConvertToPosixPath(string path)
        => _probeService?.GatePath(path, this) ?? PathConverter.WindowsPathToPosixPath(path);

    private static string PosixJoin(params string[] segments)
        => string.Join('/', segments.Select(s => s.TrimEnd('/')));

    private string? TryCreateSnapshot(IFileSystem fs, ILogger? logger)
    {
        if (ShellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (!fs.DirectoryExists(SnapshotDir))
                fs.CreateDirectory(SnapshotDir);

            var snapshotScript = "declare -f 2>/dev/null; shopt -p 2>/dev/null; set -o 2>/dev/null; alias 2>/dev/null; echo \"PATH=$PATH\"";
            var output = ExecuteShellCommand(ShellPath, $"-c -l {ShellQuote(snapshotScript)}", 10_000);
            if (output is null || string.IsNullOrWhiteSpace(output)) return null;

            var snapshotPath = Path.Combine(SnapshotDir, $"snapshot-bash-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.sh");
            fs.WriteAllText(snapshotPath, output);

            logger?.LogDebug("Bash 环境快照已创建: {Path}, Size={Size}", snapshotPath, output.Length);
            RotateSnapshots(fs, logger);
            return snapshotPath;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "创建 Bash 环境快照失败，将使用 login shell 降级");
            return null;
        }
    }

    private void RotateSnapshots(IFileSystem fs, ILogger? logger)
    {
        try
        {
            if (!fs.DirectoryExists(SnapshotDir)) return;
            var files = fs.EnumerateFiles(SnapshotDir, "snapshot-bash-*.sh", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count <= MaxSnapshotCount) return;
            var deletedCount = 0;
            foreach (var file in files.Skip(MaxSnapshotCount))
            {
                try { fs.DeleteFile(file); deletedCount++; }
                catch (Exception ex) { logger?.LogDebug(ex, "删除旧快照失败: {Path}", file); }
            }
            if (deletedCount > 0)
                logger?.LogDebug("快照轮转: 删除了 {DeletedCount} 个旧快照，保留 {RetainedCount} 个", deletedCount, MaxSnapshotCount);
        }
        catch (Exception ex) { logger?.LogDebug(ex, "快照轮转清理失败"); }
    }
}
