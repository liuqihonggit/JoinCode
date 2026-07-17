namespace Core.Agents.Doctor;

/// <summary>
/// 医生 Agent — 监控 jcc.exe 子进程运行状态，自动诊断和修复问题
/// 不继承 BuiltInAgentBase（不需要 IChatClient/LLM），独立实现进程监控+诊断+修复循环
/// </summary>
public sealed class DoctorAgent : IAsyncDisposable
{
    private readonly PatientProcessManager _patientManager;
    private readonly DoctorIpcClient _ipcClient;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly ILogger? _logger;
    private readonly List<DiagnosticReport> _diagnostics = [];
    private readonly List<HotFixResult> _fixResults = [];
    private int _isDisposed;

    /// <summary>Agent 名称</summary>
    public string Name => "DoctorAgent";

    /// <summary>Agent 描述</summary>
    public string Description => "监控 jcc.exe 子进程运行状态，自动诊断和修复问题";

    /// <summary>累计诊断报告</summary>
    public IReadOnlyList<DiagnosticReport> Diagnostics => _diagnostics;

    /// <summary>累计修复结果</summary>
    public IReadOnlyList<HotFixResult> FixResults => _fixResults;

    public DoctorAgent(
        IFileSystem fs,
        IProcessService processService,
        ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;

        _patientManager = new PatientProcessManager(processService, logger);
        _ipcClient = new DoctorIpcClient(_patientManager, logger);

        _patientManager.ProcessExited += OnProcessExited;
        _ipcClient.EventReceived += OnDiagnosticEventReceived;
    }

    /// <summary>
    /// 运行医生模式 — 启动病人进程，监控其运行，直到退出
    /// </summary>
    /// <param name="patientArguments">病人进程命令行参数</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="environmentVariables">额外环境变量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>医生报告</returns>
    public async Task<DoctorReport> RunAsync(
        string patientArguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        _logger?.LogInformation("[Doctor] 医生模式启动，病人参数: {Args}", patientArguments);

        try
        {
            var patientInfo = await _patientManager.SpawnAsync(
                patientArguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);

            await _ipcClient.ConnectAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[Doctor] 病人进程已启动 (PID={ProcessId})，开始监控", patientInfo.ProcessId);

            var exitInfo = await _patientManager.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[Doctor] 病人进程已退出: 状态={State}, 退出码={ExitCode}",
                exitInfo.State, exitInfo.ExitCode);

            return BuildReport(startedAt, exitInfo);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("[Doctor] 医生模式被取消");
            _patientManager.Kill();
            return BuildReport(startedAt, _patientManager.Info, DoctorReportStatus.Failed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Doctor] 医生模式运行异常");
            _patientManager.Kill();
            return BuildReport(startedAt, _patientManager.Info, DoctorReportStatus.Failed);
        }
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
                var report = await RunAsync(arguments, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);
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
            Patient = _patientManager.Info,
            Diagnostics = _diagnostics,
            FixResults = _fixResults,
            TestResults = testResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = testResults.All(r => r.Status == DoctorTestStatus.Pass)
                ? DoctorReportStatus.Completed
                : DoctorReportStatus.PartiallyFixed
        };
    }

    /// <summary>
    /// 获取病人进程管理器（供外部访问）
    /// </summary>
    public PatientProcessManager GetPatientManager() => _patientManager;

    /// <summary>
    /// 获取 IPC 客户端（供外部访问）
    /// </summary>
    public DoctorIpcClient GetIpcClient() => _ipcClient;

    private void OnProcessExited(object? sender, PatientInfo info)
    {
        _logger?.LogInformation("[Doctor] 病人进程退出事件: PID={ProcessId}, 状态={State}", info.ProcessId, info.State);

        if (info.State == PatientState.Hung)
        {
            _diagnostics.Add(new DiagnosticReport
            {
                RuleId = DiagnosticRuleId.ProcessHung,
                Severity = DiagnosticSeverity.Critical,
                Description = $"病人进程卡死（退出码 1234），PID={info.ProcessId}",
                SuggestedFixType = HotFixActionType.RestartProcess,
                SuggestedFixDescription = "重启病人进程"
            });
        }
    }

    private void OnDiagnosticEventReceived(object? sender, DiagnosticEvent evt)
    {
        _logger?.LogDebug("[Doctor] 收到诊断事件: {EventType}", evt.EventType);
    }

    private DoctorReport BuildReport(
        DateTimeOffset startedAt,
        PatientInfo? patientInfo,
        DoctorReportStatus status = DoctorReportStatus.Running)
    {
        return new DoctorReport
        {
            Patient = patientInfo,
            Diagnostics = _diagnostics,
            FixResults = _fixResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = status
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _patientManager.ProcessExited -= OnProcessExited;
        _ipcClient.EventReceived -= OnDiagnosticEventReceived;

        await _ipcClient.DisposeAsync().ConfigureAwait(false);
        await _patientManager.DisposeAsync().ConfigureAwait(false);
    }
}
