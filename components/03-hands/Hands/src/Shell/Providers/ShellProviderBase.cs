namespace Services.Shell.Providers;

/// <summary>
/// Shell 提供者抽象基类 — 模板方法模式
/// 封装所有 Shell 类型的通用逻辑：路径解析、版本检测、环境变量注入、编码控制
/// 子类只需实现差异化的钩子方法，新增通用功能（如 UTF8 编码控制）只需改父类
/// </summary>
public abstract class ShellProviderBase : IShellProvider
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    /// <inheritdoc />
    public abstract ShellType Type { get; }

    /// <inheritdoc />
    public string ShellPath { get; }

    /// <inheritdoc />
    public abstract bool Detached { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <summary>
    /// 标准输出编码 — 对齐 TS Shell.ts stdoutEncoding
    /// 子类可覆盖以指定特定编码（如 PowerShell 可能需要 UTF-8 BOM）
    /// </summary>
    public virtual Encoding OutputEncoding => Encoding.UTF8;

    /// <summary>
    /// 标准错误编码 — 默认与 OutputEncoding 相同
    /// </summary>
    public virtual Encoding ErrorEncoding => OutputEncoding;

    protected IFileSystem Fs => _fs;
    protected ILogger? Logger => _logger;

    protected ShellProviderBase(IFileSystem fs, string? shellPath, ILogger? logger)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        ShellPath = shellPath ?? ResolveShellPath();
        Version = DetectVersion();
    }

    #region 钩子方法 — 子类必须实现

    /// <summary>
    /// 解析 Shell 可执行文件路径 — 子类定义回退策略
    /// 环境变量 → PATH 查找 → 常见安装路径 → 回退
    /// </summary>
    protected abstract string ResolveShellPath();

    /// <summary>
    /// 检测 Shell 版本 — 子类定义版本检测命令和解析逻辑
    /// </summary>
    protected abstract string DetectVersion();

    /// <inheritdoc />
    public abstract Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command, ShellExecOptions options, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract string[] GetSpawnArgs(string commandString);

    #endregion

    #region 环境变量 — 模板方法

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command, CancellationToken cancellationToken = default)
    {
        var env = CreateBaseEnvironment();

        AppendExtraEnvironmentVariables(env, command);

        Logger?.LogDebug("{Type}: injected {Count} environment overrides", Type, env.Count);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    /// <summary>
    /// 创建基础环境变量字典 — 所有 Shell 共享
    /// CLAUDECODE=1 + GIT_EDITOR=true
    /// </summary>
    private static Dictionary<string, string> CreateBaseEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDECODE"] = "1",
            ["GIT_EDITOR"] = "true"
        };
    }

    /// <summary>
    /// 追加特有环境变量 — 子类覆盖以添加 Shell 特定的环境变量
    /// </summary>
    protected virtual void AppendExtraEnvironmentVariables(
        Dictionary<string, string> env, string command) { }

    #endregion

    #region 通用工具方法

    /// <summary>
    /// 三步路径解析模板 — 对齐 TS Shell.ts resolveShellPath
    /// 环境变量 → PATH 查找 → 常见安装路径 → 回退
    /// 子类调用此方法替代重复的三步 if-else 链
    /// </summary>
    protected string ResolveShellPathFromCandidates(
        string envVarName,
        string pathExecutable,
        string[] commonPaths,
        string fallback,
        bool excludeCurrentDir = true)
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
    /// 查找可执行文件 — 对齐 TS findExecutable (where.exe)
    /// </summary>
    /// <param name="executable">可执行文件名</param>
    /// <param name="excludeCurrentDir">是否排除当前目录下的结果（Bash 需要防止误用项目本地 exe）</param>
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
            {
                return paths.Length > 0 ? paths[0].Trim() : null;
            }

            var cwd = _fs.GetCurrentDirectory().ToLowerInvariant();

            foreach (var candidate in paths)
            {
                var normalized = Path.GetFullPath(candidate.Trim()).ToLowerInvariant();
                var dir = Path.GetDirectoryName(normalized)!;
                if (!dir.Equals(cwd, StringComparison.OrdinalIgnoreCase) &&
                    !normalized.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.Trim();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查环境变量指定的路径是否存在 — 通用路径解析第一步
    /// </summary>
    protected string? ResolveFromEnvVar(string envVarName)
    {
        var envPath = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envPath) && _fs.FileExists(envPath))
        {
            return envPath;
        }
        return null;
    }

    /// <summary>
    /// 检查常见安装路径 — 通用路径解析第三步
    /// </summary>
    protected string? FindInCommonPaths(params string[] paths)
    {
        foreach (var p in paths)
        {
            if (_fs.FileExists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 执行 Shell 命令并读取输出 — 版本检测通用模式
    /// </summary>
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
