namespace JoinCode.Entry;

using Core.Agents.Doctor;

/// <summary>
/// 医生模式运行器 — jcc.exe --doctor 入口
/// spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题
/// </summary>
internal static class DoctorModeRunner
{
    internal static async Task<int> RunAsync(CommandLineOptions options)
    {
        Cli.TerminalHelper.Init();
        Diag.WriteLifecycle("[DOCTOR] 医生模式启动");

        var fs = IO.FileSystem.FileSystemFactory.Create();
        var processService = new IO.ProcessService.PhysicalProcessService();

        var port = options.DoctorPort ?? 9902;
        var transport = new DoctorSseServer(port);

        await using var doctor = new DoctorAgent(fs, processService, transport);

        if (!string.IsNullOrWhiteSpace(options.DoctorEndpoint))
        {
            Diag.WriteLifecycle($"[DOCTOR] SSE 服务器模式，端口: {port}");
            var report = await doctor.RunServerAsync(port).ConfigureAwait(false);
            PrintReport(report);
            return report.Status switch
            {
                DoctorReportStatus.Completed => 0,
                DoctorReportStatus.PartiallyFixed => 1,
                _ => 2
            };
        }

        var patientArgs = BuildPatientArguments(options, port);
        var workingDir = fs.GetCurrentDirectory();

        Diag.WriteLifecycle($"[DOCTOR] 病人参数: {patientArgs}");

        try
        {
            var runReport = await doctor.RunAsync("patient-main", patientArgs, workingDir, cancellationToken: default).ConfigureAwait(false);

            PrintReport(runReport);

            return runReport.Status switch
            {
                DoctorReportStatus.Completed => 0,
                DoctorReportStatus.PartiallyFixed => 1,
                _ => 2
            };
        }
        catch (Exception ex)
        {
            Diag.WriteLifecycle($"[DOCTOR] 运行异常: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                Diag.WriteLifecycle($"[DOCTOR] 内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return 2;
        }
    }

    /// <summary>
    /// 医生测试套件模式 — 执行内置功能测试用例（T001-T006）
    /// </summary>
    internal static async Task<int> RunTestSuiteAsync(CommandLineOptions options)
    {
        Cli.TerminalHelper.Init();
        Diag.WriteLine("[DOCTOR] 医生测试套件模式启动");

        var fs = IO.FileSystem.FileSystemFactory.Create();
        var processService = new IO.ProcessService.PhysicalProcessService();

        var port = options.DoctorPort ?? 9902;

        var testSuite = new DoctorTestSuite();
        var results = new List<DoctorTestCaseResult>();

        foreach (var testCase in DoctorTestSuite.BuiltInTests)
        {
            Diag.WriteLine($"[DOCTOR] 执行测试: {testCase.TestCaseId} - {testCase.TestName}");

            var testPort = port + results.Count;
            var transport = new DoctorSseServer(testPort);
            await using var doctor = new DoctorAgent(fs, processService, transport);

            var patientArgs = DoctorTestSuite.BuildPatientArguments(testCase) + $" --doctor-endpoint http://localhost:{testPort}";
            var workingDir = fs.GetCurrentDirectory();

            var envVars = new Dictionary<string, string>
            {
                ["JCC_TEST_CASE_ID"] = testCase.TestCaseId
            };

            if (!string.IsNullOrWhiteSpace(options.DoctorEndpoint))
                envVars["JCC_ENDPOINT"] = options.DoctorEndpoint;
            if (!string.IsNullOrWhiteSpace(options.Model))
                envVars["JCC_MODEL_ID"] = options.Model;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(testCase.TimeoutSeconds));
                var report = await doctor.RunAsync($"test-{testCase.TestCaseId}", patientArgs, workingDir, envVars, cts.Token).ConfigureAwait(false);
                sw.Stop();

                var status = DoctorTestSuite.DetermineTestStatus(report, testCase);
                results.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCase.TestCaseId,
                    TestName = testCase.TestName,
                    Status = status,
                    Duration = sw.Elapsed
                });
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                results.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCase.TestCaseId,
                    TestName = testCase.TestName,
                    Status = DoctorTestStatus.Hung,
                    Duration = sw.Elapsed,
                    ErrorMessage = $"测试超时 ({testCase.TimeoutSeconds}s)"
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new DoctorTestCaseResult
                {
                    TestCaseId = testCase.TestCaseId,
                    TestName = testCase.TestName,
                    Status = DoctorTestStatus.Error,
                    Duration = sw.Elapsed,
                    ErrorMessage = ex.Message
                });
            }
        }

        var suiteReport = new DoctorTestSuiteReport
        {
            Results = results,
            TotalCount = results.Count,
            PassCount = results.Count(r => r.Status == DoctorTestStatus.Pass),
            FailCount = results.Count(r => r.Status == DoctorTestStatus.Fail),
            HungCount = results.Count(r => r.Status == DoctorTestStatus.Hung),
            ErrorCount = results.Count(r => r.Status == DoctorTestStatus.Error),
            SkippedCount = results.Count(r => r.Status == DoctorTestStatus.Skipped),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            IsAllPassed = results.All(r => r.Status == DoctorTestStatus.Pass)
        };

        PrintTestSuiteReport(suiteReport);

        return suiteReport.IsAllPassed ? 0 : 1;
    }

    /// <summary>
    /// 构建病人进程参数 — 从医生的 CLI 参数推导
    /// </summary>
    private static string BuildPatientArguments(CommandLineOptions options, int? doctorPort = null)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("--trust");

        if (doctorPort.HasValue)
            sb.Append($" --doctor-endpoint http://localhost:{doctorPort.Value}");

        if (options.Verbose)
            sb.Append(" --verbose");

        if (!string.IsNullOrWhiteSpace(options.Prompt))
            sb.Append($" -p \"{options.Prompt}\"");

        if (!string.IsNullOrWhiteSpace(options.Model))
            sb.Append($" -m \"{options.Model}\"");

        if (options.AwaitTimeoutSeconds is { } awaitSeconds && awaitSeconds > 0)
            sb.Append($" --await {awaitSeconds}");

        if (options.ForceInteractive)
            sb.Append(" --force-interactive");

        if (!string.IsNullOrWhiteSpace(options.PermissionMode))
            sb.Append($" --permission-mode \"{options.PermissionMode}\"");

        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
            sb.Append($" --system-prompt \"{options.SystemPrompt}\"");

        if (!string.IsNullOrWhiteSpace(options.AppendSystemPrompt))
            sb.Append($" --append-system-prompt \"{options.AppendSystemPrompt}\"");

        return sb.ToString();
    }

    /// <summary>
    /// 打印医生报告
    /// </summary>
    private static void PrintReport(DoctorReport report)
    {
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
        Cli.TerminalHelper.WriteLine("  医生报告");
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");

        if (report.Patient is not null)
        {
            Cli.TerminalHelper.WriteLine($"  病人 ID:     {report.Patient.PatientId}");
            Cli.TerminalHelper.WriteLine($"  病人 PID:    {report.Patient.ProcessId}");
            Cli.TerminalHelper.WriteLine($"  病人状态:    {report.Patient.State}");
            Cli.TerminalHelper.WriteLine($"  退出码:      {report.Patient.ExitCode}");
            Cli.TerminalHelper.WriteLine($"  启动时间:    {report.Patient.StartedAt:HH:mm:ss}");
            if (report.Patient.ExitedAt.HasValue)
                Cli.TerminalHelper.WriteLine($"  退出时间:    {report.Patient.ExitedAt.Value:HH:mm:ss}");
        }

        if (report.Patients.Count > 1)
        {
            Cli.TerminalHelper.WriteLine($"  病人总数:    {report.Patients.Count}");
            foreach (var kv in report.Patients)
            {
                Cli.TerminalHelper.WriteLine($"    {kv.Key}: PID={kv.Value.ProcessId}, 状态={kv.Value.State}");
            }
        }

        Cli.TerminalHelper.WriteLine($"  诊断数量:    {report.Diagnostics.Count}");
        Cli.TerminalHelper.WriteLine($"  修复数量:    {report.FixResults.Count}");
        Cli.TerminalHelper.WriteLine($"  测试数量:    {report.TestResults.Count}");
        Cli.TerminalHelper.WriteLine($"  总体状态:    {report.Status}");

        if (report.Diagnostics.Count > 0)
        {
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteLine("  ── 诊断详情 ──");
            foreach (var diag in report.Diagnostics)
            {
                Cli.TerminalHelper.WriteLine($"  [{diag.Severity}] {diag.RuleId} (病人: {diag.PatientId}): {diag.Description}");
            }
        }

        if (report.TestResults.Count > 0)
        {
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteLine("  ── 测试结果 ──");
            foreach (var test in report.TestResults)
            {
                var statusIcon = test.Status switch
                {
                    DoctorTestStatus.Pass => "✓",
                    DoctorTestStatus.Fail => "✗",
                    DoctorTestStatus.Hung => "⏱",
                    _ => "?"
                };
                Cli.TerminalHelper.WriteLine($"  {statusIcon} {test.TestCaseId}: {test.TestName} ({test.Duration.TotalMilliseconds:F0}ms)");
            }
        }

        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
    }

    /// <summary>
    /// 打印测试套件报告
    /// </summary>
    private static void PrintTestSuiteReport(DoctorTestSuiteReport report)
    {
        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
        Cli.TerminalHelper.WriteLine("  医生测试套件报告");
        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
        Cli.TerminalHelper.WriteLine($"  总数:  {report.TotalCount}");
        Cli.TerminalHelper.WriteLine($"  通过:  {report.PassCount}");
        Cli.TerminalHelper.WriteLine($"  失败:  {report.FailCount}");
        Cli.TerminalHelper.WriteLine($"  卡死:  {report.HungCount}");
        Cli.TerminalHelper.WriteLine($"  错误:  {report.ErrorCount}");
        Cli.TerminalHelper.WriteLine($"  跳过:  {report.SkippedCount}");
        Cli.TerminalHelper.WriteLine($"  耗时:  {report.Duration.TotalSeconds:F1}s");
        Cli.TerminalHelper.WriteLine($"  结果:  {(report.IsAllPassed ? "ALL PASSED" : "HAS FAILURES")}");

        Cli.TerminalHelper.NewLine();
        Cli.TerminalHelper.WriteLine("  ── 用例详情 ──");
        foreach (var test in report.Results)
        {
            var statusIcon = test.Status switch
            {
                DoctorTestStatus.Pass => "PASS",
                DoctorTestStatus.Fail => "FAIL",
                DoctorTestStatus.Hung => "HUNG",
                DoctorTestStatus.Error => "ERR ",
                DoctorTestStatus.Skipped => "SKIP",
                _ => "????"
            };
            Cli.TerminalHelper.WriteLine($"  [{statusIcon}] {test.TestCaseId}: {test.TestName} ({test.Duration.TotalMilliseconds:F0}ms)");
            if (!string.IsNullOrEmpty(test.ErrorMessage))
                Cli.TerminalHelper.WriteLine($"         {test.ErrorMessage}");
        }

        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
    }
}
