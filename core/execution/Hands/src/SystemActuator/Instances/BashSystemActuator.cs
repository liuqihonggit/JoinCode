namespace Services.SystemActuator;

/// <summary>
/// Bash 系统执行器 — 合并原 BashShellProvider + BashCapabilityProvider
/// 含：能力检测（静态缓存）+ 命令构建（含环境快照）+ 环境变量注入
/// </summary>
public sealed class BashSystemActuator : SystemActuatorBase
{
    public const string GitBashPathEnvVar = "JCC_GIT_BASH_PATH";
    public const string ShellPrefixEnvVar = "JCC_SHELL_PREFIX";

    private readonly IEnvironmentProbeService? _probeService;
    private string? _snapshotFilePath;

    private static readonly string SnapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppDataConstants.AppDataFolder, "shell-snapshots");

    private const int MaxSnapshotCount = 200;

    public BashSystemActuator(
        IFileSystem fs,
        IEnvironmentProbeService? probeService = null,
        ILogger? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(SystemActuatorKind.Bash, fs, logger, sandboxManager, preventSleepService, config, toolUseId, spanId)
    {
        _probeService = probeService;
        _snapshotFilePath = TryCreateSnapshot(fs, logger);
    }

    /// <summary>
    /// 检测 Bash 能力并注册到基类静态缓存 — 由 SystemActuatorInitializer 调用
    /// </summary>
    public static SystemActuatorCapability CreateCapability(IFileSystem fs, ILogger? logger = null)
    {
        var shellPath = ResolveShellPathStatic(fs, logger);
        var version = DetectVersionStatic(shellPath, logger);
        var displayName = shellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            ? "CMD (Bash fallback)"
            : $"Git Bash {version}";

        var capability = new SystemActuatorCapability
        {
            Kind = SystemActuatorKind.Bash,
            ShellPath = shellPath,
            Version = version,
            DisplayName = displayName,
            Detached = true,
        };

        SystemActuatorBase.RegisterCapability(capability);
        return capability;
    }

    private static string ResolveShellPathStatic(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(GitBashPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        var gitPath = FindExecutableStatic(fs, logger, "git.exe", excludeCurrentDir: true);
        if (gitPath is not null)
        {
            var bashFromGit = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(gitPath))!,
                "bin", "bash.exe");
            if (fs.FileExists(bashFromGit))
                return bashFromGit;
        }

        var commonPaths = new[] { @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe" };
        foreach (var p in commonPaths)
            if (fs.FileExists(p)) return p;

        logger?.LogWarning("Git Bash not found, falling back to cmd.exe. Set {EnvVar} to specify bash path.", GitBashPathEnvVar);
        return "cmd.exe";
    }

    private static string DetectVersionStatic(string shellPath, ILogger? logger)
    {
        if (shellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
            return "cmd-fallback";

        var output = ExecuteShellCommandStatic(shellPath, "--version");
        if (output is null) return "unknown";

        var match = Regex.Match(output, @"version\s+(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private static string? FindExecutableStatic(IFileSystem fs, ILogger? logger, string executable, bool excludeCurrentDir = true)
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

    private static string? ExecuteShellCommandStatic(string fileName, string arguments, int timeoutMs = 5000)
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

    /// <inheritdoc />
    public override Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken cancellationToken = default)
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

        Logger?.LogDebug("BashSystemActuator: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new SystemActuatorExecCommandResult
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
        => PathConverter.WindowsPathToPosixPath(path);

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
