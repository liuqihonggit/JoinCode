namespace Services.Shell.Providers;

/// <summary>
/// Bash Shell 提供者 — 对齐 TS BashProvider
/// 在 Windows 上使用 Git Bash bash.exe 执行命令
/// 支持 CWD 追踪、环境变量注入、extglob 禁用
/// </summary>
[Register]
public sealed class BashShellProvider : IShellProvider
{
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;

    public ShellProviderType Type => ShellProviderType.Bash;
    public string ShellPath { get; }
    public bool Detached => true;
    public string Version { get; }

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_GIT_BASH_PATH
    /// </summary>
    public const string GitBashPathEnvVar = "JCC_GIT_BASH_PATH";

    /// <summary>
    /// 环境变量名 — 对齐 TS CLAUDE_CODE_SHELL_PREFIX
    /// </summary>
    public const string ShellPrefixEnvVar = "JCC_SHELL_PREFIX";

    /// <summary>
    /// 安全环境变量白名单 — 对齐 TS BashProvider SafeEnvVars
    /// </summary>
    internal static readonly FrozenSet<string> SafeEnvVars = new[]
    {
        "NODE_ENV", "PYTHONUNBUFFERED", "PYTHONIOENCODING", "LANG",
        "LC_ALL", "LC_CTYPE", "TERM", "COLORTERM", "NO_COLOR",
        "FORCE_COLOR", "CLICOLOR", "CLICOLOR_FORCE"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public BashShellProvider(IFileSystem fs, string? shellPath = null, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        ShellPath = shellPath ?? FindGitBashPath();
        Version = DetectBashVersion();
    }

    /// <inheritdoc />
    public Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command,
        ShellExecOptions options,
        CancellationToken cancellationToken = default)
    {
        var tmpDir = Path.GetTempPath();
        var shellTmpDir = WindowsPathToPosixPath(tmpDir);

        var shellCwdFilePath = options.UseSandbox && options.SandboxTmpDir is not null
            ? PosixJoin(options.SandboxTmpDir, $"cwd-{options.SessionId}")
            : PosixJoin(shellTmpDir, $"jcc-{options.SessionId}-cwd");

        var cwdFilePath = options.UseSandbox && options.SandboxTmpDir is not null
            ? PosixJoin(options.SandboxTmpDir, $"cwd-{options.SessionId}")
            : Path.Combine(tmpDir, $"jcc-{options.SessionId}-cwd");

        var normalizedCommand = RewriteWindowsNullRedirect(command);

        var commandParts = new List<string>(4);

        var disableExtglobCmd = GetDisableExtglobCommand();
        if (disableExtglobCmd is not null)
        {
            commandParts.Add(disableExtglobCmd);
        }

        commandParts.Add($"eval {ShellQuote(normalizedCommand)}");
        commandParts.Add($"pwd -P >| {ShellQuote(shellCwdFilePath)}");

        var commandString = string.Join(" && ", commandParts);

        var shellPrefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(shellPrefix))
        {
            commandString = $"{shellPrefix} {ShellQuote(commandString)}";
        }

        _logger?.LogDebug("BashShellProvider: built command for session {SessionId}", options.SessionId);

        return Task.FromResult(new ShellExecCommandResult
        {
            CommandString = commandString,
            CwdFilePath = cwdFilePath
        });
    }

    /// <inheritdoc />
    public string[] GetSpawnArgs(string commandString) => ["-c", commandString];

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDECODE"] = "1",
            ["GIT_EDITOR"] = "true"
        };

        if (Environment.GetEnvironmentVariable("SHELL") is null)
        {
            env["SHELL"] = ShellPath;
        }

        _logger?.LogDebug("BashShellProvider: injected {Count} environment overrides", env.Count);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    /// <summary>
    /// 查找 Git Bash 路径 — 对齐 TS findGitBashPath
    /// 1. JCC_GIT_BASH_PATH 环境变量
    /// 2. 从 git.exe 推导 bash.exe 路径
    /// 3. 常见安装路径
    /// 4. 回退到 cmd.exe
    /// </summary>
    private string FindGitBashPath()
    {
        var envPath = Environment.GetEnvironmentVariable(GitBashPathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && _fs.FileExists(envPath))
        {
            return envPath;
        }

        var gitPath = FindExecutable("git.exe");
        if (gitPath is not null)
        {
            var bashFromGit = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(gitPath))!,
                "bin", "bash.exe");
            if (_fs.FileExists(bashFromGit))
            {
                return bashFromGit;
            }
        }

        var commonPaths = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        };

        foreach (var p in commonPaths)
        {
            if (_fs.FileExists(p)) return p;
        }

        _logger?.LogWarning("Git Bash not found, falling back to cmd.exe. Set {EnvVar} to specify bash path.", GitBashPathEnvVar);
        return "cmd.exe";
    }

    /// <summary>
    /// 查找可执行文件 — 对齐 TS findExecutable (where.exe)
    /// </summary>
    private string? FindExecutable(string executable)
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
    /// 禁用 extglob 命令 — 对齐 TS getDisableExtglobCommand
    /// </summary>
    private static string? GetDisableExtglobCommand()
    {
        var prefix = Environment.GetEnvironmentVariable(ShellPrefixEnvVar);
        if (!string.IsNullOrEmpty(prefix))
        {
            return "{ shopt -u extglob || setopt NO_EXTENDED_GLOB; } >/dev/null 2>&1 || true";
        }

        return "shopt -u extglob 2>/dev/null || true";
    }

    /// <summary>
    /// 重写 Windows 风格的 null 重定向 — 对齐 TS rewriteWindowsNullRedirect
    /// 将 `2>nul` 转换为 `2>/dev/null`，避免在 Git Bash 中创建名为 `nul` 的文件
    /// </summary>
    private static string RewriteWindowsNullRedirect(string command)
    {
        if (!OperatingSystem.IsWindows()) return command;
        return Regex.Replace(command, @"2>\s*nul\b", "2>/dev/null", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Shell 单引号引用 — 对齐 TS shellQuote
    /// </summary>
    private static string ShellQuote(string s)
        => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Windows 路径转 POSIX 路径 — 对齐 TS windowsPathToPosixPath
    /// </summary>
    internal static string WindowsPathToPosixPath(string windowsPath)
    {
        if (windowsPath.StartsWith("\\\\"))
        {
            return windowsPath.Replace('\\', '/');
        }

        var match = Regex.Match(windowsPath, @"^([A-Za-z]):[/\\]");
        if (match.Success)
        {
            var drive = match.Groups[1].Value.ToLowerInvariant();
            return '/' + drive + windowsPath[2..].Replace('\\', '/');
        }

        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// POSIX 路径拼接 — 对齐 TS path/posix.join
    /// </summary>
    private static string PosixJoin(params string[] segments)
    {
        var result = string.Join('/', segments.Select(s => s.TrimEnd('/')));
        return result;
    }

    /// <summary>
    /// 检测 Bash 版本 — 对齐 TS bash --version
    /// </summary>
    private string DetectBashVersion()
    {
        if (ShellPath.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "cmd-fallback";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ShellPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null) return "unknown";

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var match = Regex.Match(output, @"version\s+(\S+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
