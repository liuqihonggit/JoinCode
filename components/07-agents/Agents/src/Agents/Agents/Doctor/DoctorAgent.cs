namespace Core.Agents.Doctor;

/// <summary>
/// 医生 Agent — 监控 jcc.exe 子进程运行状态，自动诊断和修复问题
/// 不继承 BuiltInAgentBase（不需要 IChatClient/LLM），独立实现进程监控+诊断+修复循环
/// 支持 1:N 多病人：通过 SSE 传输层管理多个病人进程
/// </summary>
public sealed class DoctorAgent : IAsyncDisposable
{
    private readonly PatientProcessManager _patientManager;
    private readonly IDoctorTransport _transport;
    private readonly DiagnosticEngine _diagnosticEngine;
    private readonly HotFixEngine _hotFixEngine;
    private readonly SourceCodePatcher _patcher;
    private readonly BuildOrchestrator _builder;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly List<HotFixResult> _fixResults = [];
    private readonly SemaphoreSlim _fixLock = new(1, 1);
    private int _isDisposed;

    /// <summary>Agent 名称</summary>
    public string Name => "DoctorAgent";

    /// <summary>Agent 描述</summary>
    public string Description => "监控 jcc.exe 子进程运行状态，自动诊断和修复问题";

    /// <summary>是否启用自动修复</summary>
    public bool AutoFixEnabled { get; set; } = true;

    /// <summary>累计诊断报告</summary>
    public IReadOnlyList<DiagnosticReport> Diagnostics => _diagnosticEngine.Reports;

    /// <summary>累计修复结果</summary>
    public IReadOnlyList<HotFixResult> FixResults => _fixResults;

    /// <summary>病人进程管理器</summary>
    public PatientProcessManager PatientManager => _patientManager;

    /// <summary>IPC 传输层</summary>
    public IDoctorTransport Transport => _transport;

    public DoctorAgent(
        IFileSystem fs,
        IProcessService processService,
        IDoctorTransport? transport = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));

        _patientManager = new PatientProcessManager(processService);
        _transport = transport ?? new DoctorTcpServer(9902);
        _diagnosticEngine = new DiagnosticEngine();
        _patcher = new SourceCodePatcher(fs);
        _builder = new BuildOrchestrator(processService);
        _hotFixEngine = new HotFixEngine(_patcher, _builder, _patientManager, _transport, fs);

        _patientManager.ProcessExited += OnProcessExited;
        _patientManager.OutputLineReceived += OnOutputLineReceived;
        _patientManager.ErrorLineReceived += OnErrorLineReceived;
        _transport.EventReceived += OnDiagnosticEventReceived;
        _transport.PatientConnected += OnPatientConnected;
        _transport.PatientDisconnected += OnPatientDisconnected;
        _diagnosticEngine.DiagnosticReportGenerated += OnDiagnosticReportGenerated;
        _hotFixEngine.FixApplied += OnFixApplied;
        _hotFixEngine.FixRolledBack += OnFixRolledBack;
    }

    /// <summary>
    /// 运行医生模式（单病人 stdio 模式）— 启动病人进程，监控其运行，直到退出
    /// </summary>
    public async Task<DoctorReport> RunAsync(
        string patientArguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var patientId = $"patient-{Guid.NewGuid().ToString("N")[..6]}";
        return await RunAsync(patientId, patientArguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 运行医生模式（指定病人 ID）— 启动病人进程，监控其运行，直到退出
    /// </summary>
    public async Task<DoctorReport> RunAsync(
        string patientId,
        string patientArguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DoctorDiag.Write($"[Doctor] 医生模式启动，病人: {patientId}, 参数: {patientArguments}");

        try
        {
            DoctorDiag.Write("[Doctor] 正在启动 SSE 服务器...");
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            DoctorDiag.Write("[Doctor] SSE 服务器已启动");

            DoctorDiag.Write($"[Doctor] 正在启动病人进程: {patientId}");
            var patientInfo = await _patientManager.SpawnAsync(
                patientId, patientArguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);

            DoctorDiag.Write($"[Doctor] 病人进程已启动: {patientId} (PID={patientInfo.ProcessId})，开始监控");

            DoctorDiag.Write($"[Doctor] 等待病人进程退出: {patientId}");
            var exitInfo = await _patientManager.WaitForExitAsync(patientId, cancellationToken).ConfigureAwait(false);

            DoctorDiag.Write($"[Doctor] 病人进程已退出: {patientId}, 状态={exitInfo.State}, 退出码={exitInfo.ExitCode}");

            var reportStatus = exitInfo.State switch
            {
                PatientState.Completed => DoctorReportStatus.Completed,
                PatientState.Hung => DoctorReportStatus.Failed,
                PatientState.Failed => DoctorReportStatus.PartiallyFixed,
                _ => DoctorReportStatus.Failed
            };

            return BuildReport(startedAt, exitInfo, reportStatus);
        }
        catch (OperationCanceledException)
        {
            DoctorDiag.Write("[Doctor] 医生模式被取消");
            await _patientManager.KillAllAsync().ConfigureAwait(false);
            return BuildReport(startedAt, _patientManager.GetPatientInfo(patientId), DoctorReportStatus.Failed);
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 医生模式运行异常: {ex.GetType().Name}: {ex.Message}");
            await _patientManager.KillAllAsync().ConfigureAwait(false);
            return BuildReport(startedAt, _patientManager.GetPatientInfo(patientId), DoctorReportStatus.Failed);
        }
    }

    /// <summary>
    /// 运行 SSE 服务器模式 — 监听病人连接，持续监控直到所有病人退出
    /// </summary>
    public async Task<DoctorReport> RunServerAsync(
        int port = 9902,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DoctorDiag.Write($"[Doctor] 医生 SSE 服务器模式启动，端口: {port}");

        try
        {
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            DoctorDiag.Write("[Doctor] SSE 服务器已启动，等待病人连接...");

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DoctorDiag.Write("[Doctor] 医生 SSE 服务器被停止");
        }

        return BuildReport(startedAt, null);
    }

    /// <summary>
    /// 运行测试套件 — 依次执行多个测试用例
    /// </summary>
    public async Task<DoctorReport> RunTestSuiteAsync(
        IEnumerable<(string TestCaseId, string TestName, string Arguments)> testCases,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var testResults = new List<DoctorTestCaseResult>();

        DoctorDiag.Write($"[Doctor] 测试套件启动，共 {testCases.Count()} 个用例");

        foreach (var (testCaseId, testName, arguments) in testCases)
        {
            if (cancellationToken.IsCancellationRequested) break;

            DoctorDiag.Write($"[Doctor] 执行测试: {testCaseId} - {testName}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var patientId = $"test-{testCaseId}";
                var report = await RunAsync(patientId, arguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var status = report.Patient?.State switch
                {
                    PatientState.Completed => DoctorTestStatus.Pass,
                    PatientState.Hung => DoctorTestStatus.Hung,
                    PatientState.Failed => DoctorTestStatus.Fail,
                    _ => DoctorTestStatus.Error
                };

                testResults.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCaseId,
                    TestName = testName,
                    Status = status,
                    Duration = sw.Elapsed
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                testResults.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCaseId,
                    TestName = testName,
                    Status = DoctorTestStatus.Error,
                    Duration = sw.Elapsed,
                    ErrorMessage = ex.Message
                });
            }
        }

        return new DoctorReport
        {
            Patients = _patientManager.Patients,
            Diagnostics = _diagnosticEngine.Reports,
            FixResults = _fixResults,
            TestResults = testResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = testResults.All(r => r.Status == DoctorTestStatus.Pass)
                ? DoctorReportStatus.Completed
                : DoctorReportStatus.PartiallyFixed
        };
    }

    private void OnProcessExited(object? sender, PatientInfo info)
    {
        DoctorDiag.Write($"[Doctor] 病人进程退出事件: {info.PatientId}, PID={info.ProcessId}, 状态={info.State}");
        _diagnosticEngine.EvaluateProcessHung(info);
    }

    private void OnOutputLineReceived(object? sender, (string PatientId, string Line) e)
    {
        System.Diagnostics.Trace.WriteLine($"[Doctor] 病人 {e.PatientId} stdout: {e.Line}");
    }

    private void OnErrorLineReceived(object? sender, (string PatientId, string Line) e)
    {
        System.Diagnostics.Trace.WriteLine($"[Doctor] 病人 {e.PatientId} stderr: {e.Line}");
    }

    private void OnDiagnosticEventReceived(object? sender, DiagnosticEvent evt)
    {
        DoctorDiag.Write($"[Doctor] 收到诊断事件: {evt.EventType} (病人: {evt.PatientId})");
        _diagnosticEngine.Evaluate(evt);
    }

    private void OnPatientConnected(object? sender, string patientId)
    {
        DoctorDiag.Write($"[Doctor] 病人已连接: {patientId}");
    }

    private void OnPatientDisconnected(object? sender, string patientId)
    {
        DoctorDiag.Write($"[Doctor] 病人已断开: {patientId}");
    }

    private void OnDiagnosticReportGenerated(object? sender, DiagnosticReport report)
    {
        DoctorDiag.WriteError($"[Doctor] 诊断报告: {report.RuleId} - {report.Description} (病人: {report.PatientId})");

        if (!AutoFixEnabled) return;

        _ = Task.Run(async () =>
        {
            if (!await _fixLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)) return;

            try
            {
                var result = await _hotFixEngine.ExecuteFixAsync(report).ConfigureAwait(false);
                _fixResults.Add(result);
            }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[Doctor] 自动修复异常: {report.RuleId} (病人: {report.PatientId}): {ex.Message}");
            }
            finally
            {
                _fixLock.Release();
            }
        });
    }

    private void OnFixApplied(object? sender, HotFixResult result)
    {
        DoctorDiag.Write($"[Doctor] 修复已应用: {result.Action.ActionType} (病人: {result.PatientId})");
    }

    private void OnFixRolledBack(object? sender, HotFixResult result)
    {
        DoctorDiag.WriteError($"[Doctor] 修复已回滚: {result.Action.ActionType} (病人: {result.PatientId})");
    }

    private DoctorReport BuildReport(
        DateTimeOffset startedAt,
        PatientInfo? patientInfo,
        DoctorReportStatus status = DoctorReportStatus.Running)
    {
        var patients = patientInfo is not null
            ? new Dictionary<string, PatientInfo> { [patientInfo.PatientId] = patientInfo }
            : _patientManager.Patients;

        return new DoctorReport
        {
            Patients = patients,
            Diagnostics = _diagnosticEngine.Reports,
            FixResults = [.. _fixResults, .. _hotFixEngine.Results],
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = status
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _patientManager.ProcessExited -= OnProcessExited;
        _patientManager.OutputLineReceived -= OnOutputLineReceived;
        _patientManager.ErrorLineReceived -= OnErrorLineReceived;
        _transport.EventReceived -= OnDiagnosticEventReceived;
        _transport.PatientConnected -= OnPatientConnected;
        _transport.PatientDisconnected -= OnPatientDisconnected;
        _diagnosticEngine.DiagnosticReportGenerated -= OnDiagnosticReportGenerated;
        _hotFixEngine.FixApplied -= OnFixApplied;
        _hotFixEngine.FixRolledBack -= OnFixRolledBack;

        await _transport.DisposeAsync().ConfigureAwait(false);
        await _patientManager.DisposeAsync().ConfigureAwait(false);
        _fixLock.Dispose();
    }
}
