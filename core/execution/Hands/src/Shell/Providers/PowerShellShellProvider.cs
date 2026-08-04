namespace Services.Shell.Providers;

/// <summary>
/// PowerShell 能力描述提供者 — DI 单例，长命缓存
/// </summary>
[Register]
public sealed class PowerShellCapabilityProvider : ShellCapabilityProvider
{
    public const string PowerShellPathEnvVar = "JCC_POWERSHELL_PATH";

    public override ShellProviderBase CreateProvider(
        ShellCapability capability, IFileSystem fs, ILogger? logger = null)
        => new PowerShellShellProvider(capability, fs, logger);

    protected override ShellType GetShellType() => ShellType.PowerShell;

    protected override string ResolveShellPath(IFileSystem fs, ILogger? logger)
    {
        var envPath = Environment.GetEnvironmentVariable(PowerShellPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && fs.FileExists(envPath)) return envPath;

        var psi = new ProcessStartInfo
        {
            FileName = "where.exe",
            Arguments = "pwsh.exe",
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
        catch (Exception ex) { logger?.LogDebug(ex, "where.exe pwsh.exe failed"); }

        var commonPath = @"C:\Program Files\PowerShell\7\pwsh.exe";
        if (fs.FileExists(commonPath)) return commonPath;

        logger?.LogWarning("PowerShell not found, falling back to powershell.exe. Set {EnvVar} to specify path.", PowerShellPathEnvVar);
        return "powershell.exe";
    }

    protected override string DetectVersion(string shellPath, ILogger? logger)
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
            using var p = Process.Start(psi);
            if (p is null) return "unknown";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var version = output?.Trim();
            return string.IsNullOrEmpty(version) ? "unknown" : version;
        }
        catch { return "unknown"; }
    }

    protected override bool DetectIsPowerShellCore(string shellPath, string version)
        => shellPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase)
        || version.StartsWith('7') || version.StartsWith('6');

    protected override string BuildDisplayName(string shellPath, string version)
        => DetectIsPowerShellCore(shellPath, version)
            ? $"PowerShell Core {version}"
            : $"PowerShell Desktop {version}";
}
