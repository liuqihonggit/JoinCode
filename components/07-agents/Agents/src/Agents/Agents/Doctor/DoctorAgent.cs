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
    private readonly ILogger? _logger;
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
        IDoctorTransport? transport = null,
        ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;

        _patientManager = new PatientProcessManager(processService, logger);
        _transport = transport ?? new DoctorSseServer(9902, logger: logger);
        _diagnosticEngine = new DiagnosticEngine(logger);
        _patcher = new SourceCodePatcher(fs, logger);
        _builder = new BuildOrchestrator(processService, logger);
        _hotFixEngine = new HotFixEngine(_patcher, _builder, _patientManager, _transport, fs, logger);

        _patientManager.ProcessExited += OnProcessExited;
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
        _logger?.LogInformation("[Doctor] 医生模式启动，病人: {PatientId}, 参数: {Args}", patientId, patientArguments);

        try
        {
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            var patientInfo = await _patientManager.SpawnAsync(
                patientId, patientArguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[Doctor] 病人进程已启动: {PatientId} (PID={ProcessId})，开始监控", patientId, patientInfo.ProcessId);

            var exitInfo = await _patientManager.WaitForExitAsync(patientId, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[Doctor] 病人进程已退出: {PatientId}, 状态={State}, 退出码={ExitCode}",
                patientId, exitInfo.State, exitInfo.ExitCode);

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
            _logger?.LogWarning("[Doctor] 医生模式被取消");
            await _patientManager.KillAllAsync().ConfigureAwait(false);
            return BuildReport(startedAt, _patientManager.GetPatientInfo(patientId), DoctorReportStatus.Failed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Doctor] 医生模式运行异常");
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
        _logger?.LogInformation("[Doctor] 医生 SSE 服务器模式启动，端口: {Port}", port);

        try
        {
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[Doctor] SSE 服务器已启动，等待病人连接...");

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("[Doctor] 医生 SSE 服务器被停止");
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

        _logger?.LogInformation("[Doctor] 测试套件启动，共 {Count} 个用例", testCases.Count());

        foreach (var (testCaseId, testName, arguments) in testCases)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger?.LogInformation("[Doctor] 执行测试: {TestId} - {TestName}", testCaseId, testName);

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
        _logger?.LogInformation("[Doctor] 病人进程退出事件: {PatientId}, PID={ProcessId}, 状态={State}", info.PatientId, info.ProcessId, info.State);

        _diagnosticEngine.EvaluateProcessHung(info);
    }

    private void OnDiagnosticEventReceived(object? sender, DiagnosticEvent evt)
    {
        _logger?.LogDebug("[Doctor] 收到诊断事件: {EventType} (病人: {PatientId})", evt.EventType, evt.PatientId);
        _diagnosticEngine.Evaluate(evt);
    }

    private void OnPatientConnected(object? sender, string patientId)
    {
        _logger?.LogInformation("[Doctor] 病人已连接: {PatientId}", patientId);
    }

    private void OnPatientDisconnected(object? sender, string patientId)
    {
        _logger?.LogInformation("[Doctor] 病人已断开: {PatientId}", patientId);
    }

    private void OnDiagnosticReportGenerated(object? sender, DiagnosticReport report)
    {
        _logger?.LogWarning("[Doctor] 诊断报告: {RuleId} - {Description} (病人: {PatientId})", report.RuleId, report.Description, report.PatientId);

        if (!AutoFixEnabled) return;

        _ = Task.Run(async () =>
        {
            if (!await _fixLock.WaitAsync(0).ConfigureAwait(false)) return;

            try
            {
                var result = await _hotFixEngine.ExecuteFixAsync(report).ConfigureAwait(false);

                await _fixLock.WaitAsync().ConfigureAwait(false);
                try { _fixResults.Add(result); }
                finally { _fixLock.Release(); }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Doctor] 自动修复异常: {RuleId} (病人: {PatientId})", report.RuleId, report.PatientId);
            }
            finally
            {
                _fixLock.Release();
            }
        });
    }

    private void OnFixApplied(object? sender, HotFixResult result)
    {
        _logger?.LogInformation("[Doctor] 修复已应用: {ActionType} (病人: {PatientId})", result.Action.ActionType, result.PatientId);
    }

    private void OnFixRolledBack(object? sender, HotFixResult result)
    {
        _logger?.LogWarning("[Doctor] 修复已回滚: {ActionType} (病人: {PatientId})", result.Action.ActionType, result.PatientId);
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
