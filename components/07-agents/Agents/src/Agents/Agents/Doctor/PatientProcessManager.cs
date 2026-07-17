namespace Core.Agents.Doctor;

/// <summary>
/// 病人进程管理器 — spawn/kill jcc.exe 子进程，监控其生命周期
/// 复用 IProcessService + IInteractiveProcess 模式（与 BridgeSubprocessHandle 一致）
/// </summary>
public sealed class PatientProcessManager : IAsyncDisposable
{
    private readonly IProcessService _processService;
    private readonly ILogger? _logger;
    private IInteractiveProcess? _process;
    private Task? _stdoutReadTask;
    private CancellationTokenSource _readCts;
    private int _isDisposed;
    private readonly Queue<string> _stderrQueue;
    private const int MaxStderrLines = 50;

    /// <summary>病人进程信息</summary>
    public PatientInfo? Info { get; private set; }

    /// <summary>病人 stdout 行接收事件</summary>
    public event EventHandler<string>? OutputLineReceived;

    /// <summary>病人 stderr 行接收事件</summary>
    public event EventHandler<string>? ErrorLineReceived;

    /// <summary>病人进程退出事件</summary>
    public event EventHandler<PatientInfo>? ProcessExited;

    /// <summary>病人进程是否在运行</summary>
    public bool IsRunning
    {
        get
        {
            try { return _process is not null && !_process.HasExited; }
            catch { return false; }
        }
    }

    /// <summary>病人 stdout 读取器（供 DoctorIpcClient 使用）</summary>
    public System.IO.StreamReader? StandardOutput => _process?.StandardOutput;

    /// <summary>病人 stdin 写入器（供医生发送指令）</summary>
    public System.IO.StreamWriter? StandardInput => _process?.StandardInput;

    public PatientProcessManager(IProcessService processService, ILogger? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _readCts = new CancellationTokenSource();
        _stderrQueue = new Queue<string>(MaxStderrLines);
    }

    /// <summary>
    /// 启动病人进程 — spawn jcc.exe 子进程
    /// </summary>
    /// <param name="arguments">命令行参数（如 "--trust --verbose --await 30 -p 读取README.md"）</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="environmentVariables">额外环境变量</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<PatientInfo> SpawnAsync(
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        if (_process is not null && !_process.HasExited)
            throw new InvalidOperationException("病人进程已在运行，请先 Kill 后再 Spawn");

        var execPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? "jcc";

        _logger?.LogInformation("[Doctor] 启动病人进程: {ExecPath} {Args}", execPath, arguments);

        var options = new InteractiveProcessOptions
        {
            FileName = execPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environmentVariables,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true
        };

        _process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);

        _process.ErrorDataReceived += OnErrorDataReceived;

        Info = new PatientInfo
        {
            ProcessId = _process.Id,
            State = PatientState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Arguments = arguments
        };

        _readCts = new CancellationTokenSource();
        _stdoutReadTask = ReadStdoutAsync(_readCts.Token);
        _ = MonitorExitAsync(_readCts.Token);

        _logger?.LogInformation("[Doctor] 病人进程已启动: PID={ProcessId}", _process.Id);

        return Info;
    }

    /// <summary>
    /// 终止病人进程
    /// </summary>
    public void Kill()
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            _process.Kill();
            _logger?.LogInformation("[Doctor] 病人进程已终止: PID={ProcessId}", _process.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Doctor] 终止病人进程失败");
        }
    }

    /// <summary>
    /// 等待病人进程退出
    /// </summary>
    public async Task<PatientInfo> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_process is null)
            throw new InvalidOperationException("病人进程未启动");

        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return Info ?? new PatientInfo { State = PatientState.Failed };
    }

    private async Task ReadStdoutAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is not null)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                OutputLineReceived?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Doctor] 病人 stdout 读取结束");
        }
    }

    private void OnErrorDataReceived(object? sender, string line)
    {
        while (_stderrQueue.Count >= MaxStderrLines)
            _stderrQueue.Dequeue();
        _stderrQueue.Enqueue(line);

        ErrorLineReceived?.Invoke(this, line);
    }

    private async Task MonitorExitAsync(CancellationToken ct)
    {
        try
        {
            if (_process is null) return;

            await _process.WaitForExitAsync(ct).ConfigureAwait(false);

            var exitCode = _process.ExitCode;
            var state = exitCode switch
            {
                0 => PatientState.Completed,
                1234 => PatientState.Hung,
                _ => PatientState.Failed
            };

            Info = Info! with
            {
                State = state,
                ExitCode = exitCode,
                ExitedAt = DateTimeOffset.UtcNow
            };

            _logger?.LogInformation("[Doctor] 病人进程退出: PID={ProcessId}, 退出码={ExitCode}, 状态={State}",
                Info.ProcessId, exitCode, state);

            ProcessExited?.Invoke(this, Info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Doctor] 监控病人进程退出异常");

            if (Info is not null)
            {
                Info = Info with { State = PatientState.Failed, ExitedAt = DateTimeOffset.UtcNow };
                ProcessExited?.Invoke(this, Info);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        await _readCts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (_process is not null && !_process.HasExited)
            {
                Kill();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Doctor] Dispose 时等待进程退出失败");
        }

        var tasks = new List<Task>();
        if (_stdoutReadTask is not null) tasks.Add(_stdoutReadTask);
        if (tasks.Count > 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception ex) { _logger?.LogDebug(ex, "[Doctor] Dispose 时等待读取任务完成失败"); }
        }

        _readCts.Dispose();
        if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
    }
}
