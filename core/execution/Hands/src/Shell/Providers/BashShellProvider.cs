namespace Services.Shell.Providers;

/// <summary>
/// Bash Shell 提供者 — 对齐 TS BashProvider
/// 在 Windows 上使用 Git Bash bash.exe 执行命令
/// 支持 CWD 追踪、环境变量注入、extglob 禁用
/// </summary>
[Register]
public sealed class BashShellProvider : ShellProviderBase, IDisposable
{
    private string? _snapshotFilePath;
    private readonly IEnvironmentProbeService? _probeService;

    public override ShellType Type => ShellType.Bash;
    public override bool Detached => true;

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_GIT_BASH_PATH
    /// </summary>
    public const string GitBashPathEnvVar = "JCC_GIT_BASH_PATH";

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_SHELL_PREFIX
    /// </summary>
    public const string ShellPrefixEnvVar = "JCC_SHELL_PREFIX";

    /// <summary>
    /// 快照目录 — 对齐 TS ~/.claude/shell-snapshots
    /// </summary>
    private static readonly string SnapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppDataConstants.AppDataFolder, "shell-snapshots");

    /// <summary>
    /// 快照最大保留数量 — 超过此数量时删除最旧的快照
    /// </summary>
    private const int MaxSnapshotCount = 200;

    public BashShellProvider(IFileSystem fs, IEnvironmentProbeService? probeService = null, string? shellPath = null, ILogger? logger = null)
        : base(fs, shellPath, logger)
    {
        _probeService = probeService;
        _snapshotFilePath = TryCreateSnapshot();
    }

    /// <summary>
    /// 释放资源 — 对齐 TS registerCleanup: 正常退出时删除当前会话快照
    /// 异常退出时快照残留，由轮转清理机制在下次启动时处理
    /// </summary>
    public void Dispose()
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
    protected override string ResolveShellPath()
    {
        var envPath = ResolveFromEnvVar(GitBashPathEnvVar);
        if (envPath is not null) return envPath;

        var gitPath = FindExecutable("git.exe", excludeCurrentDir: true);
        if (gitPath is not null)
        {
            var bashFromGit = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(gitPath))!,
                "bin", "bash.exe");
            if (Fs.FileExists(bashFromGit))
            {
                return bashFromGit;
            }
        }

        var commonPath = FindInCommonPaths(
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe");
        if (commonPath is not null) return commonPath;

        Logger?.LogWarning("Git Bash not found, falling back to cmd.exe. Set {EnvVar} to specify bash path.", GitBashPathEnvVar);
        return "cmd.exe";
    }

    /// <inheritdoc />
    protected override string DetectVersion()
    {
        if (ShellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "cmd-fallback";
        }

        var output = ExecuteShellCommand(ShellPath, "--version");
        if (output is null) return "unknown";

        var match = Regex.Match(output, @"version\s+(\S+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    /// <inheritdoc />
    public override Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command,
        ShellExecOptions options,
        CancellationToken cancellationToken = default)
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
        {
            commandParts.Add(disableExtglobCmd);
        }

        commandParts.Add($"eval {ShellQuote(normalizedCommand)}");
        commandParts.Add($"pwd -P >| {ShellQuote(shellCwdFilePath)}");

        var commandString = string.Join(" && ", commandParts);

        var shellPrefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(shellPrefix))
        {
            commandString = $"{shellPrefix} {ShellQuote(commandString)}";
        }

        Logger?.LogDebug("BashShellProvider: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// 有快照时跳过 -l（login shell），因为快照已包含用户环境 — 对齐 TS BashProvider
    /// </remarks>
    public override string[] GetSpawnArgs(string commandString)
        => _snapshotFilePath is not null ? ["-c", commandString] : ["-c", "-l", commandString];

    /// <inheritdoc />
    protected override void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command)
    {
        if (Environment.GetEnvironmentVariable("SHELL") is null)
        {
            env["SHELL"] = ShellPath;
        }
    }

    /// <summary>
    /// 禁用 extglob 命令 — 对齐 TS getDisableExtglobCommand
    /// </summary>
    private static string? GetDisableExtglobCommand()
    {
        var prefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(prefix))
        {
            return "{ shopt -u extglob || setopt NO_EXTENDED_GLOB; } >/dev/null 2>&1 || true";
        }

        return "shopt -u extglob 2>/dev/null || true";
    }

    /// <summary>
    /// 重写 Windows 风格的 null 重定向 — 对齐 TS rewriteWindowsNullRedirect
    /// 将 `2>nul` 转换为 `2>/dev/null`，避免在 Git Bash 中创建名为 `nul` 的文件
    /// </summary>
    private static string RewriteWindowsNullRedirect(string command)
    {
        if (!OperatingSystem.IsWindows()) return command;
        return Regex.Replace(command, @"2>\s*nul\b", "2>/dev/null", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Shell 单引号引用 — 对齐 TS shellQuote
    /// </summary>
    private static string ShellQuote(string s)
        => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// 转换路径为 POSIX 格式 — 优先委托给 IEnvironmentProbeService.GatePath，回退到 PathConverter
    /// </summary>
    private string ConvertToPosixPath(string path)
        => _probeService?.GatePath(path, isPowerShell: false) ?? PathConverter.WindowsPathToPosixPath(path);

    /// <summary>
    /// Windows 路径转 POSIX 路径 — 对齐 TS windowsPathToPosixPath
    /// 保留为向后兼容，新代码应使用 PathConverter.WindowsPathToPosixPath 或 IEnvironmentProbeService.GatePath
    /// </summary>
    [Obsolete("Use PathConverter.WindowsPathToPosixPath instead")]
    internal static string WindowsPathToPosixPath(string windowsPath)
    {
        if (windowsPath.StartsWith("\\\\"))
        {
            return windowsPath.Replace('\\', '/');
        }

        var match = Regex.Match(windowsPath, @"^([A-Za-z]):[/\\]");
        if (match.Success)
        {
            var drive = match.Groups[1].Value.ToLowerInvariant();
            return '/' + drive + windowsPath[2..].Replace('\\', '/');
        }

        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// POSIX 路径拼接 — 对齐 TS path/posix.join
    /// </summary>
    private static string PosixJoin(params string[] segments)
    {
        var result = string.Join('/', segments.Select(s => s.TrimEnd('/')));
        return result;
    }

    /// <summary>
    /// 尝试创建 Bash 环境快照 — 对齐 TS ShellSnapshot.createAndSaveSnapshot
    /// 捕获别名、shell 选项、PATH 等用户环境，供后续命令 source
    /// 失败时静默降级（不影响主流程）
    /// </summary>
    private string? TryCreateSnapshot()
    {
        if (ShellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            if (!Fs.DirectoryExists(SnapshotDir))
            {
                Fs.CreateDirectory(SnapshotDir);
            }

            var snapshotScript = "declare -f 2>/dev/null; shopt -p 2>/dev/null; set -o 2>/dev/null; alias 2>/dev/null; echo \"PATH=$PATH\"";

            var output = ExecuteShellCommand(ShellPath, $"-c -l {ShellQuote(snapshotScript)}", 10_000);
            if (output is null || string.IsNullOrWhiteSpace(output)) return null;

            var snapshotPath = Path.Combine(SnapshotDir, $"snapshot-bash-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.sh");
            Fs.WriteAllText(snapshotPath, output);

            Logger?.LogDebug("Bash 环境快照已创建: {Path}, Size={Size}", snapshotPath, output.Length);

            RotateSnapshots();

            return snapshotPath;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "创建 Bash 环境快照失败，将使用 login shell 降级");
            return null;
        }
    }

    /// <summary>
    /// 快照轮转清理 — 保留最近 MaxSnapshotCount 个快照，删除更旧的
    /// 按文件名中的时间戳排序（文件名格式: snapshot-bash-{unixTimestamp}.sh）
    /// </summary>
    private void RotateSnapshots()
    {
        try
        {
            if (!Fs.DirectoryExists(SnapshotDir)) return;

            var files = Fs.EnumerateFiles(SnapshotDir, "snapshot-bash-*.sh", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static f => f, StringComparer.Ordinal)
                .ToList();

            if (files.Count <= MaxSnapshotCount) return;

            var toDelete = files.Skip(MaxSnapshotCount);
            var deletedCount = 0;

            foreach (var file in toDelete)
            {
                try
                {
                    Fs.DeleteFile(file);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug(ex, "删除旧快照失败: {Path}", file);
                }
            }

            if (deletedCount > 0)
            {
                Logger?.LogDebug("快照轮转: 删除了 {DeletedCount} 个旧快照，保留 {RetainedCount} 个", deletedCount, MaxSnapshotCount);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "快照轮转清理失败");
        }
    }
}
