
namespace Services.Shell;

/// <summary>
/// Shell 执行服务实现
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

    public ShellExecutionService(ShellExecutionConfig config, IFileSystem fs, IProcessService processService, ILogger<ShellExecutionService>? logger = null,
        ISandboxModeService? sandboxModeService = null, IPreventSleepService? preventSleepService = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
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
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, "cmd",
            (cmd, cwd, t) => new ProcessOptions
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                WorkingDirectory = cwd,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                TimeoutMs = t
            },
            "Command",
            cancellationToken);

    /// <inheritdoc />
    public Task<ShellExecutionResult> ExecutePowerShellAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(command, timeout, workingDirectory, disableSandbox, "PowerShell",
            (cmd, cwd, t) => new ProcessOptions
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd.Replace("\"", "\"\"")}\"",
                WorkingDirectory = cwd,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                TimeoutMs = t
            },
            "PowerShell command",
            cancellationToken);

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

        if (!_fs.DirectoryExists(cwd))
        {
            throw new DirectoryNotFoundException($"Working directory does not exist: {cwd}");
        }

        _logger?.LogInformation("Starting backgroundable command: {Command}", command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);

        var context = ShellCommandContext.Start(
            command,
            cwd,
            _fs,
            timeout,
            isPowerShell,
            shouldAutoBackground,
            _logger);

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
        string shellLabel,
        Func<string, string, int?, ProcessOptions> buildOptions,
        string logLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ShellExecutionResult.FailureResult("Command cannot be empty");
        }

        var cwd = ResolveWorkingDirectory(workingDirectory, disableSandbox);

        if (!_fs.DirectoryExists(cwd))
        {
            return ShellExecutionResult.FailureResult($"Working directory does not exist: {cwd}");
        }

        _logger?.LogInformation("Executing {Shell} command: {Command}", shellLabel, command);

        if (_preventSleepService is not null) await _preventSleepService.PreventSleepAsync(SleepPreventionType.Continuous).ConfigureAwait(false);
        try
        {
            var options = buildOptions(command, cwd, timeout);

            var result = await _processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == -1 && timeout.HasValue)
            {
                return ShellExecutionResult.TimeoutResult(timeout.Value);
            }

            var stdout = TruncateOutput(result.StandardOutput);
            var stderr = TruncateOutput(result.StandardError);

            string? persistedPath = null;
            long? persistedSize = null;
            if (stdout.Length > ShellExecutionResult.MaxInlineOutputChars)
            {
                (persistedPath, persistedSize) = await PersistLargeOutputAsync(
                    stdout, command, cancellationToken).ConfigureAwait(false);
                stdout = stdout[..Math.Min(stdout.Length, ShellExecutionResult.PreviewSizeBytes)];
            }

            var cwdWasReset = ResetCwdIfOutsideProject(cwd);

            _logger?.LogInformation(
                "{LogLabel} completed: ExitCode={ExitCode}, StdoutLength={StdoutLength}, StderrLength={StderrLength}",
                logLabel,
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
            _logger?.LogError(ex, "{LogLabel} execution failed: {Command}", logLabel, command);
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

    private string TruncateOutput(string output)
    {
        if (output.Length <= _config.MaxOutputBytes)
        {
            return output;
        }

        var end = _config.MaxOutputBytes;
        while (end > 0 && !IsCharBoundary(output, end))
        {
            end--;
        }

        return output[..end] + $"\n\n[Output truncated — exceeded {_config.MaxOutputBytes} bytes]";
    }

    private static bool IsCharBoundary(string s, int index)
    {
        if (index <= 0 || index >= s.Length)
        {
            return true;
        }

        var c = s[index];
        return !char.IsHighSurrogate(s[index - 1]) || !char.IsLowSurrogate(c);
    }

    #endregion
}
