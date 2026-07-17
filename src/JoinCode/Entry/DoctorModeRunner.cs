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
        Diag.WriteLine("[DOCTOR] 医生模式启动");

        var fs = IO.FileSystem.FileSystemFactory.Create();
        var processService = new IO.ProcessService.PhysicalProcessService();

        await using var doctor = new DoctorAgent(fs, processService);

        var patientArgs = BuildPatientArguments(options);
        var workingDir = fs.GetCurrentDirectory();

        Diag.WriteLine($"[DOCTOR] 病人参数: {patientArgs}");

        var report = await doctor.RunAsync(patientArgs, workingDir, cancellationToken: default).ConfigureAwait(false);

        PrintReport(report);

        return report.Status switch
        {
            DoctorReportStatus.Completed => 0,
            DoctorReportStatus.PartiallyFixed => 1,
            _ => 2
        };
    }

    /// <summary>
    /// 构建病人进程参数 — 从医生的 CLI 参数推导
    /// </summary>
    private static string BuildPatientArguments(CommandLineOptions options)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("--trust");

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
            Cli.TerminalHelper.WriteLine($"  病人 PID:    {report.Patient.ProcessId}");
            Cli.TerminalHelper.WriteLine($"  病人状态:    {report.Patient.State}");
            Cli.TerminalHelper.WriteLine($"  退出码:      {report.Patient.ExitCode}");
            Cli.TerminalHelper.WriteLine($"  启动时间:    {report.Patient.StartedAt:HH:mm:ss}");
            if (report.Patient.ExitedAt.HasValue)
                Cli.TerminalHelper.WriteLine($"  退出时间:    {report.Patient.ExitedAt.Value:HH:mm:ss}");
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
                Cli.TerminalHelper.WriteLine($"  [{diag.Severity}] {diag.RuleId}: {diag.Description}");
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
}
