namespace Core.Agents.Doctor;


/// <summary>
/// 自举后台 Agent — jcc --doctor 入口，LLM 驱动，监控病人遥测并修复 jcc 自身源码
/// 数据源: IPC 遥测（病人进程）+ 日志文件双源
/// 触发: LLM 判断 + 用户确认
/// 修复: BootstrapLoop 源码工程闭环
/// </summary>
public sealed class BootstrapAgent : IAsyncDisposable
{
    private readonly IChatClient _chatClient;
    private readonly ISourceCodeEngine _sourceEngine;
    private readonly IBootstrapWorktreeManager _worktreeMgr;
    private readonly ICodePatchGenerator _patchGenerator;
    private readonly IBootstrapGuard _guard;
    private readonly IReflexionMemory? _memory;
    private readonly IDoctorTransport _transport;
    private readonly DiagnosticEngine _diagnosticEngine;
    private readonly PatientProcessManager _patientManager;
    private readonly IFileSystem _fs;
    private readonly DiagnosticLogWatcher? _logWatcher;
    private readonly List<DiagnosticReport> _pendingReports = [];
    private readonly AsyncLock _reportsLock = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(30);
    private readonly Func<string, string, Task<bool>> _confirmCallback;
    private int _isDisposed;

    public IDoctorTransport Transport => _transport;
    public IEnumerable<DiagnosticReport> PendingReports
    {
        get
        {
            using var guard = _reportsLock.TryLock(TimeSpan.Zero);
            if (guard is not null)
            {
                return _pendingReports;
            }
            return _pendingReports.ToList();
        }
    }

    public BootstrapAgent(
        IChatClient chatClient,
        ISourceCodeEngine sourceEngine,
        IBootstrapWorktreeManager worktreeMgr,
        ICodePatchGenerator patchGenerator,
        IBootstrapGuard guard,
        PatientProcessManager patientManager,
        IFileSystem fs,
        IDoctorTransport? transport = null,
        IReflexionMemory? memory = null,
        Func<string, string, Task<bool>>? confirmCallback = null,
        DiagnosticLogWatcher? logWatcher = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _sourceEngine = sourceEngine ?? throw new ArgumentNullException(nameof(sourceEngine));
        _worktreeMgr = worktreeMgr ?? throw new ArgumentNullException(nameof(worktreeMgr));
        _patchGenerator = patchGenerator ?? throw new ArgumentNullException(nameof(patchGenerator));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _patientManager = patientManager ?? throw new ArgumentNullException(nameof(patientManager));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _transport = transport ?? new DoctorTcpServer(9902);
        _memory = memory;
        _diagnosticEngine = new DiagnosticEngine();
        _confirmCallback = confirmCallback ?? DefaultConfirmAsync;
        _logWatcher = logWatcher;

        _transport.EventReceived += OnDiagnosticEventReceived;
        _diagnosticEngine.DiagnosticReportGenerated += OnDiagnosticReportGenerated;

        if (_logWatcher is not null)
        {
            _logWatcher.EventDetected += OnLogWatcherEventDetected;
            _logWatcher.Start();
        }
    }

    /// <summary>
    /// SSE 服务器模式 — 等待病人连接，接收遥测事件，LLM 判断后修复
    /// 复用 SSE 服务器模式，用 BootstrapLoop 实现源码工程修复闭环
    /// 韧性由 DoctorTcpServer/DoctorSseClient 的指数退避重连保证
    /// </summary>
    public async Task<DoctorReport> RunServerAsync(
        int port = 9902,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DoctorDiag.Write($"[Bootstrap] SSE 服务器模式启动，端口: {port}");

        var diagnostics = new List<DiagnosticReport>();
        var fixResults = new List<HotFixResult>();

        try
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
            DoctorDiag.Write("[Bootstrap] SSE 服务器已启动，等待病人连接...");

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_debounceInterval, ct).ConfigureAwait(false);

                List<DiagnosticReport> reportsToProcess;
                using var guard = await _reportsLock.LockAsync(ct).ConfigureAwait(false);

                reportsToProcess = [.. _pendingReports];
                _pendingReports.Clear();
            

                if (reportsToProcess.Count == 0) continue;

                diagnostics.AddRange(reportsToProcess);
                DoctorDiag.Write($"[Bootstrap] 累积 {reportsToProcess.Count} 个诊断报告，提交 LLM 判断");

                var judgment = await LlmJudgeAsync(reportsToProcess, ct).ConfigureAwait(false);
                if (!judgment.NeedsFix) continue;

                var confirmed = await _confirmCallback(FormatConfirmMessage(judgment), judgment.Reasoning ?? "").ConfigureAwait(false);
                if (!confirmed) continue;

                var diagnostic = reportsToProcess[0];
                var bootstrapLoop = new BootstrapLoop(_sourceEngine, _worktreeMgr, _patchGenerator, _guard, _fs, _memory);
                var result = await bootstrapLoop.ExecuteAsync(diagnostic, ct: ct).ConfigureAwait(false);

                if (result.Success)
                {
                    fixResults.Add(new HotFixResult
                    {
                        Success = true,
                        PatientId = diagnostic.PatientId,
                        Action = new HotFixAction { ActionType = HotFixActionType.SourceCodePatch, Description = result.Patch?.Description ?? "Bootstrap fix" },
                        Description = $"修复成功: {result.Patch?.TargetFilePath}"
                    });
                }

                ReportResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            DoctorDiag.Write("[Bootstrap] SSE 服务器被停止");
        }

        return new DoctorReport
        {
            Diagnostics = diagnostics,
            FixResults = fixResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = fixResults.Count == 0 ? DoctorReportStatus.Completed
                : fixResults.All(r => r.Success) ? DoctorReportStatus.Completed
                : DoctorReportStatus.PartiallyFixed
        };
    }

    /// <summary>
    /// 启动病人进程并运行自举监控主循环
    /// </summary>
    /// <param name="patientId">病人标识</param>
    /// <param name="patientArguments">命令行参数字符串（回退模式，<paramref name="patientArgumentList"/> 优先）</param>
    /// <param name="patientArgumentList">参数化启动列表 — 优先于 <paramref name="patientArguments"/></param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="environmentVariables">环境变量</param>
    /// <param name="ct">取消令牌</param>
    public async Task<DoctorReport> RunWithPatientAsync(
        string patientId,
        string patientArguments,
        IReadOnlyList<string>? patientArgumentList = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        DoctorDiag.Write($"[Bootstrap] 启动病人进程: {patientId}");
        DoctorDiag.Write($"[Bootstrap] 病人参数: {patientArguments}");

        var patient = await _patientManager.SpawnAsync(
            patientId, patientArguments, patientArgumentList, workingDirectory, environmentVariables, ct).ConfigureAwait(false);

        DoctorDiag.Write($"[Bootstrap] 病人已启动: PID={patient.ProcessId}, 等待遥测事件...");

        await _transport.ConnectAsync(ct).ConfigureAwait(false);
        DoctorDiag.Write("[Bootstrap] IPC 传输已连接");

        var startedAt = DateTimeOffset.UtcNow;
        var diagnostics = new List<DiagnosticReport>();
        var fixResults = new List<HotFixResult>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var patientState = _patientManager.GetPatientInfo(patientId)?.State ?? PatientState.NotStarted;
                if (patientState is PatientState.Completed or PatientState.Failed or PatientState.Killed)
                {
                    DoctorDiag.Write($"[Bootstrap] 病人进程已退出: {patientState}");
                    break;
                }

                await Task.Delay(_debounceInterval, ct).ConfigureAwait(false);

                List<DiagnosticReport> reportsToProcess;
                using var guard = await _reportsLock.LockAsync(ct).ConfigureAwait(false);

                reportsToProcess = [.. _pendingReports];
                _pendingReports.Clear();
            

                if (reportsToProcess.Count == 0) continue;

                diagnostics.AddRange(reportsToProcess);
                DoctorDiag.Write($"[Bootstrap] 累积 {reportsToProcess.Count} 个诊断报告，提交 LLM 判断");

                var judgment = await LlmJudgeAsync(reportsToProcess, ct).ConfigureAwait(false);

                if (!judgment.NeedsFix)
                {
                    DoctorDiag.Write($"[Bootstrap] LLM 判断: 无需源码修复 ({judgment.Reasoning})");
                    continue;
                }

                DoctorDiag.Write($"[Bootstrap] LLM 判断: 需要修复 {judgment.TargetFile} (优先级: {judgment.Priority})");

                var confirmed = await _confirmCallback(
                    FormatConfirmMessage(judgment),
                    judgment.Reasoning ?? "").ConfigureAwait(false);

                if (!confirmed)
                {
                    DoctorDiag.Write("[Bootstrap] 用户拒绝修复，继续监控");
                    continue;
                }

                var diagnostic = reportsToProcess[0];
                var bootstrapLoop = new BootstrapLoop(
                    _sourceEngine, _worktreeMgr, _patchGenerator, _guard, _fs, _memory);

                var result = await bootstrapLoop.ExecuteAsync(diagnostic, workingDirectory, ct).ConfigureAwait(false);

                if (result.Success)
                {
                    fixResults.Add(new HotFixResult
                    {
                        Success = true,
                        PatientId = patientId,
                        Action = new HotFixAction { ActionType = HotFixActionType.SourceCodePatch, Description = result.Patch?.Description ?? "Bootstrap fix" },
                        Description = $"修复成功: {result.Patch?.TargetFilePath}"
                    });
                }

                ReportResult(result);
            }
        }
        catch (OperationCanceledException) { }

        var finalPatient = _patientManager.GetPatientInfo(patientId);
        var patients = new Dictionary<string, PatientInfo>();
        if (finalPatient is not null) patients[patientId] = finalPatient;

        return new DoctorReport
        {
            Patients = patients,
            Diagnostics = diagnostics,
            FixResults = fixResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = fixResults.All(r => r.Success) ? DoctorReportStatus.Completed : DoctorReportStatus.PartiallyFixed
        };
    }

    /// <summary>
    /// 运行自举监控主循环（无病人进程，仅 IPC 监控）
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        DoctorDiag.Write("[Bootstrap] 自举监控主循环启动");

        await _transport.ConnectAsync(ct).ConfigureAwait(false);
        DoctorDiag.Write("[Bootstrap] IPC 传输已连接，等待诊断事件...");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_debounceInterval, ct).ConfigureAwait(false);

                List<DiagnosticReport> reportsToProcess;
                using var guard = await _reportsLock.LockAsync(ct).ConfigureAwait(false);

                reportsToProcess = [.. _pendingReports];
                _pendingReports.Clear();
            

                if (reportsToProcess.Count == 0) continue;

                DoctorDiag.Write($"[Bootstrap] 累积 {reportsToProcess.Count} 个诊断报告，提交 LLM 判断");

                var judgment = await LlmJudgeAsync(reportsToProcess, ct).ConfigureAwait(false);

                if (!judgment.NeedsFix)
                {
                    DoctorDiag.Write($"[Bootstrap] LLM 判断: 无需源码修复 ({judgment.Reasoning})");
                    continue;
                }

                DoctorDiag.Write($"[Bootstrap] LLM 判断: 需要修复 {judgment.TargetFile} (优先级: {judgment.Priority})");

                var confirmed = await _confirmCallback(
                    FormatConfirmMessage(judgment),
                    judgment.Reasoning ?? "").ConfigureAwait(false);

                if (!confirmed)
                {
                    DoctorDiag.Write("[Bootstrap] 用户拒绝修复，继续监控");
                    continue;
                }

                var diagnostic = reportsToProcess[0];
                var bootstrapLoop = new BootstrapLoop(
                    _sourceEngine, _worktreeMgr, _patchGenerator, _guard, _fs, _memory);

                var result = await bootstrapLoop.ExecuteAsync(diagnostic, ct: ct).ConfigureAwait(false);

                ReportResult(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[Bootstrap] 主循环异常: {ex.Message}");
            }
        }

        DoctorDiag.Write("[Bootstrap] 自举监控主循环退出");
    }

    /// <summary>
    /// LLM 判断: 这些诊断事件是否需要源码修复？
    /// </summary>
    internal async Task<BootstrapJudgment> LlmJudgeAsync(
        IReadOnlyList<DiagnosticReport> reports,
        CancellationToken ct)
    {
        var eventsSummary = string.Join("\n", reports.Select(r =>
            $"- {r.RuleId} ({r.Severity}): {r.Description}"));

        var prompt = $"""
            你是 jcc 自举监控引擎。以下是从 jcc 运行中收集的诊断事件:

            {eventsSummary}

            请分析:
            1. 这些事件是否指向 jcc 自身的源码缺陷（而非用户项目问题）？
            2. 如果是，哪个源码文件最可能有问题？
            3. 修复的优先级（high/medium/low）？
            4. 建议的修复方向？

            输出 JSON格式:
            needsFix: bool, targetFile: string?, priority: high|medium|low, reasoning: string
            """;

        try
        {
            var queryService = _chatClient.GetChatCompletionService();
            var messages = new MessageList();
            messages.AddSystemMessage(prompt);

            var response = await queryService.GetApiMessageContentsAsync(messages, cancellationToken: ct).ConfigureAwait(false);
            var content = response.FirstOrDefault()?.Content ?? "";

            return ParseJudgment(content);
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Bootstrap] LLM 判断失败: {ex.Message}");
            return new BootstrapJudgment { NeedsFix = false, Priority = "low", Reasoning = $"LLM 调用失败: {ex.Message}" };
        }
    }

    internal static BootstrapJudgment ParseJudgment(string llmResponse, ILogger? logger = null)
    {
        var result = LlmJsonHelper.Deserialize(llmResponse, AgentsJsonContext.Default.BootstrapJudgmentJson, out var repairHint, logger);
        if (result is null)
        {
            var reasoning = string.IsNullOrEmpty(repairHint)
                ? "LLM 输出 JSON 解析失败"
                : $"LLM 输出 JSON 解析失败: {repairHint}";
            return new BootstrapJudgment { NeedsFix = false, Priority = "low", Reasoning = reasoning };
        }

        return new BootstrapJudgment
        {
            NeedsFix = result.NeedsFix,
            TargetFile = result.TargetFile,
            Priority = result.Priority,
            Reasoning = result.Reasoning
        };
    }

    private static string FormatConfirmMessage(BootstrapJudgment judgment)
    {
        return $"[Bootstrap] 检测到自身缺陷:\n  目标文件: {judgment.TargetFile ?? "未知"}\n  优先级: {judgment.Priority}\n  LLM 推理: {judgment.Reasoning}\n\n是否执行修复？";
    }

    private static Task<bool> DefaultConfirmAsync(string message, string reasoning)
    {
        Console.WriteLine(message);
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("[Bootstrap] 非交互模式，自动确认修复");
            return Task.FromResult(true);
        }
        Console.Write("[Y/n] ");
        var input = Console.ReadLine();
        return Task.FromResult(string.IsNullOrEmpty(input) || input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static void ReportResult(BootstrapResult result)
    {
        if (result.Success)
        {
            DoctorDiag.Write($"[Bootstrap] 修复成功! 变更文件: {result.Patch?.TargetFilePath}");
        }
        else
        {
            DoctorDiag.WriteError($"[Bootstrap] 修复失败: {result.FailureReason}");
        }
    }

    private void OnDiagnosticEventReceived(object? sender, DiagnosticEvent evt)
    {
        _diagnosticEngine.Evaluate(evt);
    }

    private void OnLogWatcherEventDetected(object? sender, DiagnosticEvent evt)
    {
        _diagnosticEngine.Evaluate(evt);
        DoctorDiag.Write($"[Bootstrap] 日志文件检测到事件: {evt.EventType}");
    }

    private void OnDiagnosticReportGenerated(object? sender, DiagnosticReport report)
    {
        using var guard = _reportsLock.TryLock(TimeSpan.Zero);
        if (guard is not null)
        {
            _pendingReports.Add(report);
        }
        else
        {
            _pendingReports.Add(report);
        }
        DoctorDiag.Write($"[Bootstrap] 收到诊断报告: {report.RuleId} - {report.Description}");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _transport.EventReceived -= OnDiagnosticEventReceived;
        _diagnosticEngine.DiagnosticReportGenerated -= OnDiagnosticReportGenerated;

        if (_logWatcher is not null)
        {
            _logWatcher.EventDetected -= OnLogWatcherEventDetected;
            await _logWatcher.DisposeAsync().ConfigureAwait(false);
        }

        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// LLM 判断结果
/// </summary>
public sealed record BootstrapJudgment
{
    /// <summary>是否需要源码修复</summary>
    public required bool NeedsFix { get; init; }

    /// <summary>目标源码文件</summary>
    public string? TargetFile { get; init; }

    /// <summary>修复优先级</summary>
    public required string Priority { get; init; } = "low";

    /// <summary>LLM 推理过程</summary>
    public string? Reasoning { get; init; }
}
