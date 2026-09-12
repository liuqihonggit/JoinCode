namespace Services.SystemActuator;

public sealed class SystemActuatorCommandContext : ISystemActuatorCommandContext, ISystemActuatorLifecycle, IAsyncDisposable
{
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

    public string TaskId { get; } = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
    public SystemActuatorCommandStatus Status => _status;
    public Task<SystemActuatorExecutionResult> ResultTask => _resultTcs.Task;
    public string Command => _command;
    public string? OutputFilePath => _outputCollector.SpillFilePath;
    public bool ShouldAutoBackground { get; }

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
        _detached = detached;
        _processCts = new CancellationTokenSource();
        _stopwatch.Start();
        ShouldAutoBackground = shouldAutoBackground;

        _outputCollector = new ProcessOutputCollector(fs, logger, TaskId);
        _cwdTracker = new CwdTracker(fs, logger, cwdFilePath, workingDirectory);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _outputCollector.OnOutputDataReceived(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _outputCollector.OnErrorDataReceived(e.Data);
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
        var sessionId = global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var options = new SystemActuatorExecOptions
        {
            SessionId = sessionId,
            UseSandbox = useSandbox,
            SandboxTmpDir = sandboxTmpDir,
        };

        var execResult = await actuator.BuildExecCommandAsync(command, options).ConfigureAwait(false);
        var envOverrides = await actuator.GetEnvironmentOverridesAsync(command).ConfigureAwait(false);

        var spawnArgs = actuator.GetSpawnArgs(execResult.CommandString);

        var psi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
        {
            FileName = actuator.ShellPath,
            WorkingDirectory = workingDirectory,
            ArgumentList = spawnArgs,
            StandardOutputEncoding = actuator.OutputEncoding,
            StandardErrorEncoding = actuator.ErrorEncoding,
            SkipArgumentValidation = true,
        });

        psi.RedirectStandardInput = true;

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

        _outputCollector.SpillToDisk();
        _cwdTracker.CleanupCwdTrackingFile();

        _logger?.LogInformation("命令已转后台: {TaskId}, 命令: {Command}", taskId, _command);

        Backgrounded?.Invoke(this, taskId);

        return true;
    }

    public string GetCurrentStdout() => _outputCollector.GetCurrentStdout();
    public string GetCurrentStderr() => _outputCollector.GetCurrentStderr();

    private void StartSizeWatchdog()
    {
        _sizeWatchdogTimer = new Timer(static state =>
        {
            var ctx = (SystemActuatorCommandContext)(state ?? throw new InvalidOperationException("Timer state is null."));
            if (ctx._status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

            var outputLength = ctx._outputCollector.GetCurrentStdoutLength();
            if (outputLength > SystemActuatorExecutionResult.MaxPersistedSizeBytes)
            {
                ctx._logger?.LogWarning("任务输出超过硬上限，强制杀死: {TaskId}, Size={Size}", ctx._backgroundTaskId ?? ctx.TaskId, outputLength);
                ctx.Kill();
            }
        }, this, TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs), TimeSpan.FromMilliseconds(SizeWatchdogIntervalMs));
    }

    public void Kill()
    {
        if (_status is not (SystemActuatorCommandStatus.Running or SystemActuatorCommandStatus.Backgrounded)) return;

        try { ProcessKillHelper.KillProcessTree(_process, _logger); }
        catch (Exception ex) { _logger?.LogWarning(ex, "杀进程树失败"); }

        _status = SystemActuatorCommandStatus.Killed;
    }

    public bool Interrupt()
    {
        if (_status != SystemActuatorCommandStatus.Running) return false;

        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
        if (!Background(taskId)) return false;

        _logger?.LogInformation("命令被 interrupt 转后台: {TaskId}, 命令: {Command}", taskId, _command);
        return true;
    }

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

    public SystemActuatorLifecycleState LifecycleState => _status switch
    {
        SystemActuatorCommandStatus.Running => SystemActuatorLifecycleState.Active,
        SystemActuatorCommandStatus.Backgrounded => SystemActuatorLifecycleState.Backgrounded,
        SystemActuatorCommandStatus.Killed => SystemActuatorLifecycleState.Terminated,
        SystemActuatorCommandStatus.Completed => SystemActuatorLifecycleState.Completed,
        _ => SystemActuatorLifecycleState.Active,
    };

    public Task CompactAsync(CancellationToken cancellationToken = default)
    {
        if (_status == SystemActuatorCommandStatus.Running)
        {
            var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
            Background(taskId);
        }

        if (_status is SystemActuatorCommandStatus.Backgrounded && _outputCollector.SpillFilePath is null)
        {
            var currentLen = _outputCollector.GetCurrentStdoutLength();
            if (currentLen > SystemActuatorExecutionResult.PreviewSizeBytes)
            {
                _outputCollector.SpillToDisk();
                if (_outputCollector.SpillFilePath is null)
                {
                    _outputCollector.TruncateStdout(SystemActuatorExecutionResult.PreviewSizeBytes);
                }
            }
        }

        return Task.CompletedTask;
    }

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
                _outputCollector.GetCurrentStdout(),
                _outputCollector.GetCurrentStderr()) with { ExecutionTime = _stopwatch.Elapsed });
            return;
        }

        var stdout = _outputCollector.GetCurrentStdout();
        var stderr = _outputCollector.GetCurrentStderr();

        string? persistedPath = null;
        long? persistedSize = null;
        if (stdout.Length > SystemActuatorExecutionResult.MaxInlineOutputChars)
        {
            (persistedPath, persistedSize) = await OutputPersister.PersistLargeOutputAsync(stdout, _fs, _logger).ConfigureAwait(false);
            stdout = stdout[..Math.Min(stdout.Length, SystemActuatorExecutionResult.PreviewSizeBytes)];
        }

        var cwdWasReset = _isForeground ? _cwdTracker.TryUpdateCwdFromTrackingFile() : _cwdTracker.CleanupCwdTrackingFile();

        var result = SystemActuatorExecutionResult.SuccessResult(stdout, stderr, _process.ExitCode) with
        {
            ProcessId = _process.Id,
            PersistedOutputPath = persistedPath,
            PersistedOutputSize = persistedSize,
            BackgroundTaskId = _backgroundTaskId,
            CwdWasReset = cwdWasReset,
            ExecutionTime = _stopwatch.Elapsed,
        };

        _resultTcs.TrySetResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _timeoutTimer?.Dispose();
        _assistantTimer?.Dispose();
        _sizeWatchdogTimer?.Dispose();
        _processCts.Cancel();
        _processCts.Dispose();

        try
        {
            if (!_process.HasExited) ProcessKillHelper.KillProcessTree(_process, _logger);
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync 时终止进程失败"); }

        _process.Dispose();

        await _outputCollector.DisposeAsync().ConfigureAwait(false);
        await _cwdTracker.DisposeAsync().ConfigureAwait(false);
    }
}
