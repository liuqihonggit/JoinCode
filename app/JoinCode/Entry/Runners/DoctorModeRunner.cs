namespace JoinCode.Entry;

using Core.Agents.Doctor;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Doctor;
using JoinCode.Abstractions.LLM;

/// <summary>
/// 医生模式运行器 — jcc.exe --doctor 入口
/// 启动病人进程获取遥测，LLM 判断是否需要修复 jcc 自身源码
/// </summary>
internal static class DoctorModeRunner
{
    internal static async Task<int> RunAsync(CommandLineOptions options, IServiceProvider services)
    {
        Cli.TerminalHelper.Init();
        Diag.WriteLifecycle("[DOCTOR] 自举医生模式启动");

        var fs = IO.FileSystem.FileSystemFactory.Create();
        var processService = new IO.ProcessService.PhysicalProcessService();

        var port = options.DoctorPort ?? 9902;
        var transport = new DoctorTcpServer(port);
        var patientManager = new PatientProcessManager(processService);

        var sourceEngine = new SourceCodeEngine(fs);
        var worktreeMgr = new BootstrapWorktreeManager(fs);
        var guard = new DefaultBootstrapGuard(fs);

        var (chatClient, queryService) = ResolveLlmServices(services);
        var patchGenerator = new LlmCodePatchGenerator(queryService);

        var diagnosticEngine = new DiagnosticEngine();
        var logWatcher = new DiagnosticLogWatcher(fs, diagnosticEngine);

        await using var agent = new BootstrapAgent(
            chatClient, sourceEngine, worktreeMgr, patchGenerator, guard,
            patientManager, fs, transport, logWatcher: logWatcher);

        if (options.DoctorServerMode)
        {
            Diag.WriteLifecycle($"[DOCTOR] SSE 服务器模式，端口: {port}");
            var report = await agent.RunServerAsync(port).ConfigureAwait(false);
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
            var runReport = await agent.RunWithPatientAsync("patient-main", patientArgs, workingDir).ConfigureAwait(false);

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

    private static (IChatClient, IQueryService) ResolveLlmServices(IServiceProvider services)
    {
        var queryEngine = services.GetService<IQueryEngine>();
        if (queryEngine is not null)
        {
            var chatClient = queryEngine.GetKernel();
            var queryService = queryEngine.GetChatCompletionService();
            if (chatClient is not null && queryService is not null)
            {
                Diag.WriteLifecycle("[DOCTOR] 通过 IQueryEngine 解析 LLM 服务成功");
                return (chatClient, queryService);
            }
        }

        var chatClientFallback = services.GetService<IChatClient>();
        var queryServiceFallback = services.GetService<IQueryService>();
        if (chatClientFallback is not null && queryServiceFallback is not null)
        {
            Diag.WriteLifecycle("[DOCTOR] 通过 DI 直接解析 IChatClient + IQueryService 成功");
            return (chatClientFallback, queryServiceFallback);
        }

        throw new InvalidOperationException(
            "无法从 DI 容器解析 LLM 服务。请确保配置了有效的 API Key 和 Provider。" +
            "可通过环境变量 JCC_API_KEY + JCC_PROVIDER 或 .env/api.json 配置。");
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

        Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
    }
}
