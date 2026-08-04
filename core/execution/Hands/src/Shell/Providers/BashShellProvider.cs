namespace Services.Shell.Providers;

/// <summary>
/// Bash 能力描述提供者 — DI 单例，长命缓存
/// 版本/路径只检测一次，后续调用返回缓存
/// </summary>
[Register]
public sealed class BashCapabilityProvider : ShellCapabilityProvider
{
    public const string GitBashPathEnvVar = "JCC_GIT_BASH_PATH";
    public const string ShellPrefixEnvVar = "JCC_SHELL_PREFIX";

    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    public BashCapabilityProvider(IFileSystem fs, IEnvironmentProbeService? probeService = null, ILogger<BashCapabilityProvider>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    public override ShellProviderBase CreateProvider(
        ShellCapability capability, IFileSystem fs, ILogger? logger = null)
        => new BashShellProvider(capability, fs, logger: logger);

    protected override ShellType GetShellType() => ShellType.Bash;
    protected override bool IsDetached() => true;

    protected override string ResolveShellPath(IFileSystem fs, ILogger? logger)
    {
        var envPath = ResolveFromEnvVarShared(fs, BashCapabilityProvider.GitBashPathEnvVar);
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

        logger?.LogWarning("Git Bash not found, falling back to cmd.exe. Set {EnvVar} to specify bash path.", BashCapabilityProvider.GitBashPathEnvVar);
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
