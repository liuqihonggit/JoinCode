namespace Services.SystemActuator;

/// <summary>
/// Python 系统执行器 — 合并原 PythonShellProvider + PythonCapabilityProvider
/// </summary>
public sealed class PythonSystemActuator : SystemActuatorBase
{
    public const string PythonPathEnvVar = "JCC_PYTHON_PATH";

    public PythonSystemActuator(
        IFileSystem fs,
        ILogger? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(SystemActuatorKind.Python, fs, logger, sandboxManager, preventSleepService, config, toolUseId, spanId) { }

    /// <summary>
    /// 检测 Python 能力并注册到基类静态缓存
    /// </summary>
    public static SystemActuatorCapability CreateCapability(IFileSystem fs, ILogger? logger = null)
    {
        var shellPath = ResolveShellPathStatic(fs, logger);
        var version = DetectVersionStatic(shellPath, logger);
        var displayName = $"Python {version}";

        var capability = new SystemActuatorCapability
        {
            Kind = SystemActuatorKind.Python,
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
        var envPath = Environment.GetEnvironmentVariable(PythonPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        try
        {
            var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
            {
                FileName = "where.exe",
                ArgumentList = ["python3.exe"],
                RedirectStandardError = false,
            });
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
        catch (Exception ex) { logger?.LogDebug(ex, "where.exe python3.exe failed"); }

        var commonPaths = new[] { @"C:\Python312\python.exe", @"C:\Python311\python.exe", @"C:\Python310\python.exe" };
        foreach (var cp in commonPaths)
            if (fs.FileExists(cp)) return cp;

        logger?.LogWarning("Python not found, falling back to python3. Set {EnvVar} to specify path.", PythonPathEnvVar);
        return "python3";
    }

    private static string DetectVersionStatic(string shellPath, ILogger? logger)
    {
        try
        {
            var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
            {
                FileName = shellPath,
                ArgumentList = ["--version"],
                RedirectStandardError = false,
            });
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(output)) return output.Trim();
        }
        catch (Exception ex) { logger?.LogDebug(ex, "python3 --version failed: {ShellPath}", shellPath); }

        try
        {
            var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
            {
                FileName = "python",
                ArgumentList = ["--version"],
                RedirectStandardError = false,
            });
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (!string.IsNullOrWhiteSpace(output)) return output.Trim();
            }
        }
        catch (Exception ex) { logger?.LogDebug(ex, "python --version fallback failed"); }

        return "unknown";
    }

    /// <inheritdoc />
    public override Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken cancellationToken = default)
    {
        var tmpDir = Path.GetTempPath();
        var cwdFilePath = Path.Combine(tmpDir, $"jcc-pwd-py-{options.SessionId}");

        var script = command
            + $"\nimport os; open(r'{cwdFilePath}', 'w').write(os.getcwd())";

        var commandString = script.Replace("'", "'\\''");

        Logger?.LogDebug("PythonSystemActuator: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new SystemActuatorExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public override string[] GetSpawnArgs(string commandString)
        => ["-c", commandString];
}
