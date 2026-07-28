
namespace Core.Scheduling;

/// <summary>
/// 工具移植计划运行器 - 用于执行和监控工具移植并行计划
/// </summary>
[Register]
public sealed partial class ToolPortingPlanRunner
{
    private readonly ParallelExecutionEngine _executionEngine;
    [Inject] private readonly ILogger<ToolPortingPlanRunner>? _logger;
    private readonly IClockService _clock;
    private readonly StringBuilder _executionLog = new();
    private readonly ConcurrentQueue<PlanExecutionRecord> _executionHistory = new();

    public ToolPortingPlanRunner(ParallelExecutionEngine executionEngine, ILogger<ToolPortingPlanRunner>? logger = null, IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(executionEngine);
        _executionEngine = executionEngine;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 运行完整的并行计划
    /// </summary>
    public async Task<ToolPortingExecutionResult> RunAsync(PlanOptions? options = null)
    {
        options ??= new PlanOptions();
        _executionLog.Clear();

        Log("=".PadRight(60, '='));
        Log(L.T(StringKey.PortingPlanStartLog));
        Log("=".PadRight(60, '='));
        Log(L.T(StringKey.StartTimeLog, _clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss")));
        Log(L.T(StringKey.FirstWaveTaskCount, 9));
        Log(L.T(StringKey.SecondWaveTaskCount, 3));
        Log(L.T(StringKey.SuggestedAgentCount, 17));
        Log("");

        // 创建执行选项
        var executionOptions = new ExecutionOptions
        {
            SimulatedWorkDurationMs = options.SimulatedMode ? 500 : 0,
            MaxConcurrentTasks = 12,
            VerboseLogging = options.Verbose
        };

        // 执行并行计划
        var result = await _executionEngine.ExecuteAsync(executionOptions).ConfigureAwait(false);

        // 生成报告
        Log("");
        Log("=".PadRight(60, '='));
        Log(L.T(StringKey.ExecutionReport));
        Log("=".PadRight(60, '='));

        var report = result.Report;
        Log(L.T(StringKey.TotalTaskCount, report.TotalTasks));
        Log(L.T(StringKey.CompletedCount, report.CompletedTasks.Count));
        Log(L.T(StringKey.FailedCount, report.FailedTasks.Count));
        Log(L.T(StringKey.PendingCount, report.PendingTasks.Count));
        Log(L.T(StringKey.CompletionRate, report.CompletionPercentage));
        Log(L.T(StringKey.ExecutionDuration, report.ExecutionDuration.TotalSeconds));

        if (report.FailedTasks.Count > 0)
        {
            Log("");
            Log(L.T(StringKey.FailedTaskList));
            foreach (var task in report.FailedTasks)
            {
                Log($"  - {task.Name}: {task.LastMessage}");
            }
        }

        Log("");
        Log(L.T(StringKey.TaskDetails));
        foreach (var detail in report.TaskDetails.OrderBy(d => d.TaskName))
        {
            var statusIcon = detail.Status switch
            {
                ScheduledTaskStatus.Completed => "✓",
                ScheduledTaskStatus.Failed => "✗",
                ScheduledTaskStatus.InProgress => "→",
                _ => "○"
            };
            Log($"  {L.T(StringKey.AgentCountLabel, $"{statusIcon} {detail.TaskName}", detail.RequiredAgents, detail.Status)}");
        }

        Log("");
        Log("=".PadRight(60, '='));
        Log(L.T(StringKey.ExecutionResultLabel, result.Success ? L.T(StringKey.SuccessLabel) : L.T(StringKey.FailedLabel)));
        Log("=".PadRight(60, '='));

        return new ToolPortingExecutionResult
        {
            Success = result.Success,
            Report = report,
            ExecutionLog = _executionLog.ToString()
        };
    }

    /// <summary>
    /// 执行计划的便捷方法
    /// </summary>
    public async Task<ToolPortingExecutionResult> ExecutePlanAsync(PlanOptions options)
    {
        var result = await RunAsync(options).ConfigureAwait(false);

        // 记录执行历史
        var record = new PlanExecutionRecord
        {
            ExecutedAt = _clock.GetUtcNow(),
            Options = options,
            Result = result
        };
        _executionHistory.Enqueue(record);

        // 限制历史记录数量
        while (_executionHistory.Count > WorkflowConstants.Limits.ExecutionHistoryMax)
        {
            _executionHistory.TryDequeue(out _);
        }

        return result;
    }

    /// <summary>
    /// 获取执行历史
    /// </summary>
    public IReadOnlyList<PlanExecutionRecord> GetExecutionHistory()
    {
        return _executionHistory.ToList();
    }

    /// <summary>
    /// 生成任务分配方案
    /// </summary>
    public TaskAssignmentPlan GenerateAssignmentPlan()
    {
        var scheduler = new ToolPortingScheduler();
        scheduler.InitializeTasks();

        var tasks = scheduler.GetAllTasks();
        var firstWave = scheduler.GetFirstWaveTasks();
        var secondWave = tasks.Where(t => t.Dependencies.Any()).ToList();

        var assignments = tasks.Select(t => new TaskAgentAssignment
        {
            TaskId = t.Id,
            TaskName = t.Name,
            Description = t.Description,
            RequiredAgents = t.RequiredAgents,
            Priority = t.Priority,
            Dependencies = t.Dependencies,
            IsFirstWave = !t.Dependencies.Any(),
            AgentWorkScopes = GetAgentWorkScopes(t)
        }).ToList();

        return new TaskAssignmentPlan
        {
            TotalTasks = tasks.Count,
            FirstWaveCount = firstWave.Count,
            SecondWaveCount = secondWave.Count,
            TotalAgentsRequired = tasks.Sum(t => t.RequiredAgents),
            Assignments = assignments,
            ExecutionOrder = GenerateExecutionOrder(tasks)
        };
    }

    /// <summary>
    /// 导出计划到 JSON
    /// </summary>
    public string ExportPlanToJson()
    {
        var plan = GenerateAssignmentPlan();
        return JsonSerializer.Serialize(plan, SchedulingIndentedTasksJsonContext.Default.TaskAssignmentPlan);
    }

    /// <summary>
    /// 导出计划到 Markdown
    /// </summary>
    public string ExportPlanToMarkdown()
    {
        var plan = GenerateAssignmentPlan();
        var sb = new StringBuilder();

        sb.AppendLine(L.T(StringKey.PortingPlanTitle));
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SectionOverview));
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.TotalTasksLabel, plan.TotalTasks));
        sb.AppendLine(L.T(StringKey.FirstWaveLabel, plan.FirstWaveCount));
        sb.AppendLine(L.T(StringKey.SecondWaveLabel, plan.SecondWaveCount));
        sb.AppendLine(L.T(StringKey.SuggestedAgentsLabel, plan.TotalAgentsRequired));
        sb.AppendLine();

        sb.AppendLine(L.T(StringKey.FirstWaveImmediateStart));
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.FirstWaveTableHeader));
        sb.AppendLine("|------|----------|--------|----------|");

        foreach (var assignment in plan.Assignments.Where(a => a.IsFirstWave).OrderByDescending(a => a.Priority))
        {
            var priority = assignment.Priority switch
            {
                TodoPriority.Critical => $"{PrioritySymbol.Critical.ToValue()} Critical",
                TodoPriority.High => $"{PrioritySymbol.High.ToValue()} High",
                TodoPriority.Medium => $"{PrioritySymbol.Medium.ToValue()} Medium",
                _ => $"{PrioritySymbol.Low.ToValue()} Low"
            };
            var workScope = string.Join(", ", assignment.AgentWorkScopes.Select((s, i) => L.T(StringKey.AgentScopeLabel, i, s)));
            sb.AppendLine($"| {assignment.TaskName} | {assignment.RequiredAgents} | {priority} | {workScope} |");
        }

        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SecondWaveConditionalTrigger));
        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SecondWaveTableHeader));
        sb.AppendLine("|------|----------|--------|----------|----------|");

        foreach (var assignment in plan.Assignments.Where(a => !a.IsFirstWave).OrderByDescending(a => a.Priority))
        {
            var priority = assignment.Priority switch
            {
                TodoPriority.Critical => $"{PrioritySymbol.Critical.ToValue()} Critical",
                TodoPriority.High => $"{PrioritySymbol.High.ToValue()} High",
                TodoPriority.Medium => $"{PrioritySymbol.Medium.ToValue()} Medium",
                _ => $"{PrioritySymbol.Low.ToValue()} Low"
            };
            var dependencies = string.Join(", ", assignment.Dependencies);
            var workScope = string.Join("; ", assignment.AgentWorkScopes.Select((s, i) => L.T(StringKey.AgentScopeLabel, i, s)));
            sb.AppendLine($"| {assignment.TaskName} | {assignment.RequiredAgents} | {priority} | {dependencies} | {workScope} |");
        }

        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SectionExecutionOrder));
        sb.AppendLine();
        sb.AppendLine("```");
        foreach (var phase in plan.ExecutionOrder)
        {
            sb.AppendLine(L.T(StringKey.PhaseLabel, phase.PhaseNumber, phase.Description));
            foreach (var taskName in phase.TaskNames)
            {
                sb.AppendLine($"  - {taskName}");
            }
        }
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SectionDependencyGraph));
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(L.T(StringKey.FirstWaveParallelStart));
        sb.AppendLine("├── Task-01-Agent-Core ──┬──→ Task-02-User-Interaction");
        sb.AppendLine("│                        └──→ Task-12-Skill-Messaging");
        sb.AppendLine($"├── Task-03-MCP-Interop {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine("├── Task-04-Task-Lifecycle ──→ Task-06-Todo-Search");
        sb.AppendLine($"├── Task-05-Web-Features {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine($"├── Task-07-Bash-Security {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine($"├── Task-08-PowerShell-Security {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine($"├── Task-09-LSP-Support {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine($"├── Task-10-Config-Cron {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine($"└── Task-11-File-Operations {L.T(StringKey.IndependentLabel)}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private void Log(string message)
    {
        _executionLog.AppendLine(message);
        _logger?.LogInformation(message);
    }

    private List<string> GetAgentWorkScopes(ScheduledTask task)
    {
        return task.Name switch
        {
            "Task-01-Agent-Core" => new List<string>
            {
                "AgentTool + agentColorManager + agentDisplay",
                "agentMemory + agentMemorySnapshot"
            },
            "Task-03-MCP-Interop" => new List<string>
            {
                "MCPTool + McpAuthTool",
                "ListMcpResourcesTool + ReadMcpResourceTool + classifyForCollapse"
            },
            "Task-04-Task-Lifecycle" => new List<string>
            {
                "TaskCreateTool + TaskGetTool + TaskListTool + TaskUpdateTool + TaskStopTool + TaskOutputTool"
            },
            "Task-05-Web-Features" => new List<string>
            {
                "WebSearchTool + WebFetchTool + preapproved + utils"
            },
            "Task-07-Bash-Security" => new List<string>
            {
                "bashPermissions + bashSecurity + destructiveCommandWarning + commandSemantics + pathValidation + readOnlyValidation + modeValidation + shouldUseSandbox"
            },
            "Task-08-PowerShell-Security" => new List<string>
            {
                "powershellPermissions + powershellSecurity + destructiveCommandWarning + commandSemantics + pathValidation + readOnlyValidation + modeValidation + gitSafety + clmTypes"
            },
            "Task-09-LSP-Support" => new List<string>
            {
                "LSPTool + formatters",
                "symbolContext + schemas"
            },
            "Task-10-Config-Cron" => new List<string>
            {
                "ConfigTool + supportedSettings + CronCreateTool + CronDeleteTool + CronListTool"
            },
            "Task-11-File-Operations" => new List<string>
            {
                "FileEditTool + types + utils + imageProcessor + limits"
            },
            "Task-02-User-Interaction" => new List<string>
            {
                "AskUserQuestionTool + EnterPlanModeTool + ExitPlanModeV2Tool",
                "planAgent + exploreAgent + verificationAgent + generalPurposeAgent + claudeCodeGuideAgent"
            },
            "Task-06-Todo-Search" => new List<string>
            {
                "TodoWriteTool + ToolSearchTool"
            },
            "Task-12-Skill-Messaging" => new List<string>
            {
                "SkillTool + SendMessageTool + spawnMultiAgent",
                "agentToolUtils + forkSubagent + resumeAgent + runAgent"
            },
            _ => new List<string> { L.T(StringKey.FullTaskScope) }
        };
    }

    private List<ExecutionPhase> GenerateExecutionOrder(List<ScheduledTask> tasks)
    {
        var phases = new List<ExecutionPhase>();

        // 第一波
        var firstWave = tasks.Where(t => !t.Dependencies.Any()).ToList();
        phases.Add(new ExecutionPhase
        {
            PhaseNumber = 1,
            Description = L.T(StringKey.FirstWaveDescription),
            TaskNames = firstWave.Select(t => t.Name).ToList()
        });

        // 第二波
        var secondWave = tasks.Where(t => t.Dependencies.Any()).ToList();
        phases.Add(new ExecutionPhase
        {
            PhaseNumber = 2,
            Description = L.T(StringKey.SecondWaveDescription),
            TaskNames = secondWave.Select(t => t.Name).ToList()
        });

        return phases;
    }
}

/// <summary>
/// 计划选项
/// </summary>
public sealed partial class PlanOptions
{
    /// <summary>
    /// 是否使用模拟模式（用于测试）
    /// </summary>
    public bool SimulatedMode { get; init; } = true;

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool Verbose { get; init; } = true;
}

/// <summary>
/// 工具移植计划执行结果
/// </summary>
public sealed partial class ToolPortingExecutionResult
{
    public required bool Success { get; init; }
    public required ExecutionReport Report { get; init; }
    public required string ExecutionLog { get; init; }
}

/// <summary>
/// 任务分配计划
/// </summary>
public sealed partial class TaskAssignmentPlan
{
    public int TotalTasks { get; init; }
    public int FirstWaveCount { get; init; }
    public int SecondWaveCount { get; init; }
    public int TotalAgentsRequired { get; init; }
    public List<TaskAgentAssignment> Assignments { get; init; } = new();
    public List<ExecutionPhase> ExecutionOrder { get; init; } = new();
}

/// <summary>
/// 任务智能体分配
/// </summary>
public sealed partial class TaskAgentAssignment
{
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public required string Description { get; init; }
    public required int RequiredAgents { get; init; }
    public required TodoPriority Priority { get; init; }
    public required List<string> Dependencies { get; init; }
    public required bool IsFirstWave { get; init; }
    public required List<string> AgentWorkScopes { get; init; }
}

/// <summary>
/// 执行阶段
/// </summary>
public sealed partial class ExecutionPhase
{
    public required int PhaseNumber { get; init; }
    public required string Description { get; init; }
    public required List<string> TaskNames { get; init; }
}

/// <summary>
/// 计划执行记录 - 用于跟踪执行历史
/// </summary>
public sealed partial class PlanExecutionRecord
{
    /// <summary>
    /// 执行时间
    /// </summary>
    public required DateTime ExecutedAt { get; init; }

    /// <summary>
    /// 执行选项
    /// </summary>
    public required PlanOptions Options { get; init; }

    /// <summary>
    /// 执行结果
    /// </summary>
    public required ToolPortingExecutionResult Result { get; init; }
}
