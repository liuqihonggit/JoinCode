using Services.Shell.Providers;

namespace Services.Shell;

/// <summary>
/// Shell 命令执行上下文实现 — 对齐 TS ShellCommand
/// 封装正在运行的进程，支持前台转后台操作、输出溢出到磁盘、环境变量清理
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
    private readonly bool _detached;

    private int _isDisposed;
    private ShellCommandStatus _status = ShellCommandStatus.Running;
    private string? _backgroundTaskId;
    private Timer? _timeoutTimer;
    private Timer? _assistantTimer;
    private Timer? _sizeWatchdogTimer;

    /// <summary>
    /// 后台化后输出溢出文件路径 — 对齐 TS TaskOutput.spillToDisk()
    /// 后台化时将 StringBuilder 内容写入磁盘并清空，后续输出追加到文件
    /// </summary>
    private string? _spillFilePath;

    /// <summary>
    /// 是否为前台任务 — 对齐 TS ShellCommand 前后台标记
    /// 前台任务：CWD 变化会更新到进程；后台任务：CWD 仅清理文件
    /// </summary>
    private bool _isForeground = true;

    private const int SizeWatchdogIntervalMs = 5_000;

    /// <summary>
    /// 后台化时输出溢出阈值 — 超过此大小触发 spillToDisk
    /// </summary>
    private const int SpillThresholdChars = 100_000;

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

                    // 对齐 TS TaskOutput.#writeBuffered: 前台命令输出超过内存阈值时自动溢出到磁盘
                    // 防止长时间运行的前台命令导致 OOM
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

        // 对齐 TS TaskOutput: 启动大小看护，前台和后台都需要防止输出无限增长
        StartSizeWatchdog();
    }

    /// <summary>
    /// 创建并启动 Shell 执行上下文 — 对齐 TS ShellCommand.exec
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
            StandardOutputEncoding = provider is ShellProviderBase spb ? spb.OutputEncoding : Encoding.UTF8,
            StandardErrorEncoding = provider is ShellProviderBase spb2 ? spb2.ErrorEncoding : Encoding.UTF8
        };

        // 对齐 TS Shell.ts: detached 进程组隔离
        // Bash: detached=true → 新进程组，tree-kill 可精确杀整棵进程树
        // PowerShell: detached=false → 共享父进程控制台
        // Windows: CreateNoWindow=true 已足够，WindowStyle.Hidden 作为额外保障
        // Unix: TODO 需要通过 process group ID 创建新进程组 (setsid)
        if (provider.Detached)
        {
            if (OperatingSystem.IsWindows())
            {
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            }
        }

        // 对齐 TS Shell.ts: 先清理环境变量（CI 场景移除敏感信息），再叠加 envOverrides
        // psi.EnvironmentVariables 默认继承当前进程环境，只需删除敏感项
        SubprocessEnvCleaner.ScrubProcessEnvironment(psi);

        foreach (var (key, value) in envOverrides)
            psi.EnvironmentVariables[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ShellCommandContext(
            process, command, workingDirectory, timeoutMs,
            shouldAutoBackground, logger, fs, execResult.CwdFilePath, provider.Detached);
    }

    /// <inheritdoc />
    public bool Background(string taskId)
    {
        if (_status != ShellCommandStatus.Running) return false;

        _backgroundTaskId = taskId;
        _status = ShellCommandStatus.Backgrounded;
        _isForeground = false;

        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        _assistantTimer?.Dispose();
        _assistantTimer = null;

        // 对齐 TS ShellCommand.background() → spillToDisk()
        // 后台化时将内存中的输出溢出到磁盘，释放内存
        SpillToDisk();

        // 对齐 TS Shell.ts: 后台化后不再需要 CWD 追踪，立即清理文件
        CleanupCwdTrackingFile();

        _logger?.LogInformation("Shell 命令已转后台: {TaskId}, 命令: {Command}", taskId, _command);

        Backgrounded?.Invoke(this, taskId);

        return true;
    }

    /// <summary>
    /// 将内存中的输出溢出到磁盘 — 对齐 TS TaskOutput.#spillToDisk()
    /// 调用场景：
    ///   1. 前台命令输出超过 SpillThresholdChars 时自动触发（防止OOM）
    ///   2. Background() 转后台时调用（释放内存）
    /// 后续 stdout 接收直接追加到文件，不再驻留内存
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

    /// <summary>
    /// 获取当前已收集的 stdout — 支持从磁盘溢出文件读取
    /// </summary>
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
            var ctx = (ShellCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
            if (ctx._status is not (ShellCommandStatus.Running or ShellCommandStatus.Backgrounded)) return;

            if (ctx._spillFilePath is not null && ctx._fs.FileExists(ctx._spillFilePath))
            {
                var fileSize = ctx._fs.GetFileLength(ctx._spillFilePath);
                if (fileSize > ShellExecutionResult.MaxPersistedSizeBytes)
                {
                    ctx._logger?.LogWarning("任务输出文件超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, fileSize);
                    ctx.Kill();
                }
            }
            else if (ctx._stdoutBuilder.Length > ShellExecutionResult.MaxPersistedSizeBytes)
            {
                ctx._logger?.LogWarning("任务输出超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, ctx._stdoutBuilder.Length);
                ctx.Kill();
            }
        }, this, TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs), TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs));
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_status is not (ShellCommandStatus.Running or ShellCommandStatus.Backgrounded)) return;

        try { KillProcessTree(_process); }
        catch (Exception ex) { _logger?.LogWarning(ex, "杀进程树失败"); }

        _status = ShellCommandStatus.Killed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对齐 TS ShellCommand.#abortHandler: reason==='interrupt' 时不杀进程
    /// 用户提交新消息触发 interrupt，进程继续运行并转为后台任务
    /// 与 Kill() 的区别：interrupt 保留进程让模型可看到部分输出
    /// </remarks>
    public bool Interrupt()
    {
        if (_status != ShellCommandStatus.Running) return false;

        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
        if (!Background(taskId)) return false;

        _logger?.LogInformation("Shell 命令被 interrupt 转后台: {TaskId}, 命令: {Command}", taskId, _command);
        return true;
    }

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
            catch (Exception) { process.Kill(); }
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

        if (_status is ShellCommandStatus.Backgrounded && _spillFilePath is null
            && _stdoutBuilder.Length > ShellExecutionResult.PreviewSizeBytes)
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

    private static void HandleTimeout(object state)
    {
        var ctx = (ShellCommandContext)state;
        if (ctx._status != ShellCommandStatus.Running) return;

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

        if (_status == ShellCommandStatus.Killed)
        {
            _resultTcs.TrySetResult(ShellExecutionResult.FailureResult(
                "Process killed",
                GetCurrentStdout(),
                _stderrBuilder.ToString()));
            return;
        }

        var stdout = GetCurrentStdout();
        var stderr = _stderrBuilder.ToString();

        string? persistedPath = null;
        long? persistedSize = null;
        if (stdout.Length > ShellExecutionResult.MaxInlineOutputChars)
        {
            (persistedPath, persistedSize) = await PersistLargeOutputAsync(stdout).ConfigureAwait(false);
            stdout = stdout[..Math.Min(stdout.Length, ShellExecutionResult.PreviewSizeBytes)];
        }

        // 对齐 TS Shell.ts: 仅前台任务更新 CWD，后台任务仅清理文件
        var cwdWasReset = _isForeground ? TryUpdateCwdFromTrackingFile() : CleanupCwdTrackingFile();

        var result = ShellExecutionResult.SuccessResult(stdout, stderr, _process.ExitCode) with
        {
            PersistedOutputPath = persistedPath,
            PersistedOutputSize = persistedSize,
            BackgroundTaskId = _backgroundTaskId,
            CwdWasReset = cwdWasReset,
        };

        _resultTcs.TrySetResult(result);
    }

    /// <summary>
    /// 从 CWD 追踪文件读取命令执行后的工作目录变化 — 对齐 TS Shell.ts readCwdFile
    /// 仅前台任务调用：更新 CWD + 删除文件
    /// </summary>
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
                    // 对齐 TS Shell.ts: CWD 不存在时回退到项目根目录
                    try { _fs.SetCurrentDirectory(_workingDirectory); }
                    catch (Exception innerEx) { _logger?.LogDebug(innerEx, "回退到原始目录也失败: {Cwd}", _workingDirectory); }
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "读取 CWD 追踪文件失败: {Path}", _cwdFilePath); return false; }
    }

    /// <summary>
    /// 仅清理 CWD 追踪文件 — 后台任务调用，不更新 CWD
    /// </summary>
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
            if (fileSize > ShellExecutionResult.MaxPersistedSizeBytes)
            {
                var truncated = output[..(int)ShellExecutionResult.MaxPersistedSizeBytes];
                await _fs.WriteAllTextAsync(filePath, truncated).ConfigureAwait(false);
                fileSize = ShellExecutionResult.MaxPersistedSizeBytes;
            }

            return (filePath, fileSize);
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "大输出持久化失败"); return (null, null); }
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
        catch (Exception ex) { _logger?.LogDebug(ex, "Dispose 时终止进程失败"); }

        _process.Dispose();

        // 清理溢出文件
        if (_spillFilePath is not null)
        {
            try { if (_fs.FileExists(_spillFilePath)) _fs.DeleteFile(_spillFilePath); }
            catch (Exception ex) { _logger?.LogDebug(ex, "清理溢出文件失败: {Path}", _spillFilePath); }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
