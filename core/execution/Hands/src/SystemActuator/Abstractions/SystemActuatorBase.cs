namespace Services.SystemActuator;

/// <summary>
/// 系统执行器抽象基类 — 合并原 ProviderBase + CapabilityProvider + ExecutionService
/// 含：静态能力缓存 + 命令构建 + 执行编排 + 环境变量 + 工具方法
/// 子类只需重写命令构建 + 能力检测，无需改基类
/// </summary>
public abstract class SystemActuatorBase : ToolExecutionEntity, ISystemActuator
{
    private static readonly ConcurrentDictionary<SystemActuatorKind, SystemActuatorCapability> _capabilityCache = new();

    private readonly SystemActuatorKind _kind;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly ISandboxManager? _sandboxManager;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ShellExecutionConfig? _config;

    /// <summary>
    /// 执行器能力描述 — 从静态缓存读取
    /// </summary>
    public SystemActuatorCapability Capability { get; }

    /// <inheritdoc />
    public SystemActuatorKind Kind => _kind;

    /// <inheritdoc />
    public string ShellPath => Capability.ShellPath;

    /// <inheritdoc />
    public string Version => Capability.Version;

    /// <inheritdoc />
    public new string DisplayName => Capability.DisplayName;

    /// <inheritdoc />
    public bool Detached => Capability.Detached;

    /// <inheritdoc />
    public Encoding OutputEncoding => Capability.OutputEncoding;

    /// <inheritdoc />
    public Encoding ErrorEncoding => Capability.ErrorEncoding;

    /// <summary>
    /// 文件系统
    /// </summary>
    protected IFileSystem Fs => _fs;

    /// <summary>
    /// 日志
    /// </summary>
    protected ILogger? Logger => _logger;

    /// <summary>
    /// 沙箱管理器
    /// </summary>
    protected ISandboxManager? SandboxManager => _sandboxManager;

    /// <summary>
    /// 防休眠服务
    /// </summary>
    protected IPreventSleepService? PreventSleepService => _preventSleepService;

    /// <summary>
    /// 执行配置
    /// </summary>
    protected ShellExecutionConfig? Config => _config;

    protected SystemActuatorBase(
        SystemActuatorKind kind,
        IFileSystem fs,
        ILogger? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(ObjectType.Executor, kind.Id, toolUseId, spanId, GetCachedCapability(kind).DisplayName)
    {
        _kind = kind;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _sandboxManager = sandboxManager;
        _preventSleepService = preventSleepService;
        _config = config;
        Capability = GetCachedCapability(kind);
    }

    #region 静态能力缓存

    /// <summary>
    /// 从静态缓存获取能力描述 — 未缓存时抛异常
    /// </summary>
    private static SystemActuatorCapability GetCachedCapability(SystemActuatorKind kind)
    {
        if (_capabilityCache.TryGetValue(kind, out var cap)) return cap;
        throw new InvalidOperationException(
            $"SystemActuatorCapability not initialized for {kind.Id}. Call SystemActuatorRegistry.Initialize() first.");
    }

    /// <summary>
    /// 注册能力描述到静态缓存 — 由 SystemActuatorRegistry.Initialize 调用
    /// </summary>
    internal static void RegisterCapability(SystemActuatorCapability capability)
    {
        _capabilityCache[capability.Kind] = capability;
    }

    /// <summary>
    /// 重置缓存 — 仅用于测试
    /// </summary>
    internal static void ResetCapabilityCache()
    {
        _capabilityCache.Clear();
    }

    #endregion

    #region ISystemActuator — 子类必须实现

    /// <inheritdoc />
    public abstract Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract string[] GetSpawnArgs(string commandString);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command, CancellationToken cancellationToken = default)
    {
        var env = CreateBaseEnvironment();
        AppendExtraEnvironmentVariables(env, command);
        Logger?.LogDebug("{Kind}: injected {Count} environment overrides", Kind, env.Count);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    #endregion

    #region 执行逻辑（原 ShellExecutionService）

    /// <inheritdoc />
    public async Task<SystemActuatorExecutionResult> ExecuteAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return SystemActuatorExecutionResult.FailureResult("Command cannot be empty");

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
                return SystemActuatorExecutionResult.FailureResult($"Working directory does not exist: {cwd}");
        }

        _logger?.LogInformation("Executing {Kind} command: {Command}", Kind, command);

        if (_preventSleepService is not null)
            await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);
        try
        {
            var useSandbox = !disableSandbox && _sandboxManager is not null && _sandboxManager.IsInSandbox;
            var sandboxTmpDir = useSandbox
                ? (_sandboxManager ?? throw new InvalidOperationException("SandboxManager not available.")).CurrentSandbox?.RootPath
                : null;

            await using var context = await SystemActuatorCommandContext.StartAsync(
                command, cwd, _fs, this, timeout,
                shouldAutoBackground: false, useSandbox, sandboxTmpDir, _logger).ConfigureAwait(false);

            var result = await context.ResultTask.ConfigureAwait(false);

            _logger?.LogInformation(
                "{Kind} completed: ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}",
                Kind, result.ExitCode, result.Stdout?.Length ?? 0, result.Stderr?.Length ?? 0);

            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Kind} execution failed: {Command}", Kind, command);
            return SystemActuatorExecutionResult.FailureResult(ex.Message);
        }
        finally
        {
            if (_preventSleepService is not null)
                await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ISystemActuatorCommandContext> StartWithBackgroundSupportAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool shouldAutoBackground = true,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be empty", nameof(command));

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
                throw new DirectoryNotFoundException($"Working directory does not exist: {cwd}");
        }

        _logger?.LogInformation("Starting backgroundable command with {Kind}: {Command}", Kind, command);

        if (_preventSleepService is not null)
            await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);

        var useSandbox = !disableSandbox && _sandboxManager is not null && _sandboxManager.IsInSandbox;
        var sandboxTmpDir = useSandbox
            ? (_sandboxManager ?? throw new InvalidOperationException("SandboxManager not available.")).CurrentSandbox?.RootPath
            : null;

        var context = await SystemActuatorCommandContext.StartAsync(
            command, cwd, _fs, this, timeout,
            shouldAutoBackground, useSandbox, sandboxTmpDir, _logger).ConfigureAwait(false);

        _ = context.ResultTask.ContinueWith(async _ =>
        {
            if (_preventSleepService is not null)
                await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }, TaskScheduler.Default);

        return context;
    }

    private string ResolveWorkingDirectory(string? workingDirectory, bool disableSandbox)
    {
        var cwd = string.IsNullOrEmpty(workingDirectory)
            ? _fs.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);

        if (!disableSandbox && _sandboxManager != null && _sandboxManager.IsInSandbox)
            cwd = _sandboxManager.ResolvePath(cwd);

        return cwd;
    }

    #endregion

    #region 环境变量

    private static Dictionary<string, string> CreateBaseEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDECODE"] = "1",
            ["GIT_EDITOR"] = "true"
        };
    }

    /// <summary>
    /// 追加额外环境变量 — 子类重写以注入特定环境变量
    /// </summary>
    protected virtual void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command) { }

    #endregion

    #region 通用工具方法（原 ShellProviderBase）

    /// <summary>
    /// 在 PATH 中查找可执行文件
    /// </summary>
    protected string? FindExecutable(string executable, bool excludeCurrentDir = true)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add(executable);

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0) return null;

            var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            if (!excludeCurrentDir)
                return paths.Length > 0 ? paths[0].Trim() : null;

            var cwd = _fs.GetCurrentDirectory().ToLowerInvariant();

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
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从环境变量解析路径
    /// </summary>
    protected string? ResolveFromEnvVar(string envVarName)
    {
        var envPath = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envPath) && _fs.FileExists(envPath))
            return envPath;
        return null;
    }

    /// <summary>
    /// 在常见路径中查找文件
    /// </summary>
    protected string? FindInCommonPaths(params string[] paths)
    {
        foreach (var p in paths)
            if (_fs.FileExists(p)) return p;
        return null;
    }

    /// <summary>
    /// 从候选路径解析执行器路径
    /// </summary>
    protected string ResolveShellPathFromCandidates(
        string envVarName, string pathExecutable, string[] commonPaths, string fallback, bool excludeCurrentDir = true)
    {
        var envPath = ResolveFromEnvVar(envVarName);
        if (envPath is not null) return envPath;

        var fromPath = FindExecutable(pathExecutable, excludeCurrentDir);
        if (fromPath is not null) return fromPath;

        var commonPath = FindInCommonPaths(commonPaths);
        if (commonPath is not null) return commonPath;

        Logger?.LogWarning("Shell not found via {EnvVar}, PATH, or common paths. Falling back to {Fallback}.", envVarName, fallback);
        return fallback;
    }

    /// <summary>
    /// 执行命令行并返回输出
    /// </summary>
    protected string? ExecuteShellCommand(string fileName, IReadOnlyList<string> args, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(timeoutMs);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 跨会话克隆 — 执行器是服务组件，不应跨会话克隆

    /// <summary>
    /// 执行器是可复用的服务组件（持有 IFileSystem/ILogger 等依赖），不是数据实体
    /// 跨会话时应通过 SystemActuatorRegistry.Get() 在目标会话创建新实例，而非克隆
    /// </summary>
    public override Entity Clone(CloneContext context)
        => throw new NotSupportedException(
            $"{GetType().Name} 是服务执行器组件，不应跨会话克隆。" +
            $"请在新会话中通过 SystemActuatorRegistry.Get(Kind) 创建新实例。");

    /// <summary>
    /// 回收判定 — 执行器是服务组件，放宽条件：非 Disposed 且超过 5 分钟无活动即可回收
    /// 避免每次 Registry.Get() 创建的执行器实例在 SessionScope 中无限堆积
    /// </summary>
    public override bool CanReclaim()
    {
        return LifecycleState != EntityLifecycle.Disposed
            && DateTime.UtcNow - LastActivityAt > SystemActuatorReclaimTimeout;
    }

    /// <summary>执行器回收超时 — 5 分钟无活动</summary>
    private static readonly TimeSpan SystemActuatorReclaimTimeout = TimeSpan.FromMinutes(5);

    #endregion
}
