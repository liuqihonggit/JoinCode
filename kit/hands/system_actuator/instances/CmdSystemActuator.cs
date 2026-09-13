namespace Services.SystemActuator;

/// <summary>
/// CMD 系统执行器 — Windows 命令提示符，新增实现（原 Kind.Cmd 无实现）
/// </summary>
public sealed class CmdSystemActuator : SystemActuatorBase
{
    public const string CmdPathEnvVar = "JCC_CMD_PATH";

    public CmdSystemActuator(
        IFileSystem fs,
        ILogger? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(SystemActuatorKind.Cmd, fs, logger, sandboxManager, preventSleepService, config, toolUseId, spanId) { }

    /// <summary>
    /// 检测 CMD 能力并注册到基类静态缓存
    /// </summary>
    public static SystemActuatorCapability CreateCapability(IFileSystem fs, ILogger? logger = null)
    {
        var shellPath = ResolveShellPathStatic(fs, logger);
        var version = DetectVersionStatic(shellPath, logger);
        var displayName = $"CMD {version}";

        var capability = new SystemActuatorCapability
        {
            Kind = SystemActuatorKind.Cmd,
            ShellPath = shellPath,
            Version = version,
            DisplayName = displayName,
            Detached = false,
        };

        SystemActuatorBase.RegisterCapability(capability);
        return capability;
    }

    private static string ResolveShellPathStatic(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(CmdPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var cmdPath = Path.Combine(systemDir, "cmd.exe");
        if (fs.FileExists(cmdPath)) return cmdPath;

        logger?.LogWarning("cmd.exe not found, falling back to 'cmd.exe'. Set {EnvVar} to specify path.", CmdPathEnvVar);
        return "cmd.exe";
    }

    private static string DetectVersionStatic(string shellPath, ILogger? logger)
    {
        try
        {
            var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
            {
                FileName = shellPath,
                ArgumentList = ["/c", "ver"],
                RedirectStandardError = false,
            });
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            var match = Regex.Match(output ?? "", @"\[Version\s+([^\]]+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "unknown";
        }
        catch { return "unknown"; }
    }

    /// <inheritdoc />
    public override Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken cancellationToken = default)
    {
        var tmpDir = Path.GetTempPath();
        var cwdFilePath = Path.Combine(tmpDir, $"jcc-pwd-cmd-{options.SessionId}");

        var cwdTracking = $" & echo %CD% > \"{cwdFilePath}\"";

        var commandString = command + cwdTracking;

        Logger?.LogDebug("CmdSystemActuator: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new SystemActuatorExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public override string[] GetSpawnArgs(string commandString)
        => ["/c", commandString];
}
