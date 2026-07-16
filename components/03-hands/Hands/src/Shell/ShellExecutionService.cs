
using Services.Shell.Providers;

namespace Services.Shell;

/// <summary>
/// Shell 执行服务实现 — 使用 IShellProvider 替代硬编码的 cmd.exe/powershell.exe
/// </summary>
[Register]
public sealed partial class ShellExecutionService : IShellExecutionService
{
    [Inject] private readonly ILogger<ShellExecutionService>? _logger;
    [Inject] private readonly IProcessService _processService;
    private readonly ShellExecutionConfig _config;
    private readonly IFileSystem _fs;
    private readonly ISandboxModeService? _sandboxModeService;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly IShellProvider _bashProvider;
    private readonly IShellProvider _powerShellProvider;

    public ShellExecutionService(
        ShellExecutionConfig config,
        IFileSystem fs,
        IProcessService processService,
        BashShellProvider bashProvider,
        PowerShellShellProvider powerShellProvider,
        ILogger<ShellExecutionService>? logger = null,
        ISandboxModeService? sandboxModeService = null,
        IPreventSleepService? preventSleepService = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _bashProvider = bashProvider ?? throw new ArgumentNullException(nameof(bashProvider));
        _powerShellProvider = powerShellProvider ?? throw new ArgumentNullException(nameof(powerShellProvider));
        _logger = logger;
        _sandboxModeService = sandboxModeService;
        _preventSleepService = preventSleepService;
    }

    /// <inheritdoc />
    public Task<ShellExecutionResult> ExecuteAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, _bashProvider,
            "Bash", cancellationToken);

    /// <inheritdoc />
    public Task<ShellExecutionResult> ExecutePowerShellAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, _powerShellProvider,
            "PowerShell", cancellationToken);

    /// <inheritdoc />
    public async Task<IShellCommandContext> StartWithBackgroundSupportAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool isPowerShell = false,
        bool shouldAutoBackground = true,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be empty", nameof(command));
        }

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        // 对齐 TS Shell.ts: CWD 不存在时回退到项目根目录而非抛异常
        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
            {
                throw new DirectoryNotFoundException($"Working directory does not exist: {cwd}");
            }
        }

        var provider = isPowerShell ? _powerShellProvider : _bashProvider;

        _logger?.LogInformation("Starting backgroundable command with {ProviderType}: {Command}", provider.Type, command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);

        var useSandbox = !disableSandbox && _sandboxModeService is not null && _sandboxModeService.IsInSandbox;
        var sandboxTmpDir = useSandbox ? (_sandboxModeService ?? throw new InvalidOperationException("SandboxModeService not available.")).CurrentSandbox?.RootPath : null;

        var context = await ShellCommandContext.StartAsync(
            command,
            cwd,
            _fs,
            provider,
            timeout,
            shouldAutoBackground,
            useSandbox,
            sandboxTmpDir,
            _logger).ConfigureAwait(false);

        _ = context.ResultTask.ContinueWith(async _ =>
        {
            if (_preventSleepService is not null) await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }, TaskScheduler.Default);

        return context;
    }

    #region Core Execution

    private async Task<ShellExecutionResult> ExecuteCoreAsync(
        string command,
        int? timeout,
        string? workingDirectory,
        bool disableSandbox,
        IShellProvider provider,
        string shellLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ShellExecutionResult.FailureResult("Command cannot be empty");
        }

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        if (!_fs.DirectoryExists(cwd))
        {
            _logger?.LogWarning("工作目录不存在: {Cwd}，回退到项目根目录", cwd);
            cwd = _fs.GetCurrentDirectory();
            if (!_fs.DirectoryExists(cwd))
            {
                return ShellExecutionResult.FailureResult($"Working directory does not exist: {cwd}");
            }
        }

        _logger?.LogInformation("Executing {Shell} command: {Command}", shellLabel, command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);
        try
        {
            var useSandbox = !disableSandbox && _sandboxModeService is not null && _sandboxModeService.IsInSandbox;
            var sandboxTmpDir = useSandbox ? (_sandboxModeService ?? throw new InvalidOperationException("SandboxModeService not available.")).CurrentSandbox?.RootPath : null;

            var sessionId = Guid.NewGuid().ToString("N")[..8];
            var execOptions = new ShellExecOptions
            {
                SessionId = sessionId,
                UseSandbox = useSandbox,
                SandboxTmpDir = sandboxTmpDir,
            };

            var execResult = await provider.BuildExecCommandAsync(command, execOptions, cancellationToken).ConfigureAwait(false);
            var envOverrides = await provider.GetEnvironmentOverridesAsync(command, cancellationToken).ConfigureAwait(false);

            var spawnArgs = provider.GetSpawnArgs(execResult.CommandString);

            // 对齐 TS Shell.ts: 从当前进程环境复制，清理敏感变量，再叠加 envOverrides
            var mergedEnv = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in Environment.GetEnvironmentVariables().Keys)
            {
                if (key is string k)
                {
                    var v = Environment.GetEnvironmentVariable(k);
                    if (v is not null) mergedEnv[k] = v;
                }
            }
            mergedEnv = SubprocessEnvCleaner.ScrubDictionaryEnv(mergedEnv);
            foreach (var (key, value) in envOverrides)
                mergedEnv[key] = value;

            var outputEncoding = provider is ShellProviderBase spb ? spb.OutputEncoding : Encoding.UTF8;
            var errorEncoding = provider is ShellProviderBase spb2 ? spb2.ErrorEncoding : Encoding.UTF8;

            var options = new ProcessOptions
            {
                FileName = provider.ShellPath,
                Arguments = string.Join(' ', spawnArgs),
                WorkingDirectory = cwd,
                EnvironmentVariables = mergedEnv.Count > 0 ? mergedEnv : null,
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = errorEncoding,
                TimeoutMs = timeout
            };

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == -1 && timeout.HasValue)
            {
                return ShellExecutionResult.TimeoutResult(timeout.Value);
            }

            var rawStdout = result.StandardOutput;
            var rawStderr = result.StandardError;

            string? persistedPath = null;
            long? persistedSize = null;

            if (rawStdout.Length > ShellExecutionResult.MaxInlineOutputChars)
            {
                (persistedPath, persistedSize) = await PersistLargeOutputAsync(
                    rawStdout, command, cancellationToken).ConfigureAwait(false);
                rawStdout = rawStdout[..Math.Min(rawStdout.Length, ShellExecutionResult.PreviewSizeBytes)];
            }

            var stdout = rawStdout;
            var stderr = rawStderr;

            var cwdWasReset = ResetCwdIfOutsideProject(cwd);

            _logger?.LogInformation(
                "{LogLabel} completed: ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}",
                shellLabel,
                result.ExitCode,
                stdout.Length,
                stderr.Length);

            return ShellExecutionResult.SuccessResult(stdout, stderr, result.ExitCode)
                with
            {
                PersistedOutputPath = persistedPath,
                PersistedOutputSize = persistedSize,
                CwdWasReset = cwdWasReset,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{LogLabel} execution failed: {Command}", shellLabel, command);
            return ShellExecutionResult.FailureResult(ex.Message);
        }
        finally
        {
            if (_preventSleepService is not null) await _preventSleepService.AllowSleepAsync().ConfigureAwait(false);
        }
    }

    private string ResolveWorkingDirectory(string? workingDirectory, bool disableSandbox)
    {
        var cwd = string.IsNullOrEmpty(workingDirectory)
            ? _fs.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);

        if (!disableSandbox && _sandboxModeService != null && _sandboxModeService.IsInSandbox)
        {
            cwd = _sandboxModeService.ResolvePath(cwd);
        }

        return cwd;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 大输出持久化到磁盘 — 对齐 TS BashTool 大输出磁盘持久化
    /// 输出超过 30K 字符时保存到临时文件，返回文件路径和大小
    /// </summary>
    private async Task<(string? Path, long? Size)> PersistLargeOutputAsync(
        string output, string command, CancellationToken cancellationToken)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jcc-tool-results");
            DirectoryHelper.EnsureDirectoryExists(_fs, tempDir);

            var taskId = Guid.NewGuid().ToString("N")[..12];
            var filePath = Path.Combine(tempDir, $"{taskId}.txt");

            await _fs.WriteAllTextAsync(filePath, output, cancellationToken).ConfigureAwait(false);

            var fileSize = _fs.GetFileLength(filePath);

            // 超过硬上限则截断 — 对齐 TS MAX_PERSISTED_SIZE (64MB)
            if (fileSize > ShellExecutionResult.MaxPersistedSizeBytes)
            {
                var truncated = output[..(int)ShellExecutionResult.MaxPersistedSizeBytes];
                await _fs.WriteAllTextAsync(filePath, truncated, cancellationToken).ConfigureAwait(false);
                fileSize = ShellExecutionResult.MaxPersistedSizeBytes;
            }

            return (filePath, fileSize);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// CWD 越界自动重置 — 对齐 TS resetCwdIfOutsideProject
    /// 如果当前工作目录离开了项目根目录，自动重置回项目根目录
    /// </summary>
    private bool ResetCwdIfOutsideProject(string projectCwd)
    {
        try
        {
            var currentCwd = _fs.GetCurrentDirectory();
            if (string.Equals(currentCwd, projectCwd, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _fs.SetCurrentDirectory(projectCwd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
