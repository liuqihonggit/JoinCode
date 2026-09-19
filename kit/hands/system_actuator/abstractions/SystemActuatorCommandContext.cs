namespace Services.SystemActuator;

/// <summary>
/// 系统执行器命令上下文 — 封装单次命令执行的全生命周期：进程启动、输出收集、超时/后台化/中断/杀死、CWD 追踪与异步释放
/// </summary>
public sealed class SystemActuatorCommandContext : ISystemActuatorCommandContext, ISystemActuatorLifecycle, IAsyncDisposable {
    private readonly Process _process;
    private readonly ProcessOutputCollector _outputCollector;
    private readonly CwdTracker _cwdTracker;
    private readonly CancellationTokenSource _processCts;
    private readonly TaskCompletionSource<SystemActuatorExecutionResult> _resultTcs = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly int? _timeoutMs;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly bool _detached;

    private int _isDisposed;
    private SystemActuatorCommandStatus _status = SystemActuatorCommandStatus.Running;
    private string? _backgroundTaskId;
    private Timer? _timeoutTimer;
    private Timer? _assistantTimer;
    private Timer? _sizeWatchdogTimer;
    private bool _isForeground = true;

    private const int SizeWatchdogIntervalMs = 5_000;

    /// <summary>任务唯一标识（自动生成）</summary>
    public string TaskId { get; } = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
    /// <summary>命令当前状态</summary>
    public SystemActuatorCommandStatus Status => _status;
    /// <summary>命令执行结果任务 — 进程退出或被杀死时完成</summary>
    public Task<SystemActuatorExecutionResult> ResultTask => _resultTcs.Task;
    /// <summary>原始命令字符串</summary>
    public string Command => _command;
    /// <summary>输出溢出文件路径 — 输出过大时溢出到磁盘的文件路径，未溢出时为 null</summary>
    public string? OutputFilePath => _outputCollector.SpillFilePath;
    /// <summary>是否允许自动后台化 — 超时或 Assistant 阻塞预算耗尽时自动转后台</summary>
    public bool ShouldAutoBackground { get; }

    /// <summary>命令被转后台时触发 — 参数为上下文与后台任务 ID</summary>
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
        bool detached) {
        _process = process;
        _command = command;
        _workingDirectory = workingDirectory;
        _timeoutMs = timeoutMs;
        _logger = logger;
        _fs = fs;
        _detached = detached;
        _processCts = new CancellationTokenSource();
        _stopwatch.Start();
        ShouldAutoBackground = shouldAutoBackground;

        _outputCollector = new ProcessOutputCollector(fs, logger, TaskId);
        _cwdTracker = new CwdTracker(fs, logger, cwdFilePath, workingDirectory);

        process.OutputDataReceived += (_, e) => {
            if (e.Data != null) _outputCollector.OnOutputDataReceived(e.Data);
        };
        process.ErrorDataReceived += (_, e) => {
            if (e.Data != null) _outputCollector.OnErrorDataReceived(e.Data);
        };

        if (timeoutMs.HasValue && timeoutMs.Value > 0) {
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
    /// 异步启动一个命令上下文 — 构建命令、注入环境变量、启动进程并开始输出收集
    /// </summary>
    /// <param name="command">要执行的命令字符串</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="actuator">系统执行器实例</param>
    /// <param name="timeoutMs">超时毫秒数（null 或 ≤0 表示不超时）</param>
    /// <param name="shouldAutoBackground">是否允许超时自动后台化</param>
    /// <param name="useSandbox">是否使用沙箱</param>
    /// <param name="sandboxTmpDir">沙箱临时目录路径</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>已启动的命令上下文</returns>
    public static async Task<SystemActuatorCommandContext> StartAsync(
        string command,
        string workingDirectory,
        IFileSystem fs,
        ISystemActuator actuator,
        int? timeoutMs = null,
        bool shouldAutoBackground = true,
        bool useSandbox = false,
        string? sandboxTmpDir = null,
        ILogger? logger = null) {
        var sessionId = global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var options = new SystemActuatorExecOptions {
            SessionId = sessionId,
            UseSandbox = useSandbox,
            SandboxTmpDir = sandboxTmpDir,
        };

        var execResult = await actuator.BuildExecCommandAsync(command, options).ConfigureAwait(false);
        var envOverrides = await actuator.GetEnvironmentOverridesAsync(command).ConfigureAwait(false);

        var spawnArgs = actuator.GetSpawnArgs(execResult.CommandString);

        var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions {
            FileName = actuator.ShellPath,
            WorkingDirectory = workingDirectory,
            ArgumentList = spawnArgs,
            StandardOutputEncoding = actuator.OutputEncoding,
            StandardErrorEncoding = actuator.ErrorEncoding,
            SkipArgumentValidation = true,
        });

        psi.RedirectStandardInput = true;

        if (actuator.Detached) {
            if (OperatingSystem.IsWindows()) {
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

    /// <summary>
    /// 将当前前台命令转后台 — 释放超时/Assistant 定时器，溢出输出到磁盘，清理 CWD 追踪文件
    /// </summary>
    /// <param name="taskId">后台任务 ID</param>
    /// <returns>成功转后台返回 true；命令非 Running 状态返回 false</returns>
    public bool Background(string taskId) {
        if (_status != SystemActuatorCommandStatus.Running) return false;

        _backgroundTaskId = taskId;
        _status = SystemActuatorCommandStatus.Backgrounded;
        _isForeground = false;

        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        _assistantTimer?.Dispose();
        _assistantTimer = null;

        _outputCollector.SpillToDisk();
        _cwdTracker.CleanupCwdTrackingFile();

        _logger?.LogInformation("命令已转后台: {TaskId}, 命令: {Command}", taskId, _command);

        Backgrounded?.Invoke(this, taskId);

        return true;
    }

    /// <summary>获取当前累积的标准输出（含已溢出到磁盘的内容）</summary>
    /// <returns>当前标准输出字符串</returns>
    public string GetCurrentStdout() => _outputCollector.GetCurrentStdout();
    /// <summary>获取当前累积的标准错误输出</summary>
    /// <returns>当前标准错误字符串</returns>
    public string GetCurrentStderr() => _outputCollector.GetCurrentStderr();

    private void StartSizeWatchdog() {
        _sizeWatchdogTimer = new Timer(static state => {
            var ctx = (SystemActuatorCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
            if (ctx._status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

            var outputLength = ctx._outputCollector.GetCurrentStdoutLength();
            if (outputLength > SystemActuatorExecutionResult.MaxPersistedSizeBytes) {
                ctx._logger?.LogWarning("任务输出超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, outputLength);
                ctx.Kill();
            }
        }, this, TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs), TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs));
    }

    /// <summary>强制杀死进程树并将状态置为 Killed — 已非 Running/Backgrounded 状态时为空操作</summary>
    public void Kill() {
        if (_status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

        try { ProcessKillHelper.KillProcessTree(_process, _logger); } catch (Exception ex) { _logger?.LogWarning(ex, "杀进程树失败"); }

        _status = SystemActuatorCommandStatus.Killed;
    }

    /// <summary>
    /// 中断当前前台命令 — 通过将其转后台实现（生成新任务 ID）
    /// </summary>
    /// <returns>成功转后台返回 true；命令非 Running 状态返回 false</returns>
    public bool Interrupt() {
        if (_status != SystemActuatorCommandStatus.Running) return false;

        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
        if (!Background(taskId)) return false;

        _logger?.LogInformation("命令被 interrupt 转后台: {TaskId}, 命令: {Command}", taskId, _command);
        return true;
    }

    /// <summary>
    /// 启动 Assistant 自动后台化定时器 — 当 ShouldAutoBackground 为 true 且命令在 Assistant 阻塞预算耗尽后仍 Running 时自动转后台
    /// </summary>
    public void StartAssistantAutoBackgroundTimer() {
        if (!ShouldAutoBackground || _status != SystemActuatorCommandStatus.Running) return;

        _assistantTimer = new Timer(
            static state => {
                var ctx = (SystemActuatorCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
                if (ctx._status == SystemActuatorCommandStatus.Running && ctx._backgroundTaskId is null) {
                    var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
                    if (ctx.Background(taskId)) {
                        ctx._logger?.LogInformation("Assistant 自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
                    }
                }
            },
            this,
            SystemActuatorBackgroundConstants.AssistantBlockingBudgetMs,
            Timeout.Infinite);
    }

    /// <summary>生命周期状态 — 由命令状态映射而来</summary>
    public SystemActuatorLifecycleState LifecycleState => _status switch {
        SystemActuatorCommandStatus.Running => SystemActuatorLifecycleState.Active,
        SystemActuatorCommandStatus.Backgrounded => SystemActuatorLifecycleState.Backgrounded,
        SystemActuatorCommandStatus.Killed => SystemActuatorLifecycleState.Terminated,
        SystemActuatorCommandStatus.Completed => SystemActuatorLifecycleState.Completed,
        _ => SystemActuatorLifecycleState.Active,
    };

    /// <summary>
    /// 压缩命令上下文 — Running 状态先转后台；Backgrounded 状态下若输出过大且未溢出则溢出到磁盘并截断内存缓冲
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task CompactAsync(CancellationToken cancellationToken = default) {
        if (_status == SystemActuatorCommandStatus.Running) {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            Background(taskId);
        }

        if (_status is SystemActuatorCommandStatus.Backgrounded && _outputCollector.SpillFilePath is null) {
            var currentLen = _outputCollector.GetCurrentStdoutLength();
            if (currentLen > SystemActuatorExecutionResult.PreviewSizeBytes) {
                _outputCollector.SpillToDisk();
                if (_outputCollector.SpillFilePath is null) {
                    _outputCollector.TruncateStdout(SystemActuatorExecutionResult.PreviewSizeBytes);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 终止命令 — 等价于调用 Kill()
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task TerminateAsync(CancellationToken cancellationToken = default) {
        Kill();
        return Task.CompletedTask;
    }

    private static void HandleTimeout(object state) {
        var ctx = (SystemActuatorCommandContext)state;
        if (ctx._status != SystemActuatorCommandStatus.Running) return;

        if (ctx.ShouldAutoBackground) {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            ctx.Background(taskId);
            ctx._logger?.LogInformation("超时自动后台化: {TaskId}, 命令: {Command}", taskId, ctx._command);
        } else {
            ctx.Kill();
        }
    }

    private async Task MonitorProcessExitAsync() {
        try {
            await _process.WaitForExitAsync(_processCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) { }

        if (_status == SystemActuatorCommandStatus.Killed) {
            _resultTcs.TrySetResult(SystemActuatorExecutionResult.FailureResult(
                "Process killed",
                _outputCollector.GetCurrentStdout(),
                _outputCollector.GetCurrentStderr()) with { ExecutionTime = _stopwatch.Elapsed });
            return;
        }

        var stdout = _outputCollector.GetCurrentStdout();
        var stderr = _outputCollector.GetCurrentStderr();

        string? persistedPath = null;
        long? persistedSize = null;
        if (stdout.Length > SystemActuatorExecutionResult.MaxInlineOutputChars) {
            (persistedPath, persistedSize) = await OutputPersister.PersistLargeOutputAsync(stdout, _fs, _logger).ConfigureAwait(false);
            stdout = stdout[..Math.Min(stdout.Length, SystemActuatorExecutionResult.PreviewSizeBytes)];
        }

        var cwdWasReset = _isForeground ? _cwdTracker.TryUpdateCwdFromTrackingFile() : _cwdTracker.CleanupCwdTrackingFile();

        var result = SystemActuatorExecutionResult.SuccessResult(stdout, stderr, _process.ExitCode) with {
            ProcessId = _process.Id,
            PersistedOutputPath = persistedPath,
            PersistedOutputSize = persistedSize,
            BackgroundTaskId = _backgroundTaskId,
            CwdWasReset = cwdWasReset,
            ExecutionTime = _stopwatch.Elapsed,
        };

        _resultTcs.TrySetResult(result);
    }

    /// <summary>
    /// 异步释放资源 — 释放定时器、取消令牌、杀死未退出进程、释放输出收集器与 CWD 追踪器
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        _timeoutTimer?.Dispose();
        _assistantTimer?.Dispose();
        _sizeWatchdogTimer?.Dispose();
        _processCts.Cancel();
        _processCts.Dispose();

        try {
            if (!_process.HasExited) ProcessKillHelper.KillProcessTree(_process, _logger);
        } catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync 时终止进程失败"); }

        _process.Dispose();

        await _outputCollector.DisposeAsync().ConfigureAwait(false);
        await _cwdTracker.DisposeAsync().ConfigureAwait(false);
    }
}