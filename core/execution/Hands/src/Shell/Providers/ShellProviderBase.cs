namespace Services.Shell.Providers;

/// <summary>
/// Shell 执行器实体 — 继承 ToolExecutionEntity，短命（每次命令执行创建）
/// 引用 ShellCapability（长命缓存）获取版本/路径等静态属性
/// 新增 Shell 不需要改此基类，只需新增子类 + ShellCapability
/// </summary>
public abstract class ShellProviderBase : ToolExecutionEntity, IShellProvider
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    /// <summary>
    /// 执行器能力描述 — 长命缓存，版本/路径/编码等只检测一次
    /// </summary>
    public ShellCapability Capability { get; }

    /// <inheritdoc />
    public ShellType Type => Capability.Type;

    /// <inheritdoc />
    public string ShellPath => Capability.ShellPath;

    /// <inheritdoc />
    public string Version => Capability.Version;

    /// <inheritdoc />
    public bool Detached => Capability.Detached;

    /// <inheritdoc />
    public Encoding OutputEncoding => Capability.OutputEncoding;

    /// <inheritdoc />
    public Encoding ErrorEncoding => Capability.ErrorEncoding;

    protected IFileSystem Fs => _fs;
    protected ILogger? Logger => _logger;

    protected ShellProviderBase(
        ShellCapability capability,
        IFileSystem fs,
        ILogger? logger = null,
        string? toolUseId = null,
        string? spanId = null)
        : base(ObjectType.Executor, capability.Type.ToValue(), toolUseId, spanId, capability.DisplayName)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    #region IShellProvider — 子类必须实现

    /// <inheritdoc />
    public abstract Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command, ShellExecOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract string[] GetSpawnArgs(string commandString);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command, CancellationToken cancellationToken = default)
    {
        var env = CreateBaseEnvironment();
        AppendExtraEnvironmentVariables(env, command);
        Logger?.LogDebug("{Type}: injected {Count} environment overrides", Type, env.Count);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
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

    protected virtual void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command) { }

    #endregion

    #region 通用工具方法

    protected string? FindExecutable(string executable, bool excludeCurrentDir = true)
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

    protected string? ResolveFromEnvVar(string envVarName)
    {
        var envPath = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envPath) && _fs.FileExists(envPath))
            return envPath;
        return null;
    }

    protected string? FindInCommonPaths(params string[] paths)
    {
        foreach (var p in paths)
            if (_fs.FileExists(p)) return p;
        return null;
    }

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

    protected string? ExecuteShellCommand(string fileName, string arguments, int timeoutMs = 5000)
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
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// ShellCapability 构建器 — 长命缓存，子类在 DI 单例中调用一次
/// </summary>
public abstract class ShellCapabilityProvider
{
    private ShellCapability? _cached;

    /// <summary>
    /// 获取或创建 ShellCapability — 首次调用时检测，后续返回缓存
    /// </summary>
    public ShellCapability GetCapability(IFileSystem fs, ILogger? logger = null)
    {
        if (_cached is not null) return _cached;

        var shellPath = ResolveShellPath(fs, logger);
        var version = DetectVersion(shellPath, logger);
        var displayName = BuildDisplayName(shellPath, version);

        _cached = new ShellCapability
        {
            Type = GetShellType(),
            ShellPath = shellPath,
            Version = version,
            DisplayName = displayName,
            Detached = IsDetached(),
            OutputEncoding = GetOutputEncoding(),
            ErrorEncoding = GetErrorEncoding(),
            IsPowerShellCore = DetectIsPowerShellCore(shellPath, version),
        };

        return _cached;
    }

    protected abstract ShellType GetShellType();
    protected abstract string ResolveShellPath(IFileSystem fs, ILogger? logger);
    protected abstract string DetectVersion(string shellPath, ILogger? logger);
    protected virtual bool IsDetached() => false;
    protected virtual Encoding GetOutputEncoding() => Encoding.UTF8;
    protected virtual Encoding GetErrorEncoding() => GetOutputEncoding();
    protected virtual bool DetectIsPowerShellCore(string shellPath, string version) => false;

    /// <summary>
    /// 创建短命执行器实例 — 每次命令执行时调用，返回 ShellProviderBase 子类
    /// </summary>
    public abstract ShellProviderBase CreateProvider(
        ShellCapability capability, IFileSystem fs, ILogger? logger = null);

    protected virtual string BuildDisplayName(string shellPath, string version)
    {
        var typeName = GetShellType().ToValue();
        return $"{typeName} {version}";
    }
}
