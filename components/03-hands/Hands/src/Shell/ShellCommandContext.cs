using Services.Shell.Providers;

namespace Services.Shell;

/// <summary>
/// Shell 命令执行上下文实现 — 对齐 TS ShellCommand
/// 封装正在运行的进程，支持前台转后台操作
/// </summary>
public sealed class ShellCommandContext : IShellCommandContext
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

    private int _isDisposed;
    private ShellCommandStatus _status = ShellCommandStatus.Running;
    private string? _backgroundTaskId;
    private Timer? _timeoutTimer;
    private Timer? _assistantTimer;

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
        IFileSystem fs)
    {
        _process = process;
        _command = command;
        _workingDirectory = workingDirectory;
        _timeoutMs = timeoutMs;
        _logger = logger;
        _fs = fs;
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
                static state => HandleTimeout(state!),
                this,
                timeoutMs.Value,
                Timeout.Infinite);
        }

        // 监听进程退出
        _ = MonitorProcessExitAsync().WaitAsync(TimeSpan.FromSeconds(10), _processCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建并启动 Shell 执行上下文 — 对齐 TS ShellCommand.exec
    /// 使用 IShellProvider 构建 ProcessStartInfo，替代硬编码的 cmd.exe/powershell.exe
    /// </summary>
    public static ShellCommandContext Start(
        string command,
        string workingDirectory,
        IFileSystem fs,
        IShellProvider provider,
        int? timeoutMs = null,
        bool shouldAutoBackground = true,
        ILogger? logger = null)
    {
        var effectiveFs = fs;

        var commandString = command;
        var spawnArgs = provider.GetSpawnArgs(commandString);

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

        var process = new Process { StartInfo = psi };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ShellCommandContext(
            process, command, workingDirectory, timeoutMs,
            shouldAutoBackground, logger, effectiveFs);
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

        return true;
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_status is not (ShellCommandStatus.Running or ShellCommandStatus.Backgrounded)) return;

        try
        {
            _process.Kill();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "杀进程失败");
        }

        _status = ShellCommandStatus.Killed;
    }

    /// <inheritdoc />
    public void StartAssistantAutoBackgroundTimer()
    {
        if (!ShouldAutoBackground || _status != ShellCommandStatus.Running) return;

        _assistantTimer = new Timer(
            static state =>
            {
                var ctx = (ShellCommandContext)state!;
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
        };

        _resultTcs.TrySetResult(result);
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
        catch
        {
            return (null, null);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _timeoutTimer?.Dispose();
        _assistantTimer?.Dispose();
        _processCts.Cancel();
        _processCts.Dispose();

        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (Exception ex)
        {
            // 忽略进程终止异常
            System.Diagnostics.Trace.WriteLine($"终止进程时发生异常: {ex.Message}");
        }

        _process.Dispose();
    }
}
