using Services.Shell.Providers;

namespace Services.Shell;

/// <summary>
/// Shell 命令执行上下文实现 — 对齐 TS ShellCommand
/// 封装正在运行的进程，支持前台转后台操作
/// </summary>
public sealed class ShellCommandContext : IShellCommandContext, IShellLifecycle
{
    private readonly Process _process;
    private readonly StringBuilder _stdoutBuilder = new();
    private readonly StringBuilder _stderrBuilder = new();
    private readonly CancellationTokenSource _processCts;
    private readonly TaskCompletionSource<ShellExecutionResult> _resultTcs = new();
    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly int? _timeoutMs;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly string? _cwdFilePath;

    private int _isDisposed;
    private ShellCommandStatus _status = ShellCommandStatus.Running;
    private string? _backgroundTaskId;
    private Timer? _timeoutTimer;
    private Timer? _assistantTimer;
    private Timer? _sizeWatchdogTimer;

    /// <summary>
    /// 后台任务大小看门狗间隔 — 对齐 TS SIZE_WATCHDOG_INTERVAL_MS (5s)
    /// </summary>
    private const int SizeWatchdogIntervalMs = 5_000;

    /// <inheritdoc />
    public string TaskId { get; } = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);

    /// <inheritdoc />
    public ShellCommandStatus Status => _status;

    /// <inheritdoc />
    public Task<ShellExecutionResult> ResultTask => _resultTcs.Task;

    /// <inheritdoc />
    public string Command => _command;

    /// <inheritdoc />
    public bool ShouldAutoBackground { get; }

    /// <summary>
    /// 后台化事件 — 当命令被后台化时触发
    /// </summary>
    public event Action<ShellCommandContext, string>? Backgrounded;

    private ShellCommandContext(
        Process process,
        string command,
        string workingDirectory,
        int? timeoutMs,
        bool shouldAutoBackground,
        ILogger? logger,
        IFileSystem fs,
        string? cwdFilePath = null)
    {
        _process = process;
        _command = command;
        _workingDirectory = workingDirectory;
        _timeoutMs = timeoutMs;
        _logger = logger;
        _fs = fs;
        _cwdFilePath = cwdFilePath;
        _processCts = new CancellationTokenSource();

        ShouldAutoBackground = shouldAutoBackground;

        // 注册输出接收
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _stderrBuilder.AppendLine(e.Data);
        };

        // 启动超时定时器 — 对齐 TS ShellCommand.#handleTimeout
        if (timeoutMs.HasValue && timeoutMs.Value > 0)
        {
            _timeoutTimer = new Timer(
                static state => HandleTimeout(state ?? throw new InvalidOperationException("Timer state is null.")),
                this,
                timeoutMs.Value,
                Timeout.Infinite);
        }

        // 监听进程退出
        _ = MonitorProcessExitAsync().WaitAsync(TimeSpan.FromSeconds(10), _processCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建并启动 Shell 执行上下文 — 对齐 TS ShellCommand.exec
    /// 使用 IShellProvider.BuildExecCommandAsync 构建完整命令（含 CWD 追踪、extglob 禁用等）
    /// 使用 IShellProvider.GetEnvironmentOverridesAsync 注入环境变量到子进程
    /// </summary>
    public static async Task<ShellCommandContext> StartAsync(
        string command,
        string workingDirectory,
        IFileSystem fs,
        IShellProvider provider,
        int? timeoutMs = null,
        bool shouldAutoBackground = true,
        bool useSandbox = false,
        string? sandboxTmpDir = null,
        ILogger? logger = null)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var options = new ShellExecOptions
        {
            SessionId = sessionId,
            UseSandbox = useSandbox,
            SandboxTmpDir = sandboxTmpDir,
        };

        var execResult = await provider.BuildExecCommandAsync(command, options).ConfigureAwait(false);
        var envOverrides = await provider.GetEnvironmentOverridesAsync(command).ConfigureAwait(false);

        var spawnArgs = provider.GetSpawnArgs(execResult.CommandString);

        var psi = new ProcessStartInfo
        {
            FileName = provider.ShellPath,
            Arguments = string.Join(' ', spawnArgs),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (envOverrides.Count > 0)
        {
            foreach (var (key, value) in envOverrides)
                psi.EnvironmentVariables[key] = value;
        }

        var process = new Process { StartInfo = psi };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ShellCommandContext(
            process, command, workingDirectory, timeoutMs,
            shouldAutoBackground, logger, fs, execResult.CwdFilePath);
    }

    /// <inheritdoc />
    public bool Background(string taskId)
    {
        if (_status != ShellCommandStatus.Running) return false;

        _backgroundTaskId = taskId;
        _status = ShellCommandStatus.Backgrounded;

        // 清理超时定时器 — 对齐 TS ShellCommand.background() cleanupListeners
        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        _assistantTimer?.Dispose();
        _assistantTimer = null;

        _logger?.LogInformation("Shell 命令已转后台: {TaskId}, 命令: {Command}", taskId, _command);

        Backgrounded?.Invoke(this, taskId);

        StartSizeWatchdog();

        return true;
    }

    /// <summary>
    /// 启动后台任务大小看门狗 — 对齐 TS ShellCommand.#startSizeWatchdog
    /// 每 5s 检查输出大小，超过硬上限则杀进程防止磁盘填满
    /// </summary>
    private void StartSizeWatchdog()
    {
        _sizeWatchdogTimer = new Timer(static state =>
        {
            var ctx = (ShellCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
            if (ctx._status != ShellCommandStatus.Backgrounded) return;

            var currentSize = ctx._stdoutBuilder.Length;
            if (currentSize > ShellExecutionResult.MaxPersistedSizeBytes)
            {
                ctx._logger?.LogWarning("后台任务输出超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId, currentSize);
                ctx.Kill();
            }
        }, this, TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs), TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs));
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_status is not (ShellCommandStatus.Running or ShellCommandStatus.Backgrounded)) return;

        try
        {
            KillProcessTree(_process);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "杀进程树失败");
        }

        _status = ShellCommandStatus.Killed;
    }

    /// <summary>
    /// 杀死进程树 — 对齐 TS treeKill(pid, 'SIGKILL')
    /// Windows: taskkill /T /F /PID; 非Windows: process.Kill() 杀整个进程组
    /// </summary>
    private static void KillProcessTree(Process process)
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
            catch (Exception)
            {
                process.Kill();
            }
        }
        else
        {
            process.Kill();
        }
    }

    /// <inheritdoc />
    public void StartAssistantAutoBackgroundTimer()
    {
        if (!ShouldAutoBackground || _status != ShellCommandStatus.Running) return;

        _assistantTimer = new Timer(
            static state =>
            {
                var ctx = (ShellCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
                if (ctx._status == ShellCommandStatus.Running && ctx._backgroundTaskId is null)
                {
                    var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
                    if (ctx.Background(taskId))
                    {
                        ctx._logger?.LogInformation("Assistant 自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
                    }
                }
            },
            this,
            ShellBackgroundConstants.AssistantBlockingBudgetMs,
            Timeout.Infinite);
    }

    /// <inheritdoc />
    public string GetCurrentStdout() => _stdoutBuilder.ToString();

    /// <inheritdoc />
    public string GetCurrentStderr() => _stderrBuilder.ToString();

    /// <inheritdoc />
    public ShellLifecycleState LifecycleState => _status switch
    {
        ShellCommandStatus.Running => ShellLifecycleState.Active,
        ShellCommandStatus.Backgrounded => ShellLifecycleState.Backgrounded,
        ShellCommandStatus.Killed => ShellLifecycleState.Terminated,
        ShellCommandStatus.Completed => ShellLifecycleState.Completed,
        _ => ShellLifecycleState.Active,
    };

    /// <inheritdoc />
    public Task CompactAsync(CancellationToken cancellationToken = default)
    {
        if (_status == ShellCommandStatus.Running)
        {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            Background(taskId);
        }

        if (_status is ShellCommandStatus.Backgrounded && _stdoutBuilder.Length > ShellExecutionResult.PreviewSizeBytes)
        {
            _stdoutBuilder.Remove(ShellExecutionResult.PreviewSizeBytes, _stdoutBuilder.Length - ShellExecutionResult.PreviewSizeBytes);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        Kill();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 超时处理 — 对齐 TS ShellCommand.#handleTimeout
    /// 如果允许自动后台化则转后台，否则杀进程
    /// </summary>
    private static void HandleTimeout(object state)
    {
        var ctx = (ShellCommandContext)state;

        if (ctx._status != ShellCommandStatus.Running) return;

        if (ctx.ShouldAutoBackground)
        {
            // 超时自动后台化 — 对齐 TS ShellCommand.#handleTimeout
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            ctx.Background(taskId);
            ctx._logger?.LogInformation("超时自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
        }
        else
        {
            // 不允许自动后台化，杀进程
            ctx.Kill();
        }
    }

    private async Task MonitorProcessExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_processCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 进程被取消
        }

        // 进程退出后完成结果
        if (_status == ShellCommandStatus.Killed)
        {
            _resultTcs.TrySetResult(ShellExecutionResult.FailureResult(
                "Process killed",
                _stdoutBuilder.ToString(),
                _stderrBuilder.ToString()));
            return;
        }

        var stdout = _stdoutBuilder.ToString();
        var stderr = _stderrBuilder.ToString();

        // 大输出磁盘持久化
        string? persistedPath = null;
        long? persistedSize = null;
        if (stdout.Length > ShellExecutionResult.MaxInlineOutputChars)
        {
            (persistedPath, persistedSize) = await PersistLargeOutputAsync(stdout).ConfigureAwait(false);
            stdout = stdout[..Math.Min(stdout.Length, ShellExecutionResult.PreviewSizeBytes)];
        }

        var result = ShellExecutionResult.SuccessResult(stdout, stderr, _process.ExitCode) with
        {
            PersistedOutputPath = persistedPath,
            PersistedOutputSize = persistedSize,
            BackgroundTaskId = _backgroundTaskId,
            CwdWasReset = TryUpdateCwdFromTrackingFile(),
        };

        _resultTcs.TrySetResult(result);
    }

    /// <summary>
    /// 从 CWD 追踪文件读取命令执行后的工作目录变化 — 对齐 TS Shell.ts readCwdFile
    /// </summary>
    private bool TryUpdateCwdFromTrackingFile()
    {
        if (string.IsNullOrEmpty(_cwdFilePath)) return false;

        try
        {
            if (!_fs.FileExists(_cwdFilePath)) return false;

            var newCwd = _fs.ReadAllText(_cwdFilePath).Trim();
            if (string.IsNullOrEmpty(newCwd)) return false;

            try
            {
                _fs.DeleteFile(_cwdFilePath);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "清理 CWD 追踪文件失败: {Path}", _cwdFilePath);
            }

            if (!string.Equals(newCwd, _workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _fs.SetCurrentDirectory(newCwd);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "设置工作目录失败: {Cwd}", newCwd);
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "读取 CWD 追踪文件失败: {Path}", _cwdFilePath);
            return false;
        }
    }

    /// <summary>
    /// 大输出持久化到磁盘
    /// </summary>
    private async Task<(string? Path, long? Size)> PersistLargeOutputAsync(string output)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jcc-tool-results");
            DirectoryHelper.EnsureDirectoryExists(_fs, tempDir);

            var filePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}"[..^20] + ".txt");
            await _fs.WriteAllTextAsync(filePath, output).ConfigureAwait(false);

            var fileSize = _fs.GetFileLength(filePath);
            if (fileSize > ShellExecutionResult.MaxPersistedSizeBytes)
            {
                var truncated = output[..(int)ShellExecutionResult.MaxPersistedSizeBytes];
                await _fs.WriteAllTextAsync(filePath, truncated).ConfigureAwait(false);
                fileSize = ShellExecutionResult.MaxPersistedSizeBytes;
            }

            return (filePath, fileSize);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "大输出持久化失败");
            return (null, null);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _timeoutTimer?.Dispose();
        _assistantTimer?.Dispose();
        _sizeWatchdogTimer?.Dispose();
        _processCts.Cancel();
        _processCts.Dispose();

        try
        {
            if (!_process.HasExited) KillProcessTree(_process);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Dispose 时终止进程失败");
        }

        _process.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
