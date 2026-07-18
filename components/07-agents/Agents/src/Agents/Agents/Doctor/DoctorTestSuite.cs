namespace Core.Agents.Doctor;

/// <summary>
/// 测试用例定义
/// </summary>
public sealed record DoctorTestCase
{
    /// <summary>测试用例 ID（如 T001）</summary>
    public required string TestCaseId { get; init; }

    /// <summary>测试名称</summary>
    public required string TestName { get; init; }

    /// <summary>发送给病人的提示词</summary>
    public required string Prompt { get; init; }

    /// <summary>超时时间（秒）</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>预期病人退出码（0=正常完成）</summary>
    public int ExpectedExitCode { get; init; } = 0;

    /// <summary>测试类别</summary>
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// 医生测试套件 — 定义和执行 jcc.exe 功能测试用例
/// 每条用例启动一个病人进程，发送提示词，等待完成并收集结果
/// </summary>
public sealed class DoctorTestSuite
{
    private readonly ILogger? _logger;

    /// <summary>测试用例执行完成事件</summary>
    public event EventHandler<DoctorTestCaseResult>? TestCaseCompleted;

    /// <summary>测试套件执行完成事件</summary>
    public event EventHandler<DoctorTestSuiteReport>? SuiteCompleted;

    /// <summary>内置测试用例</summary>
    public static IReadOnlyList<DoctorTestCase> BuiltInTests =>
    [
        new DoctorTestCase
        {
            TestCaseId = "T001",
            TestName = "FileRead",
            Prompt = "读取当前目录的README.md",
            TimeoutSeconds = 30,
            Category = "tool"
        },
        new DoctorTestCase
        {
            TestCaseId = "T002",
            TestName = "FileEdit",
            Prompt = "在test.txt写入hello world",
            TimeoutSeconds = 30,
            Category = "tool"
        },
        new DoctorTestCase
        {
            TestCaseId = "T003",
            TestName = "ShellExec",
            Prompt = "运行 git status",
            TimeoutSeconds = 30,
            Category = "tool"
        },
        new DoctorTestCase
        {
            TestCaseId = "T004",
            TestName = "CodeSearch",
            Prompt = "搜索ErrorCode枚举定义",
            TimeoutSeconds = 30,
            Category = "tool"
        },
        new DoctorTestCase
        {
            TestCaseId = "T005",
            TestName = "SubAgent",
            Prompt = "创建一个探索子代理分析项目结构",
            TimeoutSeconds = 60,
            Category = "agent"
        },
        new DoctorTestCase
        {
            TestCaseId = "T006",
            TestName = "Compact",
            Prompt = "压缩上下文",
            TimeoutSeconds = 30,
            Category = "agent"
        }
    ];

    public DoctorTestSuite(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 执行全部内置测试用例
    /// </summary>
    public async Task<DoctorTestSuiteReport> RunAllAsync(
        DoctorAgent doctor,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(doctor, BuiltInTests, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行指定测试用例
    /// </summary>
    public async Task<DoctorTestSuiteReport> RunAsync(
        DoctorAgent doctor,
        IEnumerable<DoctorTestCase> testCases,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doctor);

        var caseList = testCases.ToList();
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<DoctorTestCaseResult>();

        _logger?.LogInformation("[DoctorTestSuite] 开始执行 {Count} 个测试用例", caseList.Count);

        foreach (var testCase in caseList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCase.TestCaseId,
                    TestName = testCase.TestName,
                    Status = DoctorTestStatus.Skipped,
                    ErrorMessage = "测试套件被取消"
                });
                continue;
            }

            var result = await RunSingleAsync(doctor, testCase, workingDirectory, environmentVariables, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            TestCaseCompleted?.Invoke(this, result);
        }

        var report = new DoctorTestSuiteReport
        {
            Results = results,
            TotalCount = results.Count,
            PassCount = results.Count(r => r.Status == DoctorTestStatus.Pass),
            FailCount = results.Count(r => r.Status == DoctorTestStatus.Fail),
            HungCount = results.Count(r => r.Status == DoctorTestStatus.Hung),
            ErrorCount = results.Count(r => r.Status == DoctorTestStatus.Error),
            SkippedCount = results.Count(r => r.Status == DoctorTestStatus.Skipped),
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            IsAllPassed = results.All(r => r.Status == DoctorTestStatus.Pass)
        };

        SuiteCompleted?.Invoke(this, report);

        _logger?.LogInformation("[DoctorTestSuite] 执行完成: {Pass}/{Total} 通过", report.PassCount, report.TotalCount);

        return report;
    }

    /// <summary>
    /// 执行单个测试用例 — 构建病人参数，启动 DoctorAgent.RunAsync，收集结果
    /// </summary>
    private async Task<DoctorTestCaseResult> RunSingleAsync(
        DoctorAgent doctor,
        DoctorTestCase testCase,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("[DoctorTestSuite] 执行测试: {TestId} - {TestName}", testCase.TestCaseId, testCase.TestName);

        var patientArgs = BuildPatientArguments(testCase);
        var patientId = $"test-{testCase.TestCaseId}";
        var envVars = BuildEnvironmentVariables(environmentVariables, testCase);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(testCase.TimeoutSeconds));

            var report = await doctor.RunAsync(patientId, patientArgs, workingDirectory, envVars, cts.Token).ConfigureAwait(false);
            sw.Stop();

            var status = DetermineTestStatus(report, testCase);

            return new DoctorTestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestName = testCase.TestName,
                Status = status,
                Duration = sw.Elapsed,
                ErrorMessage = status != DoctorTestStatus.Pass ? BuildErrorMessage(report, testCase) : null
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger?.LogWarning("[DoctorTestSuite] 测试超时: {TestId} ({Timeout}s)", testCase.TestCaseId, testCase.TimeoutSeconds);
            return new DoctorTestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestName = testCase.TestName,
                Status = DoctorTestStatus.Hung,
                Duration = sw.Elapsed,
                ErrorMessage = $"测试超时 ({testCase.TimeoutSeconds}s)"
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new DoctorTestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestName = testCase.TestName,
                Status = DoctorTestStatus.Skipped,
                Duration = sw.Elapsed,
                ErrorMessage = "测试套件被取消"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogError(ex, "[DoctorTestSuite] 测试异常: {TestId}", testCase.TestCaseId);
            return new DoctorTestCaseResult
            {
                TestCaseId = testCase.TestCaseId,
                TestName = testCase.TestName,
                Status = DoctorTestStatus.Error,
                Duration = sw.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 构建病人进程参数 — 从测试用例推导 jcc.exe 命令行
    /// </summary>
    public static string BuildPatientArguments(DoctorTestCase testCase)
    {
        var sb = new StringBuilder();
        sb.Append("--trust");
        sb.Append($" -p \"{testCase.Prompt}\"");
        sb.Append($" --await {testCase.TimeoutSeconds}");
        return sb.ToString();
    }

    /// <summary>
    /// 构建环境变量 — 合并基础变量和测试特有变量
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildEnvironmentVariables(
        IReadOnlyDictionary<string, string>? baseVars,
        DoctorTestCase testCase)
    {
        if (baseVars is null) return null;

        var result = new Dictionary<string, string>(baseVars);
        result["JCC_TEST_CASE_ID"] = testCase.TestCaseId;
        return result;
    }

    /// <summary>
    /// 根据医生报告和测试用例预期判断测试状态
    /// </summary>
    public static DoctorTestStatus DetermineTestStatus(DoctorReport report, DoctorTestCase testCase)
    {
        var patient = report.Patients.Values.FirstOrDefault(p => p.PatientId.StartsWith($"test-{testCase.TestCaseId}"))
            ?? report.Patient;

        if (patient is null)
            return DoctorTestStatus.Error;

        return patient.State switch
        {
            PatientState.Completed when patient.ExitCode == testCase.ExpectedExitCode => DoctorTestStatus.Pass,
            PatientState.Completed => DoctorTestStatus.Fail,
            PatientState.Hung => DoctorTestStatus.Hung,
            PatientState.Failed => DoctorTestStatus.Fail,
            PatientState.Killed => DoctorTestStatus.Fail,
            _ => DoctorTestStatus.Error
        };
    }

    /// <summary>
    /// 构建错误消息 — 从医生报告中提取诊断和修复信息
    /// </summary>
    private static string? BuildErrorMessage(DoctorReport report, DoctorTestCase testCase)
    {
        var parts = new List<string>();

        var patient = report.Patients.Values.FirstOrDefault(p => p.PatientId.StartsWith($"test-{testCase.TestCaseId}"))
            ?? report.Patient;

        if (patient is not null)
            parts.Add($"病人状态: {patient.State}, 退出码: {patient.ExitCode}");

        var relevantDiags = report.Diagnostics
            .Where(d => d.PatientId.StartsWith($"test-{testCase.TestCaseId}"))
            .ToList();

        if (relevantDiags.Count > 0)
            parts.Add($"诊断: {string.Join("; ", relevantDiags.Select(d => $"{d.RuleId}: {d.Description}"))}");

        var relevantFixes = report.FixResults
            .Where(f => f.PatientId.StartsWith($"test-{testCase.TestCaseId}"))
            .ToList();

        if (relevantFixes.Count > 0)
            parts.Add($"修复: {string.Join("; ", relevantFixes.Select(f => $"{f.Action.ActionType}: {(f.Success ? "成功" : "失败")}"))}");

        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }
}

/// <summary>
/// 测试套件报告 — 一次完整测试套件执行的结果汇总
/// </summary>
public sealed record DoctorTestSuiteReport
{
    /// <summary>测试结果列表</summary>
    public required IReadOnlyList<DoctorTestCaseResult> Results { get; init; }

    /// <summary>总测试数</summary>
    public required int TotalCount { get; init; }

    /// <summary>通过数</summary>
    public required int PassCount { get; init; }

    /// <summary>失败数</summary>
    public required int FailCount { get; init; }

    /// <summary>卡死数</summary>
    public required int HungCount { get; init; }

    /// <summary>错误数</summary>
    public required int ErrorCount { get; init; }

    /// <summary>跳过数</summary>
    public required int SkippedCount { get; init; }

    /// <summary>开始时间</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>完成时间</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>是否全部通过</summary>
    public required bool IsAllPassed { get; init; }

    /// <summary>总耗时</summary>
    public TimeSpan Duration => CompletedAt - StartedAt;
}
