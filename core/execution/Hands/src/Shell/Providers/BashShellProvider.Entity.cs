namespace Services.Shell.Providers;

/// <summary>
/// Bash Shell 执行器 — 短命 Entity，每次命令执行创建
/// 引用 BashCapabilityProvider 提供的 ShellCapability
/// </summary>
public sealed class BashShellProvider : ShellProviderBase
{
    private readonly IEnvironmentProbeService? _probeService;
    private string? _snapshotFilePath;

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

        var shellPrefix = Environment.GetEnvironmentVariable(BashCapabilityProvider.ShellPrefixEnvVar);
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
        var prefix = Environment.GetEnvironmentVariable(BashCapabilityProvider.ShellPrefixEnvVar);
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
