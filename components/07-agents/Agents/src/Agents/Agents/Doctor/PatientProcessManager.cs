namespace Core.Agents.Doctor;

/// <summary>
/// 病人进程管理器 — 管理多个 jcc.exe 子进程，监控其生命周期
/// 复用 IProcessService + IInteractiveProcess 模式（与 BridgeSubprocessHandle 一致）
/// </summary>
public sealed class PatientProcessManager : IAsyncDisposable
{
    private readonly IProcessService _processService;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, PatientHandle> _patients = new();
    private readonly SemaphoreSlim _patientsLock = new(1, 1);
    private int _isDisposed;

    /// <summary>病人 stdout 行接收事件（携带 PatientId）</summary>
    public event EventHandler<(string PatientId, string Line)>? OutputLineReceived;

    /// <summary>病人 stderr 行接收事件（携带 PatientId）</summary>
    public event EventHandler<(string PatientId, string Line)>? ErrorLineReceived;

    /// <summary>病人进程退出事件</summary>
    public event EventHandler<PatientInfo>? ProcessExited;

    /// <summary>所有病人信息</summary>
    public IReadOnlyDictionary<string, PatientInfo> Patients
    {
        get
        {
            _patientsLock.Wait();
            try
            {
                return _patients.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Info);
            }
            finally { _patientsLock.Release(); }
        }
    }

    public PatientProcessManager(IProcessService processService, ILogger? logger = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    /// <summary>
    /// 启动病人进程 — spawn jcc.exe 子进程
    /// </summary>
    public async Task<PatientInfo> SpawnAsync(
        string patientId,
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_patients.ContainsKey(patientId))
                throw new InvalidOperationException($"病人 {patientId} 已存在，请先 Kill 后再 Spawn");
        }
        finally { _patientsLock.Release(); }

        var execPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "jcc";

        _logger?.LogInformation("[Doctor] 启动病人进程: {PatientId}, {ExecPath} {Args}", patientId, execPath, arguments);

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

        var process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);

        var info = new PatientInfo
        {
            PatientId = patientId,
            ProcessId = process.Id,
            State = PatientState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Arguments = arguments
        };

        var handle = new PatientHandle(patientId, info, process, _logger);

        handle.OutputLineReceived += OnOutputLineReceived;
        handle.ErrorLineReceived += OnErrorLineReceived;
        handle.ProcessExited += OnProcessExited;

        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _patients[patientId] = handle; }
        finally { _patientsLock.Release(); }

        _logger?.LogInformation("[Doctor] 病人进程已启动: {PatientId}, PID={ProcessId}", patientId, process.Id);

        return info;
    }

    /// <summary>
    /// 终止指定病人进程
    /// </summary>
    public async Task KillAsync(string patientId)
    {
        PatientHandle? handle;
        await _patientsLock.WaitAsync().ConfigureAwait(false);
        try { _patients.TryGetValue(patientId, out handle); }
        finally { _patientsLock.Release(); }

        if (handle is null) return;

        handle.Kill();
    }

    /// <summary>
    /// 终止所有病人进程
    /// </summary>
    public async Task KillAllAsync()
    {
        List<PatientHandle> handles;
        await _patientsLock.WaitAsync().ConfigureAwait(false);
        try { handles = _patients.Values.ToList(); }
        finally { _patientsLock.Release(); }

        foreach (var handle in handles)
            handle.Kill();
    }

    /// <summary>
    /// 等待指定病人进程退出
    /// </summary>
    public async Task<PatientInfo> WaitForExitAsync(string patientId, CancellationToken cancellationToken = default)
    {
        PatientHandle? handle;
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _patients.TryGetValue(patientId, out handle); }
        finally { _patientsLock.Release(); }

        if (handle is null)
            throw new InvalidOperationException($"病人 {patientId} 不存在");

        return await handle.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待所有病人进程退出
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PatientInfo>> WaitForAllExitAsync(CancellationToken cancellationToken = default)
    {
        List<PatientHandle> handles;
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { handles = _patients.Values.ToList(); }
        finally { _patientsLock.Release(); }

        var results = new Dictionary<string, PatientInfo>();
        foreach (var handle in handles)
        {
            try { results[handle.PatientId] = await handle.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        return results;
    }

    /// <summary>
    /// 获取指定病人的 stdin 写入器
    /// </summary>
    public System.IO.StreamWriter? GetStandardInput(string patientId)
    {
        _patientsLock.Wait();
        try { return _patients.TryGetValue(patientId, out var h) ? h.StandardInput : null; }
        finally { _patientsLock.Release(); }
    }

    /// <summary>
    /// 获取指定病人的信息
    /// </summary>
    public PatientInfo? GetPatientInfo(string patientId)
    {
        _patientsLock.Wait();
        try { return _patients.TryGetValue(patientId, out var h) ? h.Info : null; }
        finally { _patientsLock.Release(); }
    }

    /// <summary>
    /// 指定病人是否在运行
    /// </summary>
    public bool IsRunning(string patientId)
    {
        _patientsLock.Wait();
        try { return _patients.TryGetValue(patientId, out var h) && h.IsRunning; }
        finally { _patientsLock.Release(); }
    }

    private void OnOutputLineReceived(object? sender, (string PatientId, string Line) e)
    {
        OutputLineReceived?.Invoke(this, e);
    }

    private void OnErrorLineReceived(object? sender, (string PatientId, string Line) e)
    {
        ErrorLineReceived?.Invoke(this, e);
    }

    private void OnProcessExited(object? sender, PatientInfo info)
    {
        ProcessExited?.Invoke(this, info);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        await KillAllAsync().ConfigureAwait(false);

        List<PatientHandle> handles;
        await _patientsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            handles = _patients.Values.ToList();
            _patients.Clear();
        }
        finally { _patientsLock.Release(); }

        foreach (var handle in handles)
            await handle.DisposeAsync().ConfigureAwait(false);

        _patientsLock.Dispose();
    }

    /// <summary>
    /// 单个病人进程句柄 — 封装 IInteractiveProcess + 生命周期管理
    /// </summary>
    private sealed class PatientHandle : IAsyncDisposable
    {
        private readonly string _patientId;
        private readonly IInteractiveProcess _process;
        private readonly ILogger? _logger;
        private readonly Queue<string> _stderrQueue;
        private readonly CancellationTokenSource _readCts;
        private Task? _stdoutReadTask;
        private Task? _monitorExitTask;
        private bool _isDisposed;

        private const int MaxStderrLines = 50;

        public string PatientId => _patientId;
        public PatientInfo Info { get; private set; }
        public System.IO.StreamWriter? StandardInput => _process.StandardInput;

        public bool IsRunning
        {
            get
            {
                try { return _process is not null && !_process.HasExited; }
                catch { return false; }
            }
        }

        public event EventHandler<(string PatientId, string Line)>? OutputLineReceived;
        public event EventHandler<(string PatientId, string Line)>? ErrorLineReceived;
        public event EventHandler<PatientInfo>? ProcessExited;

        public PatientHandle(string patientId, PatientInfo info, IInteractiveProcess process, ILogger? logger)
        {
            _patientId = patientId;
            Info = info;
            _process = process;
            _logger = logger;
            _stderrQueue = new Queue<string>(MaxStderrLines);
            _readCts = new CancellationTokenSource();

            _process.ErrorDataReceived += OnErrorDataReceived;
            _stdoutReadTask = ReadStdoutAsync(_readCts.Token);
            _monitorExitTask = MonitorExitAsync(_readCts.Token);
        }

        public void Kill()
        {
            if (_process is null || _process.HasExited) return;

            try
            {
                _process.Kill();
                _logger?.LogInformation("[Doctor] 病人进程已终止: {PatientId}, PID={ProcessId}", _patientId, _process.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Doctor] 终止病人进程失败: {PatientId}", _patientId);
            }
        }

        public async Task<PatientInfo> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return Info;
        }

        private async Task ReadStdoutAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break;

                    OutputLineReceived?.Invoke(this, (_patientId, line));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Doctor] 病人 {PatientId} stdout 读取结束", _patientId);
            }
        }

        private void OnErrorDataReceived(object? sender, string line)
        {
            while (_stderrQueue.Count >= MaxStderrLines)
                _stderrQueue.Dequeue();
            _stderrQueue.Enqueue(line);

            ErrorLineReceived?.Invoke(this, (_patientId, line));
        }

        private async Task MonitorExitAsync(CancellationToken ct)
        {
            try
            {
                await _process.WaitForExitAsync(ct).ConfigureAwait(false);

                var exitCode = _process.ExitCode;
                var state = exitCode switch
                {
                    0 => PatientState.Completed,
                    1234 => PatientState.Hung,
                    _ => PatientState.Failed
                };

                Info = Info with
                {
                    State = state,
                    ExitCode = exitCode,
                    ExitedAt = DateTimeOffset.UtcNow
                };

                _logger?.LogInformation("[Doctor] 病人进程退出: {PatientId}, PID={ProcessId}, 退出码={ExitCode}, 状态={State}",
                    _patientId, Info.ProcessId, exitCode, state);

                ProcessExited?.Invoke(this, Info);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Doctor] 监控病人进程退出异常: {PatientId}", _patientId);

                Info = Info with { State = PatientState.Failed, ExitedAt = DateTimeOffset.UtcNow };
                ProcessExited?.Invoke(this, Info);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            await _readCts.CancelAsync().ConfigureAwait(false);

            try
            {
                if (!_process.HasExited)
                {
                    Kill();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Doctor] Dispose 时等待进程退出失败: {PatientId}", _patientId);
            }

            var tasks = new List<Task>();
            if (_stdoutReadTask is not null) tasks.Add(_stdoutReadTask);
            if (_monitorExitTask is not null) tasks.Add(_monitorExitTask);
            if (tasks.Count > 0)
            {
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception ex) { _logger?.LogDebug(ex, "[Doctor] Dispose 时等待任务完成失败: {PatientId}", _patientId); }
            }

            _readCts.Dispose();
            await _process.DisposeAsync().ConfigureAwait(false);
        }
    }
}
