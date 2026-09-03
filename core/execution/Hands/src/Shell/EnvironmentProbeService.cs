namespace Tools;

/// <summary>
/// 环境探测服务 — 探测运行环境能力，为Shell工具提供执行器选择依据
/// 5分钟缓存，IFileSystem抽象，路径归一化
/// </summary>
[Register(typeof(IEnvironmentProbeService), ServiceLifetime.Singleton)]
public sealed class EnvironmentProbeService : ServiceEntity, IEnvironmentProbeService
{
    private readonly ILogger<EnvironmentProbeService>? _logger;
    private readonly IToolHealthMonitor _healthMonitor;
    private EnvironmentReport? _cachedReport;
    private DateTime _lastProbeTime = DateTime.MinValue;
    private readonly AsyncLock _lock = new();

    public EnvironmentProbeService(IToolHealthMonitor healthMonitor, ILogger<EnvironmentProbeService>? logger = null)
    {
        _healthMonitor = healthMonitor;
        _logger = logger;
    }

    public async Task<EnvironmentReport> ProbeEnvironmentAsync(bool forceRescan = false, CancellationToken ct = default)
    {
        if (!forceRescan && _cachedReport is not null && _lastProbeTime > DateTime.UtcNow.AddMinutes(-5))
            return _cachedReport;

        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        if (!forceRescan && _cachedReport is not null && _lastProbeTime > DateTime.UtcNow.AddMinutes(-5))
            return _cachedReport;

        var components = new List<ComponentScore>
        {
            await ProbeComponentAsync("git", "Git", ["--version"], "git version"),
            await ProbeComponentAsync("powershell", "PowerShell", ["-Command", "$PSVersionTable.PSVersion.ToString()"], null),
            await ProbeComponentAsync("python", "Python", ["--version"], "Python"),
            await ProbeComponentAsync("dotnet", ".NET SDK", ["--version"], null),
            await ProbeComponentAsync("node", "Node.js", ["--version"], null),
            await ProbeComponentAsync("wsl", "WSL2", ["--status"], null),
            await ProbeComponentAsync("docker", "Docker", ["--version"], "Docker version"),
        };

        var report = new EnvironmentReport
        {
            ProbeTime = DateTime.UtcNow,
            Components = components,
            RecommendedShell = GetRecommendedShell(components)
        };

        _cachedReport = report;
        _lastProbeTime = DateTime.UtcNow;
        return report;
    
    }

    public string NormalizePath(string rawPath, string targetFormat = "auto")
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return rawPath;

        var isWindows = OperatingSystem.IsWindows();
        var useUnixFormat = targetFormat == "unix" || (targetFormat == "auto" && !isWindows);
        var normalized = rawPath.Replace('\\', '/').Trim();

        if (useUnixFormat)
        {
            if (isWindows && normalized.Length >= 2 && normalized[1] == ':')
                normalized = $"/{char.ToLower(normalized[0])}{normalized[2..]}";
            return normalized;
        }

        if (normalized.StartsWith('/') && normalized.Length > 2 && normalized[2] == '/')
            normalized = $"{char.ToUpper(normalized[1])}:{normalized[2..]}";
        return normalized.Replace('/', '\\');
    }

    /// <inheritdoc />
    public string GatePath(string rawPath, ISystemActuator actuator)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return rawPath;

        var isWindows = OperatingSystem.IsWindows();
        var isBash = actuator.Kind == SystemActuatorKind.Bash;

        if (isWindows && isBash)
        {
            return PathConverter.WindowsPathToPosixPath(rawPath);
        }

        if (isWindows && !isBash)
        {
            return PathConverter.PosixPathToWindowsPath(rawPath);
        }

        if (!isWindows && PathConverter.LooksLikeWindowsPath(rawPath))
        {
            return PathConverter.WindowsPathToPosixPath(rawPath);
        }

        return rawPath;
    }

    /// <inheritdoc />
    public string GateCommandPaths(string command, ISystemActuator actuator)
    {
        if (string.IsNullOrEmpty(command)) return command;

        var isWindows = OperatingSystem.IsWindows();
        var isBash = actuator.Kind == SystemActuatorKind.Bash;
        var toPosix = (isWindows && isBash) || (!isWindows);

        return PathConverter.GateCommandPaths(command, toPosix);
    }

    public async Task<IReadOnlyDictionary<string, ExecutorScore>> GetExecutorScoresAsync(CancellationToken ct = default)
    {
        var report = await ProbeEnvironmentAsync(false, ct).ConfigureAwait(false);
        var healthRecords = await _healthMonitor.GetAllRecordsAsync(ct).ConfigureAwait(false);
        var scores = new Dictionary<string, ExecutorScore>(StringComparer.OrdinalIgnoreCase);

        var git = report.Components.FirstOrDefault(c => c.Id == "git");
        var wsl = report.Components.FirstOrDefault(c => c.Id == "wsl");
        scores["git_bash"] = new ExecutorScore
        {
            ExecutorId = "git_bash",
            Score = (git?.IsInstalled == true ? 60 : 0) + (wsl?.IsInstalled == true ? 20 : 0) + (git?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("git_bash_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("git_bash_success")?.SuccessCount ?? 0,
            Reason = git?.IsInstalled == true ? "Git Bash可用" : "未安装Git"
        };

        var ps = report.Components.FirstOrDefault(c => c.Id == "powershell");
        var dotnet = report.Components.FirstOrDefault(c => c.Id == "dotnet");
        scores["powershell"] = new ExecutorScore
        {
            ExecutorId = "powershell",
            Score = (ps?.IsInstalled == true ? 40 : 0) + (dotnet?.IsInstalled == true ? 15 : 0) + (ps?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("powershell_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("powershell_success")?.SuccessCount ?? 0,
            Reason = ps?.IsInstalled == true ? "Windows原生PowerShell" : "无PowerShell"
        };

        scores["cmd"] = new ExecutorScore
        {
            ExecutorId = "cmd",
            Score = 30,
            FailCount = healthRecords.GetValueOrDefault("cmd_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("cmd_success")?.SuccessCount ?? 0,
            Reason = "基础CMD，兼容性强但功能有限"
        };

        var python = report.Components.FirstOrDefault(c => c.Id == "python");
        scores["python_script"] = new ExecutorScore
        {
            ExecutorId = "python_script",
            Score = (python?.IsInstalled == true ? 50 : 0) + (python?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("python_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("python_success")?.SuccessCount ?? 0,
            Reason = python?.IsInstalled == true ? $"Python {python.Version}" : "无Python"
        };

        scores["wsl_bash"] = new ExecutorScore
        {
            ExecutorId = "wsl_bash",
            Score = (wsl?.IsInstalled == true ? 70 : 0) + (wsl?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("wsl_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("wsl_success")?.SuccessCount ?? 0,
            Reason = wsl?.IsInstalled == true ? "WSL完整Linux" : "未安装WSL"
        };

        var docker = report.Components.FirstOrDefault(c => c.Id == "docker");
        scores["docker"] = new ExecutorScore
        {
            ExecutorId = "docker",
            Score = (docker?.IsInstalled == true ? 80 : 0) + (docker?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("docker_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("docker_success")?.SuccessCount ?? 0,
            Reason = docker?.IsInstalled == true ? "Docker容器隔离" : "Docker未就绪"
        };

        return scores.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ComponentScore> ProbeComponentAsync(string command, string name, string[] args, string? versionPrefix)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
                return new ComponentScore { Id = command, Name = name, IsInstalled = false, Score = -10 };

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            process.WaitForExit(5000);

            var isInstalled = process.ExitCode == 0;
            var versionText = (string.IsNullOrWhiteSpace(output) ? error : output).Trim();
            var version = versionPrefix is not null && isInstalled
                ? ExtractVersion(versionText, versionPrefix)
                : isInstalled ? versionText.Split('\n').FirstOrDefault() : null;

            return new ComponentScore
            {
                Id = command,
                Name = name,
                Version = version,
                IsInstalled = isInstalled,
                Score = isInstalled ? 10 : -5
            };
        }
        catch
        {
            return new ComponentScore { Id = command, Name = name, IsInstalled = false, Score = -5 };
        }
    }

    private static string ExtractVersion(string output, string prefix)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, $@"{System.Text.RegularExpressions.Regex.Escape(prefix)}\s*([\d.]+)");
        return match.Success ? match.Groups[1].Value : output.Trim();
    }

    private static string GetRecommendedShell(List<ComponentScore> components)
    {
        var wsl = components.FirstOrDefault(c => c.Id == "wsl");
        if (wsl?.IsInstalled == true && wsl.Score > 0) return "wsl-bash";

        var git = components.FirstOrDefault(c => c.Id == "git");
        if (git?.IsInstalled == true && git.Score > 0) return "git-bash";

        var ps = components.FirstOrDefault(c => c.Id == "powershell");
        return ps?.IsInstalled == true ? "powershell" : "cmd";
    }

    protected override void OnDispose() => _lock.Dispose();
}
