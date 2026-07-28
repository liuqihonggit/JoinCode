
namespace Core.Scheduling;

/// <summary>
/// 任务执行器 - 负责执行单个任务
/// </summary>
internal sealed class TaskExecutor
{
    private readonly ISubAgentCoordinator? _agentCoordinator;
    private readonly ToolPortingScheduler _scheduler;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<string, AgentExecutionRecord> _executionRecords;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;

    public TaskExecutor(
        ISubAgentCoordinator? agentCoordinator,
        ToolPortingScheduler scheduler,
        ILogger? logger,
        CancellationTokenSource cts,
        ConcurrentDictionary<string, AgentExecutionRecord> executionRecords,
        ISubAgentContextAccessor subAgentContextAccessor,
        IClockService? clock = null)
    {
        _agentCoordinator = agentCoordinator;
        _scheduler = scheduler;
        _logger = logger;
        _cts = cts;
        _executionRecords = executionRecords;
        _subAgentContextAccessor = subAgentContextAccessor;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 使用信号量控制并发执行任务
    /// </summary>
    public async Task ExecuteWithSemaphoreAsync(ScheduledTask task, ExecutionContext context, CancellationToken cancellationToken = default)
    {
        await context.ConcurrencyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(task, context.Options).ConfigureAwait(false);
        }
        finally
        {
            context.ConcurrencyLock.Release();
        }
    }

    /// <summary>
    /// 执行单个任务
    /// </summary>
    private async Task ExecuteAsync(ScheduledTask task, ExecutionOptions options)
    {
        _logger?.LogInformation(L.T(StringKey.StartTaskLog, task.Name, task.RequiredAgents));
        _scheduler.StartTask(task.Id);

        try
        {
            if (_agentCoordinator != null)
            {
                await ExecuteWithAgentsAsync(task, options).ConfigureAwait(false);
            }
            else
            {
                await ExecuteInSimulationModeAsync(task, options).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _scheduler.FailTask(task.Id, L.T(StringKey.TaskCancelledMsg));
            _logger?.LogWarning(L.T(StringKey.TaskCancelledLog, task.Name));
            throw;
        }
        catch (Exception ex)
        {
            _scheduler.FailTask(task.Id, ex.Message);
            _logger?.LogError(ex, L.T(StringKey.TaskExecErrorLog, task.Name));
        }
    }

    /// <summary>
    /// 使用 AgentCoordinator 执行任务
    /// </summary>
    private async Task ExecuteWithAgentsAsync(ScheduledTask task, ExecutionOptions options)
    {
        if (_agentCoordinator == null)
        {
            throw new InvalidOperationException(L.T(StringKey.AgentCoordinatorNotInit));
        }

        var startTime = _clock.GetUtcNow();
        var taskContext = CreateTaskContext(task);
        var agentTasks = new List<string>();

        try
        {
            var subAgents = await CreateSubAgentsAsync(task, taskContext, options).ConfigureAwait(false);
            agentTasks.AddRange(subAgents.Select(a => a.Id));

            LogAgentCreation(task.Name, subAgents, task.RequiredAgents);

            var results = await ExecuteAgentsParallelAsync(subAgents, options).ConfigureAwait(false);
            var executionLog = ProcessResults(task, results, startTime);

            HandleExecutionOutcome(task, results, executionLog);

            await CleanupAgentsAsync(subAgents.Select(a => a.Id)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await CleanupAgentsAsync(agentTasks).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 创建任务上下文
    /// </summary>
    private AgentTaskContext CreateTaskContext(ScheduledTask task) => new()
    {
        TaskId = task.Id,
        AgentIndex = 0,
        TotalAgents = task.RequiredAgents,
        WorkScope = task.Description,
        TaskName = task.Name,
        Description = task.Description,
        Priority = (int)task.Priority,
        CancellationToken = _cts.Token
    };

    /// <summary>
    /// 创建子Agent集合
    /// </summary>
    private async Task<IReadOnlyList<ISubAgent>> CreateSubAgentsAsync(ScheduledTask task, AgentTaskContext taskContext, ExecutionOptions options)
    {
        if (_agentCoordinator == null) return Array.Empty<ISubAgent>();

        var subAgents = new List<ISubAgent>();
        for (int i = 0; i < task.RequiredAgents; i++)
        {
            var agent = await CreateSingleSubAgentAsync(task, taskContext, options, i).ConfigureAwait(false);
            subAgents.Add(agent);
        }
        return subAgents;
    }

    /// <summary>
    /// 创建单个SubAgent
    /// </summary>
    private async Task<ISubAgent> CreateSingleSubAgentAsync(ScheduledTask task, AgentTaskContext taskContext, ExecutionOptions options, int agentIndex)
    {
        var description = BuildAgentTaskDescription(task, agentIndex, task.RequiredAgents);
        var subAgentOptions = BuildSubAgentOptions(task, taskContext, options, agentIndex);
        var coordinator = _agentCoordinator ?? throw new InvalidOperationException("Agent coordinator not available.");
        return await coordinator.SpawnSubAgentAsync(description, subAgentOptions, _cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建SubAgent选项
    /// </summary>
    private SubAgentOptions BuildSubAgentOptions(ScheduledTask task, AgentTaskContext taskContext, ExecutionOptions options, int agentIndex)
    {
        return new SubAgentOptions
        {
            AdditionalInstructions = L.T(StringKey.AgentTaskInstructions, agentIndex + 1, task.RequiredAgents, task.Name, taskContext.Description),
            MaxIterations = 50,
            EnableThinking = options.VerboseLogging,
            ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
            SessionId = _subAgentContextAccessor.Current?.SessionId ?? "default",
        };
    }

    /// <summary>
    /// 并行执行所有Agent
    /// </summary>
    private async Task<IReadOnlyList<SubAgentResult>> ExecuteAgentsParallelAsync(IReadOnlyList<ISubAgent> subAgents, ExecutionOptions options)
    {
        if (_agentCoordinator == null) return Array.Empty<SubAgentResult>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(subAgents.Count, CpuParallelism.GetDegree(options.MaxConcurrentTasks)),
            CancellationToken = _cts.Token
        };

        return await _agentCoordinator.ExecuteParallelAsync(subAgents, parallelOptions, _cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 记录Agent创建日志
    /// </summary>
    private void LogAgentCreation(string taskName, IReadOnlyList<ISubAgent> subAgents, int totalAgents)
    {
        for (int i = 0; i < subAgents.Count; i++)
        {
            _logger?.LogInformation(L.T(StringKey.CreateSubAgentLog, taskName, subAgents[i].Id, i + 1, totalAgents));
        }
    }

    /// <summary>
    /// 处理执行结果
    /// </summary>
    private void HandleExecutionOutcome(ScheduledTask task, IReadOnlyList<SubAgentResult> results, string executionLog)
    {
        var allSuccess = results.All(r => r.IsSuccess);
        var anySuccess = results.Any(r => r.IsSuccess);

        if (allSuccess)
        {
            _scheduler.CompleteTask(task.Id, executionLog);
            _logger?.LogInformation(L.T(StringKey.TaskAllAgentsSuccessLog, task.Name, results.Count));
        }
        else if (anySuccess)
        {
            var successCount = results.Count(r => r.IsSuccess);
            _scheduler.CompleteTask(task.Id, executionLog);
            _logger?.LogWarning(L.T(StringKey.TaskPartialSuccessLog, task.Name, successCount, results.Count));
        }
        else
        {
            var errorMessage = L.T(StringKey.AllAgentsFailedMsg, string.Join("; ", results.Where(r => !r.IsSuccess).Select(r => r.Error)));
            _scheduler.FailTask(task.Id, errorMessage);
            _logger?.LogError(L.T(StringKey.TaskFailedLog, task.Name, errorMessage));
        }
    }

    /// <summary>
    /// 清理Agent资源（并行执行）
    /// </summary>
    private async Task CleanupAgentsAsync(IEnumerable<string> agentIds)
    {
        if (_agentCoordinator == null) return;

        var cleanupTasks = agentIds.Select(async agentId =>
        {
            try
            {
                await _agentCoordinator.DisposeAgentAsync(agentId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, L.T(StringKey.CleanupAgentFailedLog, agentId));
            }
        });

        await Task.WhenAll(cleanupTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 模拟模式下执行任务
    /// </summary>
    private async Task ExecuteInSimulationModeAsync(ScheduledTask task, ExecutionOptions options)
    {
        _logger?.LogInformation(L.T(StringKey.SimModeExecLog, task.Name));
        await Task.Delay(options.SimulatedWorkDurationMs, _cts.Token).ConfigureAwait(false);
        _scheduler.CompleteTask(task.Id, L.T(StringKey.SimModeCompleteLog, task.RequiredAgents));
        _logger?.LogInformation(L.T(StringKey.SimModeExecLog, task.Name));
    }

    /// <summary>
    /// 构建 Agent 任务描述
    /// </summary>
    private static string BuildAgentTaskDescription(ScheduledTask task, int agentIndex, int totalAgents)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T(StringKey.ExecTaskLabel, task.Name));
        sb.AppendLine(L.T(StringKey.TaskDescLabel, task.Description));
        sb.AppendLine(L.T(StringKey.AgentIndexLabel, agentIndex + 1, totalAgents));
        sb.AppendLine(L.T(StringKey.PriorityLabel, task.Priority));

        if (task.Dependencies != null && task.Dependencies.Any())
        {
            sb.AppendLine(L.T(StringKey.DepsLabel, string.Join(", ", task.Dependencies)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 处理 Agent 执行结果并生成执行日志
    /// </summary>
    private string ProcessResults(ScheduledTask task, IReadOnlyList<SubAgentResult> results, DateTime startTime)
    {
        var sb = new StringBuilder();
        var endTime = _clock.GetUtcNow();
        var totalDuration = endTime - startTime;

        sb.AppendLine(L.T(StringKey.TaskExecReportTitle, task.Name));
        sb.AppendLine(L.T(StringKey.ExecTimeLabel, startTime.ToString("yyyy-MM-dd HH:mm:ss"), endTime.ToString("yyyy-MM-dd HH:mm:ss")));
        sb.AppendLine(L.T(StringKey.TotalDurationLabel, totalDuration.TotalSeconds));
        sb.AppendLine(L.T(StringKey.AgentCountLabel2, results.Count));
        sb.AppendLine(L.T(StringKey.SuccessFailCount, results.Count(r => r.IsSuccess), results.Count(r => !r.IsSuccess)));
        sb.AppendLine();

        var record = new AgentExecutionRecord
        {
            TaskId = task.Id,
            TaskName = task.Name,
            StartTime = startTime,
            EndTime = endTime,
            TotalDuration = totalDuration,
            AgentResults = results.ToList()
        };
        _executionRecords[task.Id] = record;

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine(L.T(StringKey.AgentResultHeader, i + 1, result.AgentId));
            sb.AppendLine($"Status: {(result.IsSuccess ? L.T(StringKey.StatusSuccess) : L.T(StringKey.StatusFailed))}");

            if (result.ExecutionTimeMs.HasValue)
            {
                sb.AppendLine(L.T(StringKey.ExecDurationLabel, result.ExecutionTimeMs.Value));
            }

            if (!string.IsNullOrEmpty(result.Output))
            {
                var output = result.Output.Length > WorkflowConstants.Limits.OutputTruncateLength
                    ? string.Concat(result.Output.AsSpan(0, WorkflowConstants.Limits.OutputTruncateLength), "...")
                    : result.Output;
                sb.AppendLine(L.T(StringKey.OutputLabel, output));
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                sb.AppendLine(L.T(StringKey.ErrorLabel, result.Error));
            }

            sb.AppendLine();
        }

        var mergedOutput = MergeAgentOutputs(results);
        if (!string.IsNullOrEmpty(mergedOutput))
        {
            sb.AppendLine(L.T(StringKey.MergedOutputTitle));
            sb.AppendLine(mergedOutput);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 合并多个 Agent 的输出
    /// </summary>
    private static string MergeAgentOutputs(IReadOnlyList<SubAgentResult> results)
    {
        if (results.Count == 0)
        {
            return string.Empty;
        }

        if (results.Count == 1)
        {
            return results[0].Output ?? string.Empty;
        }

        var successfulOutputs = results
            .Where(r => r.IsSuccess && !string.IsNullOrEmpty(r.Output))
            .Select(r => r.Output ?? string.Empty)
            .ToList();

        if (successfulOutputs.Count == 0)
        {
            return string.Empty;
        }

        if (successfulOutputs.Count == 1)
        {
            return successfulOutputs[0];
        }

        var sb = new StringBuilder();
        sb.AppendLine(L.T(StringKey.MultiAgentResultTitle));
        sb.AppendLine();

        for (int i = 0; i < successfulOutputs.Count; i++)
        {
            sb.AppendLine(L.T(StringKey.AgentContributionHeader, i + 1));
            sb.AppendLine(successfulOutputs[i]);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
