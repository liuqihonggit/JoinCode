
namespace Core.Goal;

using JoinCode.Abstractions.Models.Build;
using JoinCode.Abstractions.Security.Scanning;
using Structura.Dag;

[Register]
public sealed partial class LeadMergeOrchestrator : ILeadMergeOrchestrator
{
    private readonly IWorktreeMergeService _worktreeMerge;
    private readonly IBuildQueueService _buildQueue;
    private readonly IGitDiffProvider? _diffProvider;
    [Inject] private readonly ILogger<LeadMergeOrchestrator>? _logger;

    public LeadMergeOrchestrator(
        IWorktreeMergeService worktreeMerge,
        IBuildQueueService buildQueue,
        ILogger<LeadMergeOrchestrator>? logger = null,
        IGitDiffProvider? diffProvider = null)
    {
        _worktreeMerge = worktreeMerge;
        _buildQueue = buildQueue;
        _logger = logger;
        _diffProvider = diffProvider;
    }

    public async Task<LeadMergeResult> MergeCompletedWorkersAsync(LeadMergeContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var completedMap = context.CompletedWorkers
            .Where(w => w.IsSuccess && w.GradingScore >= 0.6)
            .ToDictionary(w => w.SubTaskId, w => w);

        if (completedMap.Count == 0)
        {
            return LeadMergeResult.Failed("没有可合并的 Worker（全部失败或评分过低）", []);
        }

        var mergeOrder = ComputeMergeOrder(context.Plan, completedMap.Keys.ToHashSet());
        if (mergeOrder is null)
        {
            return LeadMergeResult.Failed("无法计算合并顺序（依赖关系异常）", []);
        }

        var preCheckWarnings = await PreCheckFileConflictsAsync(completedMap, ct).ConfigureAwait(false);
        foreach (var warning in preCheckWarnings)
        {
            _logger?.LogWarning("冲突预检告警: {Warning}", warning);
        }

        var steps = new List<MergeStepResult>();
        var mergedIds = new HashSet<string>();
        var failedIds = new HashSet<string>();

        foreach (var subTaskId in mergeOrder)
        {
            if (!completedMap.TryGetValue(subTaskId, out var worker))
            {
                continue;
            }

            var deps = context.Plan.Decomposition.SubTasks
                .FirstOrDefault(t => t.Id == subTaskId)?.DependsOn ?? [];
            if (deps.Any(d => failedIds.Contains(d)))
            {
                _logger?.LogWarning("Worker {SubTaskId} 跳过: 依赖的 Worker 合并失败", subTaskId);
                steps.Add(new MergeStepResult
                {
                    SubTaskId = subTaskId,
                    Merged = false,
                    TestsPassed = false,
                    Message = "跳过: 依赖的 Worker 合并失败"
                });
                failedIds.Add(subTaskId);
                continue;
            }

            var step = await MergeSingleWorkerAsync(worker, context, ct).ConfigureAwait(false);
            steps.Add(step);

            if (step.Merged)
            {
                mergedIds.Add(subTaskId);
            }
            else
            {
                failedIds.Add(subTaskId);
                _logger?.LogWarning("Worker {SubTaskId} 合并失败", subTaskId);
            }
        }

        var allMerged = mergedIds.Count == completedMap.Count;
        if (allMerged)
        {
            return LeadMergeResult.Succeeded(steps);
        }

        return steps.Any(s => s.Merged)
            ? LeadMergeResult.PartiallySucceeded(steps, $"部分合并成功: {mergedIds.Count}/{completedMap.Count}")
            : LeadMergeResult.Failed("合并失败", steps);
    }

    private async Task<MergeStepResult> MergeSingleWorkerAsync(WorkerCompletion worker, LeadMergeContext context, CancellationToken ct)
    {
        try
        {
            var mergeResult = await _worktreeMerge.MergeToTargetAsync(
                worker.WorktreePath,
                context.MainBranch,
                WorktreeMergeStrategy.AutoMerge,
                ct).ConfigureAwait(false);

            if (!mergeResult.IsSuccess)
            {
                return new MergeStepResult
                {
                    SubTaskId = worker.SubTaskId,
                    Merged = false,
                    TestsPassed = false,
                    MergeStrategy = mergeResult.StrategyUsed,
                    Message = mergeResult.Error ?? "合并失败"
                };
            }

            var testPassed = await RunPostMergeTestsAsync(context, ct).ConfigureAwait(false);

            return new MergeStepResult
            {
                SubTaskId = worker.SubTaskId,
                Merged = true,
                TestsPassed = testPassed,
                MergeStrategy = mergeResult.StrategyUsed,
                Message = testPassed ? "合并+测试通过" : "合并成功但测试失败"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Worker {SubTaskId} 合并异常", worker.SubTaskId);
            return new MergeStepResult
            {
                SubTaskId = worker.SubTaskId,
                Merged = false,
                TestsPassed = false,
                Message = $"合并异常: {ex.Message}"
            };
        }
    }

    private async Task<bool> RunPostMergeTestsAsync(LeadMergeContext context, CancellationToken ct)
    {
        try
        {
            var request = new BuildRequest
            {
                Command = "dotnet build --verbosity quiet --no-restore",
                WorkingDirectory = context.WorkingDirectory,
            };

            var buildId = await _buildQueue.SubmitAsync(request, ct).ConfigureAwait(false);
            var result = await _buildQueue.WaitAsync(buildId, ct).ConfigureAwait(false);

            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Post-merge build verification failed");
            return false;
        }
    }

    internal static List<string> PreCheckFileConflictsFromDiffs(Dictionary<string, IReadOnlyList<string>> workerDiffs)
    {
        var warnings = new List<string>();
        var fileToWorkers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (workerId, files) in workerDiffs)
        {
            foreach (var file in files)
            {
                if (!fileToWorkers.TryGetValue(file, out var workerList))
                {
                    workerList = [];
                    fileToWorkers[file] = workerList;
                }

                workerList.Add(workerId);
            }
        }

        foreach (var (file, workers) in fileToWorkers)
        {
            if (workers.Count >= 2)
            {
                warnings.Add($"文件 '{file}' 被 {workers.Count} 个 Worker 修改: {string.Join(", ", workers)}");
            }
        }

        return warnings;
    }

    private async Task<List<string>> PreCheckFileConflictsAsync(Dictionary<string, WorkerCompletion> completedMap, CancellationToken ct)
    {
        if (_diffProvider is null)
        {
            return [];
        }

        var workerDiffs = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (subTaskId, worker) in completedMap)
        {
            try
            {
                var files = await _diffProvider.GetStagedFileNamesAsync(worker.WorktreePath, ct).ConfigureAwait(false);
                workerDiffs[subTaskId] = files;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Worker {SubTaskId} diff预检失败", subTaskId);
            }
        }

        return PreCheckFileConflictsFromDiffs(workerDiffs);
    }

    internal static List<string>? ComputeMergeOrder(ClusterPlan plan, HashSet<string> availableIds)
    {
        var dag = new Dag<string>();
        foreach (var id in availableIds)
        {
            dag.AddNode(new DagNode<string> { Id = id, Payload = id });
        }

        foreach (var task in plan.Decomposition.SubTasks)
        {
            if (!availableIds.Contains(task.Id))
            {
                continue;
            }

            foreach (var depId in task.DependsOn)
            {
                if (availableIds.Contains(depId))
                {
                    var edgeResult = dag.AddEdge(new DagEdge { FromId = depId, ToId = task.Id, Label = "depends-on" });
                    if (edgeResult.CyclePath is not null)
                    {
                        return null;
                    }
                }
            }
        }

        return dag.TopologicalSort().Select(n => n.Id).ToList();
    }
}
