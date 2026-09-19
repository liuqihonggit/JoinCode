namespace Tools;

/// <summary>
/// 环境探测 Actor 命令 — Channel 中的消息类型
/// </summary>
public interface IEnvProbeCommand;

internal sealed record ProbeEnvCmd(bool ForceRescan, CancellationToken Ct, TaskCompletionSource<EnvironmentReport> Tcs) : IEnvProbeCommand;

/// <summary>
/// 环境探测服务 — Actor 化：继承 ActorBase，Consumer 线程独占 _cachedReport/_lastProbeTime，
/// 消除 AsyncLock。进程探测由 Consumer 串行执行，不再阻塞其他调用方 5s 超时。
/// 5分钟缓存，IFileSystem抽象，路径归一化
/// </summary>
[Register(typeof(IEnvironmentProbeService), ServiceLifetime.Singleton)]
public sealed class EnvironmentProbeService : ActorBase<IEnvProbeCommand, Unit>, IEnvironmentProbeService {
    private readonly ILogger<EnvironmentProbeService>? _logger;
    private readonly IToolHealthMonitor _healthMonitor;
    private EnvironmentReport? _cachedReport;
    private DateTime _lastProbeTime = DateTime.MinValue;

    /// <summary>
    /// 构造环境探测服务
    /// </summary>
    /// <param name="healthMonitor">工具健康监控</param>
    /// <param name="logger">日志器（可选）</param>
    public EnvironmentProbeService(IToolHealthMonitor healthMonitor, ILogger<EnvironmentProbeService>? logger = null)
        : base() {
        _healthMonitor = healthMonitor;
        _logger = logger;
    }


    /// <inheritdoc/>
    public async Task<EnvironmentReport> ProbeEnvironmentAsync(bool forceRescan = false, CancellationToken ct = default) {
        var tcs = TcsFactory.Create<EnvironmentReport>();
        await SendAsync(new ProbeEnvCmd(forceRescan, ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, ExecutorScore>> GetExecutorScoresAsync(CancellationToken ct = default) {
        var report = await ProbeEnvironmentAsync(false, ct).ConfigureAwait(false);
        var healthRecords = await _healthMonitor.GetAllRecordsAsync(ct).ConfigureAwait(false);
        var scores = new Dictionary<string, ExecutorScore>(StringComparer.OrdinalIgnoreCase);
        var compById = report.Components.ToLookup(c => c.Id, StringComparer.OrdinalIgnoreCase);

        var git = compById["git"].FirstOrDefault();
        var wsl = compById["wsl"].FirstOrDefault();
        scores["git_bash"] = new ExecutorScore {
            ExecutorId = "git_bash",
            Score = (git?.IsInstalled == true ? 60 : 0) + (wsl?.IsInstalled == true ? 20 : 0) + (git?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("git_bash_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("git_bash_success")?.SuccessCount ?? 0,
            Reason = git?.IsInstalled == true ? "Git Bash可用" : "未安装Git"
        };

        var ps = compById["powershell"].FirstOrDefault();
        var dotnet = compById["dotnet"].FirstOrDefault();
        scores["powershell"] = new ExecutorScore {
            ExecutorId = "powershell",
            Score = (ps?.IsInstalled == true ? 40 : 0) + (dotnet?.IsInstalled == true ? 15 : 0) + (ps?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("powershell_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("powershell_success")?.SuccessCount ?? 0,
            Reason = ps?.IsInstalled == true ? "Windows原生PowerShell" : "无PowerShellD"
        };

        scores["cmd"] = new ExecutorScore {
            ExecutorId = "cmd",
            Score = 30,
            FailCount = healthRecords.GetValueOrDefault("cmd_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("cmd_success")?.SuccessCount ?? 0,
            Reason = "基础CMD，兼容性强但功能有限"
        };

        var python = compById["python"].FirstOrDefault();
        scores["python_script"] = new ExecutorScore {
            ExecutorId = "python_script",
            Score = (python?.IsInstalled == true ? 50 : 0) + (python?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("python_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("python_success")?.SuccessCount ?? 0,
            Reason = python?.IsInstalled == true ? $"Python {python.Version}" : "无Python"
        };

        scores["wsl_bash"] = new ExecutorScore {
            ExecutorId = "wsl_bash",
            Score = (wsl?.IsInstalled == true ? 70 : 0) + (wsl?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("wsl_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("wsl_success")?.SuccessCount ?? 0,
            Reason = wsl?.IsInstalled == true ? "WSL完整Linux" : "未安装WSL"
        };

        var docker = compById["docker"].FirstOrDefault();
        scores["docker"] = new ExecutorScore {
            ExecutorId = "docker",
            Score = (docker?.IsInstalled == true ? 80 : 0) + (docker?.Score ?? 0),
            FailCount = healthRecords.GetValueOrDefault("docker_fail")?.FailCount ?? 0,
            SuccessCount = healthRecords.GetValueOrDefault("docker_success")?.SuccessCount ?? 0,
            Reason = docker?.IsInstalled == true ? "Docker容器隔离" : "Docker未就绪"
        };

        return scores.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _cachedReport/_lastProbeTime，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(IEnvProbeCommand command, CancellationToken ct) {
        if (command is ProbeEnvCmd cmd) {
            if (!cmd.ForceRescan && _cachedReport is not null && _lastProbeTime > DateTime.UtcNow.AddMinutes(-5)) {
                cmd.Tcs.TrySetResult(_cachedReport);
                return;
            }

            var components = new List<ComponentScore>
            {
                await ProbeComponentAsync("git", "Git", ["--version"], "git version").ConfigureAwait(false),
                await ProbeComponentAsync("powershell", "PowerShell", ["-Command", "$PSVersionTable.PSVersion.ToString()"], null).ConfigureAwait(false),
                await ProbeComponentAsync("python", "Python", ["--version"], "Python").ConfigureAwait(false),
                await ProbeComponentAsync("dotnet", ".NET SDK", ["--version"], null).ConfigureAwait(false),
                await ProbeComponentAsync("node", "Node.js", ["--version"], null).ConfigureAwait(false),
                await ProbeComponentAsync("wsl", "WSL2", ["--status"], null).ConfigureAwait(false),
                await ProbeComponentAsync("docker", "Docker", ["--version"], "Docker version").ConfigureAwait(false),
            };

            var report = new EnvironmentReport {
                ProbeTime = DateTime.UtcNow,
                Components = components,
                RecommendedShell = GetRecommendedShell(components)
            };

            _cachedReport = report;
            _lastProbeTime = DateTime.UtcNow;
            cmd.Tcs.TrySetResult(report);
        }
    }

    /// <summary>
    /// Actor Consumer 异常回调 — 记录警告日志，不向上抛出
    /// </summary>
    /// <param name="ex">Consumer 处理命令时抛出的异常</param>
    protected override void OnConsumerError(Exception ex) {
        _logger?.LogWarning(ex, "EnvironmentProbe Actor Consumer 命令处理异常");
    }

    private async Task<ComponentScore> ProbeComponentAsync(string command, string name, string[] args, string? versionPrefix) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
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

            return new ComponentScore {
                Id = command,
                Name = name,
                Version = version,
                IsInstalled = isInstalled,
                Score = isInstalled ? 10 : -5
            };
        } catch {
            return new ComponentScore { Id = command, Name = name, IsInstalled = false, Score = -5 };
        }
    }

    private static string ExtractVersion(string output, string prefix) {
        var match = System.Text.RegularExpressions.Regex.Match(output, $@"{System.Text.RegularExpressions.Regex.Escape(prefix)}\s*([\d.]+)");
        return match.Success ? match.Groups[1].Value : output.Trim();
    }

    private static string GetRecommendedShell(List<ComponentScore> components) {
        var compById = components.ToLookup(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var wsl = compById["wsl"].FirstOrDefault();
        if (wsl?.IsInstalled == true && wsl.Score > 0) return "wsl-bash";

        var git = compById["git"].FirstOrDefault();
        if (git?.IsInstalled == true && git.Score > 0) return "git-bash";

        var ps = compById["powershell"].FirstOrDefault();
        return ps?.IsInstalled == true ? "powershell" : "cmd";
    }
}