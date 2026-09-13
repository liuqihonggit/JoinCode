namespace Core.Security.Sandbox.Providers;


/// <summary>
/// Docker 沙箱提供者 — 通过 Docker 容器实现完全隔离的命令执行环境
/// </summary>
[Register(typeof(SandboxProviderBase), ServiceLifetime.Singleton)]
public sealed partial class DockerSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;
    private readonly ConcurrentDictionary<string, string> _containerIds = new();

    /// <summary>沙箱类型为 Docker</summary>
    public override SandboxType SandboxType => SandboxType.Docker;
    /// <summary>能力为完全隔离</summary>
    public override SandboxCapabilities Capabilities => SandboxCapabilities.FullIsolation;

    /// <summary>
    /// 初始化 Docker 沙箱提供者
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程执行服务</param>
    /// <param name="logger">可选的日志记录器</param>
    /// <param name="clock">可选的时钟服务</param>
    /// <param name="telemetryService">可选的遥测服务</param>
    public DockerSandboxProvider(
        IFileSystem fs,
        IProcessService processService,
        ILogger<DockerSandboxProvider>? logger = null,
        IClockService? clock = null,
        ITelemetryService? telemetryService = null)
        : base(fs, logger, clock ?? SystemClockService.Instance, telemetryService)
    {
        _processService = processService;
    }

    /// <summary>
    /// Docker 是否可用 — 检查 PATH 中是否存在 docker/docker.exe 可执行文件
    /// </summary>
    public override bool IsAvailable
    {
        get
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                var separator = OperatingSystem.IsWindows() ? ';' : ':';
                foreach (var dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var exePath = Path.Combine(dir, OperatingSystem.IsWindows() ? "docker.exe" : "docker");
                    if (Fs.FileExists(exePath))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    private protected override async Task OnCreateAsync(SandboxInfo info, SandboxOptions options, CancellationToken ct)
    {
        var image = options.DockerImage ?? "mcr.microsoft.com/dotnet/sdk:10.0";

        var args = new List<string> { "run", "-d" };
        args.Add("-v");
        args.Add($"{Path.GetFullPath(info.RootPath)}:/workspace");
        args.Add("-e");
        args.Add("HOME=/home/agent");
        args.Add("-e");
        args.Add("JCC_SANDBOX=1");

        if (info.RestrictNetwork)
        {
            args.Add("--network");
            args.Add("none");
        }

        if (options.MemoryLimitMb > 0)
        {
            args.Add("--memory");
            args.Add($"{options.MemoryLimitMb}m");
        }

        if (options.CpuLimitPercent > 0 && options.CpuLimitPercent <= 100)
        {
            var cpuQuota = options.CpuLimitPercent * 1000 / 100;
            args.Add("--cpu-quota");
            args.Add(cpuQuota.ToString());
            args.Add("--cpu-period");
            args.Add("100000");
        }

        args.Add("-w");
        args.Add("/workspace");
        args.Add(image);
        args.Add("sleep");
        args.Add("infinity");

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "docker",
            ArgumentList = args,
            TimeoutMs = 30000
        }, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException($"[GRD013] Docker 容器创建失败: {result.StandardError}");
        }

        var containerId = result.StandardOutput.Trim();
        _containerIds[info.SandboxId] = containerId;

        Logger?.LogInformation("[Sandbox:Docker] 容器已创建: {ContainerId}, 镜像: {Image}", containerId, image);

        await base.OnCreateAsync(info, options, ct).ConfigureAwait(false);
    }

    private protected override async Task OnDestroyAsync(SandboxInfo info, CancellationToken ct)
    {
        if (_containerIds.TryRemove(info.SandboxId, out var containerId))
        {
            try
            {
                await _processService.ExecuteAsync(new ProcessOptions
                {
                    FileName = "docker",
                    ArgumentList = new[] { "rm", "-f", containerId },
                    TimeoutMs = 10000
                }, ct).ConfigureAwait(false);

                Logger?.LogInformation("[Sandbox:Docker] 容器已移除: {ContainerId}", containerId);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "[Sandbox:Docker] 移除容器 {ContainerId} 失败", containerId);
            }
        }

        await base.OnDestroyAsync(info, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 在指定 Docker 容器内执行命令
    /// </summary>
    /// <param name="sandboxId">沙箱标识</param>
    /// <param name="command">要执行的 shell 命令</param>
    /// <param name="timeoutMs">超时毫秒数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>容器内执行结果</returns>
    public async Task<ProviderExecutionResult> ExecuteInContainerAsync(
        string sandboxId,
        string command,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        if (!_containerIds.TryGetValue(sandboxId, out var containerId))
        {
            throw new InvalidOperationException($"[GRD014] Docker 沙箱 '{sandboxId}' 容器未运行");
        }

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "docker",
            ArgumentList = new[] { "exec", containerId, "/bin/sh", "-c", ShellCommandEscape.EscapeForSingleQuotedShell(command) },
            TimeoutMs = timeoutMs
        }, ct).ConfigureAwait(false);

        return new ProviderExecutionResult
        {
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ExitCode = result.ExitCode,
            Success = result.Success,
            TimedOut = !result.Success && result.ExitCode == -1
        };
    }
}
