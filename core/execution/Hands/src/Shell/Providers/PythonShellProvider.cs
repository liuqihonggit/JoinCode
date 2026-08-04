namespace Services.Shell.Providers;

/// <summary>
/// Python 能力描述提供者 — DI 单例，长命缓存
/// </summary>
[Register]
public sealed class PythonCapabilityProvider : ShellCapabilityProvider
{
    public const string PythonPathEnvVar = "JCC_PYTHON_PATH";

    public override ShellProviderBase CreateProvider(
        ShellCapability capability, IFileSystem fs, ILogger? logger = null)
        => new PythonShellProvider(capability, fs, logger);

    protected override ShellType GetShellType() => ShellType.Python;

    protected override string ResolveShellPath(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(PythonPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        var psi = new ProcessStartInfo
        {
            FileName = "where.exe",
            Arguments = "python3.exe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        try
        {
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

    protected override string DetectVersion(string shellPath, ILogger? logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(output)) return output.Trim();
        }
        catch (Exception ex) { logger?.LogDebug(ex, "python3 --version failed: {ShellPath}", shellPath); }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
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

    protected override string BuildDisplayName(string shellPath, string version)
        => $"Python {version}";
}

/// <summary>
/// Python Shell 执行器 — 短命 Entity，每次命令执行创建
/// </summary>
public sealed class PythonShellProvider : ShellProviderBase
{
    public PythonShellProvider(
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
        var tmpDir = Path.GetTempPath();
        var cwdFilePath = Path.Combine(tmpDir, $"jcc-pwd-py-{options.SessionId}");

        var script = command
            + $"\nimport os; open(r'{cwdFilePath}', 'w').write(os.getcwd())";

        var commandString = script.Replace("'", "'\\''");

        Logger?.LogDebug("PythonShellProvider: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public override string[] GetSpawnArgs(string commandString)
        => ["-c", commandString];
}
