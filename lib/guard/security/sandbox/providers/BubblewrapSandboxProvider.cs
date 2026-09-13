namespace Core.Security.Sandbox.Providers;


/// <summary>
/// Bubblewrap (bwrap) 沙箱提供者 — Linux 下基于用户命名空间的轻量沙箱,提供文件系统、网络、进程隔离
/// </summary>
[Register(typeof(SandboxProviderBase), ServiceLifetime.Singleton)]
public sealed partial class BubblewrapSandboxProvider : SandboxProviderBase
{
    private readonly IProcessService _processService;

    /// <summary>沙箱类型 — Bubblewrap</summary>
    public override SandboxType SandboxType => SandboxType.Bubblewrap;
    /// <summary>沙箱能力 — 路径重定向、文件系统隔离、网络隔离、进程隔离、用户命名空间</summary>
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.NetworkIsolation | SandboxCapabilities.ProcessIsolation | SandboxCapabilities.UserNamespace;

    /// <summary>
    /// 构造 Bubblewrap 沙箱提供者
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程执行服务,用于启动 bwrap 子进程</param>
    /// <param name="logger">日志记录器(可选)</param>
    /// <param name="clock">时钟服务(可选),默认使用系统时钟</param>
    /// <param name="telemetryService">遥测服务(可选)</param>
    public BubblewrapSandboxProvider(
        IFileSystem fs,
        IProcessService processService,
        ILogger<BubblewrapSandboxProvider>? logger = null,
        IClockService? clock = null,
        ITelemetryService? telemetryService = null)
        : base(fs, logger, clock ?? SystemClockService.Instance, telemetryService)
    {
        _processService = processService;
    }

    /// <summary>
    /// 沙箱是否可用 — 仅 Linux 平台且 PATH 中存在 bwrap 可执行文件时返回 true
    /// </summary>
    public override bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Fs.FileExists(Path.Combine(dir, "bwrap")))
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

    /// <summary>
    /// 在 Bubblewrap 沙箱中异步执行命令 — 构造 bwrap 参数并启动子进程,绑定工作区与允许路径
    /// </summary>
    /// <param name="sandboxId">沙箱实例标识</param>
    /// <param name="command">待执行的 Shell 命令</param>
    /// <param name="timeoutMs">执行超时时间(毫秒),默认 30000</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>沙箱执行结果,包含标准输出、标准错误、退出码与成功标志</returns>
    public async Task<ProviderExecutionResult> ExecuteInSandboxAsync(
        string sandboxId,
        string command,
        int timeoutMs = 30000,
        CancellationToken ct = default)
    {
        var info = GetSandboxInfo(sandboxId)
            ?? throw new InvalidOperationException($"[GRD012] Bubblewrap 沙箱 '{sandboxId}' 不存在");

        var rootPath = Path.GetFullPath(info.RootPath);

        var bwrapArgs = new List<string> { "--unshare-all", "--die-with-parent" };

        if (!info.RestrictNetwork)
        {
            bwrapArgs.Add("--share-net");
        }

        bwrapArgs.AddRange(new[] { "--bind", rootPath, "/workspace" });
        bwrapArgs.AddRange(new[] { "--proc", "/proc" });
        bwrapArgs.AddRange(new[] { "--dev", "/dev" });
        bwrapArgs.AddRange(new[] { "--tmpfs", "/tmp" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/usr", "/usr" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/lib", "/lib" });
        if (Fs.DirectoryExists("/lib64"))
            bwrapArgs.AddRange(new[] { "--ro-bind", "/lib64", "/lib64" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/bin", "/bin" });
        if (Fs.DirectoryExists("/sbin"))
            bwrapArgs.AddRange(new[] { "--ro-bind", "/sbin", "/sbin" });

        if (info.AllowedPaths is not null)
        {
            foreach (var allowed in info.AllowedPaths)
            {
                var fullAllowed = Path.GetFullPath(allowed);
                if (Fs.DirectoryExists(fullAllowed))
                {
                    bwrapArgs.AddRange(new[] { "--bind", fullAllowed, fullAllowed });
                }
                else if (Fs.FileExists(fullAllowed))
                {
                    bwrapArgs.AddRange(new[] { "--ro-bind", fullAllowed, fullAllowed });
                }
            }
        }

        bwrapArgs.AddRange(new[] { "--", "/bin/sh", "-c", ShellCommandEscape.EscapeForSingleQuotedShell(command) });

        var result = await _processService.ExecuteAsync(new ProcessOptions
        {
            FileName = "bwrap",
            ArgumentList = bwrapArgs,
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
