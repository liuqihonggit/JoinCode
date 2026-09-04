namespace Core.Agents.Doctor;

/// <summary>
/// 病人进程管理器 — 管理多个 jcc.exe 子进程，监控其生命周期
/// 复用 IProcessService + IInteractiveProcess 模式（与 BridgeSubprocessHandle 一致）
/// </summary>
public sealed class PatientProcessManager : IAsyncDisposable
{
    private readonly IProcessService _processService;
    private readonly Dictionary<string, PatientHandle> _patients = new();
    private readonly AsyncLock _patientsLock = new();
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
            using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
            return _patients.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Info);
        }
    }

    public PatientProcessManager(IProcessService processService)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    /// <summary>
    /// 启动病人进程 — spawn jcc.exe 子进程
    /// </summary>
    /// <param name="patientId">病人标识</param>
    /// <param name="arguments">命令行参数字符串（回退模式，<paramref name="argumentList"/> 优先）</param>
    /// <param name="argumentList">参数化启动列表 — 优先于 <paramref name="arguments"/>，消除字符串拼接注入风险</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="environmentVariables">环境变量</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<PatientInfo> SpawnAsync(
        string patientId,
        string arguments,
        IReadOnlyList<string>? argumentList = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        await EnsurePatientNotExistsAsync(patientId, cancellationToken).ConfigureAwait(false);

        var execPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "jcc";

        DoctorDiag.Write($"[Doctor] 启动病人进程: {patientId}, {execPath} {arguments}");

        var options = new InteractiveProcessOptions
        {
            FileName = execPath,
            Arguments = arguments,
            ArgumentList = argumentList ?? [],
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>(),
            RedirectStandardError = true
        };

        var process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);

        ResilientSubprocess? resilientSubprocess = null;
        var resilienceEnabled = Environment.GetEnvironmentVariable("JCC_RESILIENCE_ENABLED") is not "0";
        if (resilienceEnabled)
        {
            var policy = SubprocessResiliencePolicy.DoctorDefault;
            Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc = async spawnCt =>
                await _processService.StartInteractiveAsync(options, spawnCt).ConfigureAwait(false);
            resilientSubprocess = new ResilientSubprocess(process, spawnFunc, policy);
        }

        var info = new PatientInfo
        {
            PatientId = patientId,
            ProcessId = process.Id,
            State = PatientState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Arguments = arguments
        };

        var handle = new PatientHandle(patientId, info, process, resilientSubprocess);

        handle.OutputLineReceived += OnOutputLineReceived;
        handle.ErrorLineReceived += OnErrorLineReceived;
        handle.ProcessExited += OnProcessExited;

        await RegisterPatientAsync(patientId, handle, cancellationToken).ConfigureAwait(false);

        DoctorDiag.Write($"[Doctor] 病人进程已启动: {patientId}, PID={process.Id}");

        return info;
    }

    /// <summary>检查病人是否已存在，存在则抛异常</summary>
    private async Task EnsurePatientNotExistsAsync(string patientId, CancellationToken cancellationToken)
    {
        using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
        if (_patients.ContainsKey(patientId))
            throw new InvalidOperationException($"[AGT013] 病人 {patientId} 已存在，请先 Kill 后再 Spawn");
    }

    /// <summary>注册病人进程到管理表</summary>
    private async Task RegisterPatientAsync(string patientId, PatientHandle handle, CancellationToken cancellationToken)
    {
        using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients[patientId] = handle; 
    }

    /// <summary>
    /// 终止指定病人进程
    /// </summary>
    public async Task KillAsync(string patientId)
    {
        PatientHandle? handle;
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients.TryGetValue(patientId, out handle); 

        if (handle is null) return;

        handle.Kill();
    }

    /// <summary>
    /// 从管理器中移除已退出的病人记录，允许重新 Spawn 同 ID 的病人
    /// 注意：不 Dispose handle，由 BootstrapAgent.DisposeAsync 统一处理
    /// </summary>
    public async Task RemovePatientAsync(string patientId)
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients.Remove(patientId); 
    }

    /// <summary>
    /// 终止所有病人进程
    /// </summary>
    public async Task KillAllAsync()
    {
        List<PatientHandle> handles;
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 handles = _patients.Values.ToList(); 

        foreach (var handle in handles)
            handle.Kill();
    }

    /// <summary>
    /// 等待指定病人进程退出
    /// </summary>
    public async Task<PatientInfo> WaitForExitAsync(string patientId, CancellationToken cancellationToken = default)
    {
        PatientHandle? handle;
        using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients.TryGetValue(patientId, out handle); 

        if (handle is null)
            throw new InvalidOperationException($"[AGT014] 病人 {patientId} 不存在");

        return await handle.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待所有病人进程退出
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PatientInfo>> WaitForAllExitAsync(CancellationToken cancellationToken = default)
    {
        List<PatientHandle> handles;
        using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 handles = _patients.Values.ToList(); 

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
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
        return _patients.TryGetValue(patientId, out var h) ? h.StandardInput : null;
    }

    /// <summary>
    /// 获取指定病人的信息
    /// </summary>
    public PatientInfo? GetPatientInfo(string patientId)
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
        return _patients.TryGetValue(patientId, out var h) ? h.Info : null;
    }

    /// <summary>
    /// 指定病人是否在运行
    /// </summary>
    public bool IsRunning(string patientId)
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
        return _patients.TryGetValue(patientId, out var h) && h.IsRunning;
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

        await CleanupPatientsAsync().ConfigureAwait(false);
        _patientsLock.Dispose();
    }

    /// <summary>清理所有病人句柄（在锁保护下执行）</summary>
    private async Task CleanupPatientsAsync()
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");

        var handles = _patients.Values.ToList();
        _patients.Clear();
    

        foreach (var handle in handles)
            await handle.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 单个病人进程句柄 — 封装 IInteractiveProcess + 生命周期管理
    /// </summary>
    private sealed class PatientHandle : IAsyncDisposable
    {
        private readonly string _patientId;
        private readonly IInteractiveProcess _process;
        private readonly ResilientSubprocess? _resilientSubprocess;
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

        public PatientHandle(string patientId, PatientInfo info, IInteractiveProcess process, ResilientSubprocess? resilientSubprocess = null)
        {
            _patientId = patientId;
            Info = info;
            _process = process;
            _resilientSubprocess = resilientSubprocess;
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
                DoctorDiag.Write($"[Doctor] 病人进程已终止: {_patientId}, PID={_process.Id}");
            }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[Doctor] 终止病人进程失败: {_patientId}: {ex.Message}");
            }
        }

        public async Task<PatientInfo> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var exitCode = _process.ExitCode;
            var state = exitCode switch
            {
                0 => PatientState.Completed,
                (int)ExitCode.AwaitTimeout => PatientState.Hung,
                _ => PatientState.Failed
            };

            if (Info.State == PatientState.Running)
            {
                Info = Info with
                {
                    State = state,
                    ExitCode = exitCode,
                    ExitedAt = DateTimeOffset.UtcNow
                };
            }

            return Info;
        }

        private async Task ReadStdoutAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = _resilientSubprocess is not null
                        ? await _resilientSubprocess.ReadStdoutLineAsync(ct).ConfigureAwait(false)
                        : await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break;

                    OutputLineReceived?.Invoke(this, (_patientId, line));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DoctorDiag.Write($"[Doctor] 病人 {_patientId} stdout 读取结束: {ex.Message}");
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
                    (int)ExitCode.AwaitTimeout => PatientState.Hung,
                    _ => PatientState.Failed
                };

                Info = Info with
                {
                    State = state,
                    ExitCode = exitCode,
                    ExitedAt = DateTimeOffset.UtcNow
                };

                DoctorDiag.Write($"[Doctor] 病人进程退出: {_patientId}, PID={Info.ProcessId}, 退出码={exitCode}, 状态={state}");

                ProcessExited?.Invoke(this, Info);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[Doctor] 监控病人进程退出异常: {_patientId}: {ex.Message}");

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
                DoctorDiag.Write($"[Doctor] Dispose 时等待进程退出失败: {_patientId}: {ex.Message}");
            }

            var tasks = new List<Task>();
            if (_stdoutReadTask is not null) tasks.Add(_stdoutReadTask);
            if (_monitorExitTask is not null) tasks.Add(_monitorExitTask);
            if (tasks.Count > 0)
            {
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception ex) { DoctorDiag.Write($"[Doctor] Dispose 时等待任务完成失败: {_patientId}: {ex.Message}"); }
            }

            _readCts.Dispose();
            await _process.DisposeAsync().ConfigureAwait(false);
        }
    }
}
