namespace Services.SystemActuator;

/// <summary>
/// 系统执行器命令上下文实现 — 封装正在运行的进程，支持前台转后台、输出溢出到磁盘
/// </summary>
public sealed class SystemActuatorCommandContext : ISystemActuatorCommandContext, ISystemActuatorLifecycle, IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdoutBuilder = new();
    private readonly StringBuilder _stderrBuilder = new();
    private readonly CancellationTokenSource _processCts;
    private readonly TaskCompletionSource<SystemActuatorExecutionResult> _resultTcs = new();
    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly int? _timeoutMs;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly string? _cwdFilePath;
    private readonly bool _detached;

    private int _isDisposed;
    private SystemActuatorCommandStatus _status = SystemActuatorCommandStatus.Running;
    private string? _backgroundTaskId;
    private Timer? _timeoutTimer;
    private Timer? _assistantTimer;
    private Timer? _sizeWatchdogTimer;

    /// <summary>
    /// 后台化后输出溢出文件路径
    /// </summary>
    private string? _spillFilePath;

    /// <summary>
    /// 是否为前台任务
    /// </summary>
    private bool _isForeground = true;

    private const int SizeWatchdogIntervalMs = 5_000;
    private const int SpillThresholdChars = 100_000;

    /// <inheritdoc />
    public string TaskId { get; } = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);

    /// <inheritdoc />
    public SystemActuatorCommandStatus Status => _status;

    /// <inheritdoc />
    public Task<SystemActuatorExecutionResult> ResultTask => _resultTcs.Task;

    /// <inheritdoc />
    public string Command => _command;

    /// <inheritdoc />
    public string? OutputFilePath => _spillFilePath;

    /// <inheritdoc />
    public bool ShouldAutoBackground { get; }

    /// <summary>
    /// 后台化事件 — 当命令被后台化时触发
    /// </summary>
    public event Action<SystemActuatorCommandContext, string>? Backgrounded;

    private SystemActuatorCommandContext(
        Process process,
        string command,
        string workingDirectory,
        int? timeoutMs,
        bool shouldAutoBackground,
        ILogger? logger,
        IFileSystem fs,
        string? cwdFilePath,
        bool detached)
    {
        _process = process;
        _command = command;
        _workingDirectory = workingDirectory;
        _timeoutMs = timeoutMs;
        _logger = logger;
        _fs = fs;
        _cwdFilePath = cwdFilePath;
        _detached = detached;
        _processCts = new CancellationTokenSource();

        ShouldAutoBackground = shouldAutoBackground;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                if (_spillFilePath is not null)
                {
                    try { _fs.AppendAllText(_spillFilePath, e.Data + Environment.NewLine); }
                    catch (Exception ex) { _logger?.LogDebug(ex, "追加溢出输出失败"); }
                }
                else
                {
                    _stdoutBuilder.AppendLine(e.Data);

                    if (_stdoutBuilder.Length > SpillThresholdChars)
                    {
                        SpillToDisk();
                    }
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _stderrBuilder.AppendLine(e.Data);
        };

        if (timeoutMs.HasValue && timeoutMs.Value > 0)
        {
            _timeoutTimer = new Timer(
                static state => HandleTimeout(state ?? throw new InvalidOperationException("Timer state is null.")),
                this,
                timeoutMs.Value,
                Timeout.Infinite);
        }

        _ = MonitorProcessExitAsync().WaitAsync(TimeSpan.FromSeconds(10), _processCts.Token).ConfigureAwait(false);

        StartSizeWatchdog();
    }

    /// <summary>
    /// 创建并启动执行上下文
    /// </summary>
    public static async Task<SystemActuatorCommandContext> StartAsync(
        string command,
        string workingDirectory,
        IFileSystem fs,
        ISystemActuator actuator,
        int? timeoutMs = null,
        bool shouldAutoBackground = true,
        bool useSandbox = false,
        string? sandboxTmpDir = null,
        ILogger? logger = null)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var options = new SystemActuatorExecOptions
        {
            SessionId = sessionId,
            UseSandbox = useSandbox,
            SandboxTmpDir = sandboxTmpDir,
        };

        var execResult = await actuator.BuildExecCommandAsync(command, options).ConfigureAwait(false);
        var envOverrides = await actuator.GetEnvironmentOverridesAsync(command).ConfigureAwait(false);

        var spawnArgs = actuator.GetSpawnArgs(execResult.CommandString);

        var psi = new ProcessStartInfo
        {
            FileName = actuator.ShellPath,
            Arguments = string.Join(' ', spawnArgs),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = actuator.OutputEncoding,
            StandardErrorEncoding = actuator.ErrorEncoding
        };

        if (actuator.Detached)
        {
            if (OperatingSystem.IsWindows())
            {
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            }
        }

        SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

        foreach (var (key, value) in envOverrides)
            psi.EnvironmentVariables[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new SystemActuatorCommandContext(
            process, command, workingDirectory, timeoutMs,
            shouldAutoBackground, logger, fs, execResult.CwdFilePath, actuator.Detached);
    }

    /// <inheritdoc />
    public bool Background(string taskId)
    {
        if (_status != SystemActuatorCommandStatus.Running) return false;

        _backgroundTaskId = taskId;
        _status = SystemActuatorCommandStatus.Backgrounded;
        _isForeground = false;

        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        _assistantTimer?.Dispose();
        _assistantTimer = null;

        SpillToDisk();
        CleanupCwdTrackingFile();

        _logger?.LogInformation("命令已转后台: {TaskId}, 命令: {Command}", taskId, _command);

        Backgrounded?.Invoke(this, taskId);

        return true;
    }

    /// <summary>
    /// 将内存中的输出溢出到磁盘
    /// </summary>
    private void SpillToDisk()
    {
        if (_spillFilePath is not null) return;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jcc-tool-results");
            DirectoryHelper.EnsureDirectoryExists(_fs, tempDir);

            _spillFilePath = Path.Combine(tempDir, $"spill-{TaskId}.txt");

            if (_stdoutBuilder.Length > 0)
            {
                _fs.WriteAllText(_spillFilePath, _stdoutBuilder.ToString());
                _stdoutBuilder.Clear();
            }

            _logger?.LogDebug("任务输出已溢出到磁盘: {Path}", _spillFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "输出溢出到磁盘失败，保留内存缓冲区");
        }
    }

    /// <inheritdoc />
    public string GetCurrentStdout()
    {
        if (_spillFilePath is not null && _fs.FileExists(_spillFilePath))
        {
            try { return _fs.ReadAllText(_spillFilePath); }
            catch { return _stdoutBuilder.ToString(); }
        }
        return _stdoutBuilder.ToString();
    }

    /// <inheritdoc />
    public string GetCurrentStderr() => _stderrBuilder.ToString();

    private void StartSizeWatchdog()
    {
        _sizeWatchdogTimer = new Timer(static state =>
        {
            var ctx = (SystemActuatorCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
            if (ctx._status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

            if (ctx._spillFilePath is not null && ctx._fs.FileExists(ctx._spillFilePath))
            {
                var fileSize = ctx._fs.GetFileLength(ctx._spillFilePath);
                if (fileSize > SystemActuatorExecutionResult.MaxPersistedSizeBytes)
                {
                    ctx._logger?.LogWarning("任务输出文件超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, fileSize);
                    ctx.Kill();
                }
            }
            else if (ctx._stdoutBuilder.Length > SystemActuatorExecutionResult.MaxPersistedSizeBytes)
            {
                ctx._logger?.LogWarning("任务输出超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, ctx._stdoutBuilder.Length);
                ctx.Kill();
            }
        }, this, TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs), TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs));
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

        try { KillProcessTree(_process); }
        catch (Exception ex) { _logger?.LogWarning(ex, "杀进程树失败"); }

        _status = SystemActuatorCommandStatus.Killed;
    }

    /// <inheritdoc />
    public bool Interrupt()
    {
        if (_status != SystemActuatorCommandStatus.Running) return false;

        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
        if (!Background(taskId)) return false;

        _logger?.LogInformation("命令被 interrupt 转后台: {TaskId}, 命令: {Command}", taskId, _command);
        return true;
    }

    private void KillProcessTree(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var killer = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = $"/T /F /PID {process.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    }
                };
                killer.Start();
                killer.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "taskkill.exe 终止进程树失败，尝试直接 Kill PID {Pid}", process.Id);
                TryKillSafely(process);
            }
        }
        else
        {
            TryKillSafely(process);
        }
    }

    private void TryKillSafely(Process process)
    {
        try { process.Kill(); }
        catch (Exception killEx)
        {
            _logger?.LogDebug(killEx, "直接 Kill PID {Pid} 失败（可能已退出或无权限）", process.Id);
        }
    }

    /// <inheritdoc />
    public void StartAssistantAutoBackgroundTimer()
    {
        if (!ShouldAutoBackground || _status != SystemActuatorCommandStatus.Running) return;

        _assistantTimer = new Timer(
            static state =>
            {
                var ctx = (SystemActuatorCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
                if (ctx._status == SystemActuatorCommandStatus.Running && ctx._backgroundTaskId is null)
                {
                    var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
                    if (ctx.Background(taskId))
                    {
                        ctx._logger?.LogInformation("Assistant 自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
                    }
                }
            },
            this,
            SystemActuatorBackgroundConstants.AssistantBlockingBudgetMs,
            Timeout.Infinite);
    }

    /// <inheritdoc />
    public SystemActuatorLifecycleState LifecycleState => _status switch
    {
        SystemActuatorCommandStatus.Running => SystemActuatorLifecycleState.Active,
        SystemActuatorCommandStatus.Backgrounded => SystemActuatorLifecycleState.Backgrounded,
        SystemActuatorCommandStatus.Killed => SystemActuatorLifecycleState.Terminated,
        SystemActuatorCommandStatus.Completed => SystemActuatorLifecycleState.Completed,
        _ => SystemActuatorLifecycleState.Active,
    };

    /// <inheritdoc />
    public Task CompactAsync(CancellationToken cancellationToken = default)
    {
        if (_status == SystemActuatorCommandStatus.Running)
        {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            Background(taskId);
        }

        if (_status is SystemActuatorCommandStatus.Backgrounded && _spillFilePath is null
            && _stdoutBuilder.Length > SystemActuatorExecutionResult.PreviewSizeBytes)
        {
            SpillToDisk();
            if (_spillFilePath is null)
            {
                _stdoutBuilder.Remove(SystemActuatorExecutionResult.PreviewSizeBytes, _stdoutBuilder.Length - SystemActuatorExecutionResult.PreviewSizeBytes);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        Kill();
        return Task.CompletedTask;
    }

    private static void HandleTimeout(object state)
    {
        var ctx = (SystemActuatorCommandContext)state;
        if (ctx._status != SystemActuatorCommandStatus.Running) return;

        if (ctx.ShouldAutoBackground)
        {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            ctx.Background(taskId);
            ctx._logger?.LogInformation("超时自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
        }
        else
        {
            ctx.Kill();
        }
    }

    private async Task MonitorProcessExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_processCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        if (_status == SystemActuatorCommandStatus.Killed)
        {
            _resultTcs.TrySetResult(SystemActuatorExecutionResult.FailureResult(
                "Process killed",
                GetCurrentStdout(),
                _stderrBuilder.ToString()));
            return;
        }

        var stdout = GetCurrentStdout();
        var stderr = _stderrBuilder.ToString();

        string? persistedPath = null;
        long? persistedSize = null;
        if (stdout.Length > SystemActuatorExecutionResult.MaxInlineOutputChars)
        {
            (persistedPath, persistedSize) = await PersistLargeOutputAsync(stdout).ConfigureAwait(false);
            stdout = stdout[..Math.Min(stdout.Length, SystemActuatorExecutionResult.PreviewSizeBytes)];
        }

        var cwdWasReset = _isForeground ? TryUpdateCwdFromTrackingFile() : CleanupCwdTrackingFile();

        var result = SystemActuatorExecutionResult.SuccessResult(stdout, stderr, _process.ExitCode) with
        {
            ProcessId = _process.Id,
            PersistedOutputPath = persistedPath,
            PersistedOutputSize = persistedSize,
            BackgroundTaskId = _backgroundTaskId,
            CwdWasReset = cwdWasReset,
        };

        _resultTcs.TrySetResult(result);
    }

    private bool TryUpdateCwdFromTrackingFile()
    {
        if (string.IsNullOrEmpty(_cwdFilePath)) return false;

        try
        {
            if (!_fs.FileExists(_cwdFilePath)) return false;

            var newCwd = _fs.ReadAllText(_cwdFilePath).Trim();
            if (string.IsNullOrEmpty(newCwd)) return false;

            try { _fs.DeleteFile(_cwdFilePath); }
            catch (Exception ex) { _logger?.LogDebug(ex, "清理 CWD 追踪文件失败: {Path}", _cwdFilePath); }

            if (!string.Equals(newCwd, _workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _fs.SetCurrentDirectory(newCwd);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "设置工作目录失败: {Cwd}，回退到原始目录", newCwd);
                    try { _fs.SetCurrentDirectory(_workingDirectory); }
                    catch (Exception innerEx) { _logger?.LogDebug(innerEx, "回退到原始目录也失败: {Cwd}", _workingDirectory); }
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "读取 CWD 追踪文件失败: {Path}", _cwdFilePath); return false; }
    }

    private bool CleanupCwdTrackingFile()
    {
        if (string.IsNullOrEmpty(_cwdFilePath)) return false;

        try
        {
            if (_fs.FileExists(_cwdFilePath))
            {
                _fs.DeleteFile(_cwdFilePath);
            }
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "清理 CWD 追踪文件失败: {Path}", _cwdFilePath); }

        return false;
    }

    private async Task<(string? Path, long? Size)> PersistLargeOutputAsync(string output)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jcc-tool-results");
            DirectoryHelper.EnsureDirectoryExists(_fs, tempDir);

            var filePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}"[..^20] + ".txt");
            await _fs.WriteAllTextAsync(filePath, output).ConfigureAwait(false);

            var fileSize = _fs.GetFileLength(filePath);
            if (fileSize > SystemActuatorExecutionResult.MaxPersistedSizeBytes)
            {
                var truncated = output[..(int)SystemActuatorExecutionResult.MaxPersistedSizeBytes];
                await _fs.WriteAllTextAsync(filePath, truncated).ConfigureAwait(false);
                fileSize = SystemActuatorExecutionResult.MaxPersistedSizeBytes;
            }

            return (filePath, fileSize);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "大输出持久化失败，尝试重试一次");
            try
            {
                var retryDir = Path.Combine(Path.GetTempPath(), "jcc-tool-results");
                DirectoryHelper.EnsureDirectoryExists(_fs, retryDir);
                var retryPath = Path.Combine(retryDir, $"{Guid.NewGuid():N}"[..^20] + ".txt");
                await _fs.WriteAllTextAsync(retryPath, output).ConfigureAwait(false);
                var retrySize = _fs.GetFileLength(retryPath);
                return (retryPath, retrySize);
            }
            catch (Exception retryEx)
            {
                _logger?.LogError(retryEx, "大输出持久化重试也失败，数据将丢失");
                return (null, null);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return ValueTask.CompletedTask;

        _timeoutTimer?.Dispose();
        _assistantTimer?.Dispose();
        _sizeWatchdogTimer?.Dispose();
        _processCts.Cancel();
        _processCts.Dispose();

        try
        {
            if (!_process.HasExited) KillProcessTree(_process);
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync 时终止进程失败"); }

        _process.Dispose();

        if (_spillFilePath is not null)
        {
            try { if (_fs.FileExists(_spillFilePath)) _fs.DeleteFile(_spillFilePath); }
            catch (Exception ex) { _logger?.LogDebug(ex, "清理溢出文件失败: {Path}", _spillFilePath); }
        }

        return ValueTask.CompletedTask;
    }
}
