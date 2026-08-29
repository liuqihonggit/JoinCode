namespace Core.Goal;


[Register(typeof(IClusterPlanValidator), ServiceLifetime.Singleton)]
public sealed partial class ClusterPlanValidator : ServiceEntity, IClusterPlanValidator
{
    private const int MaxSubTasks = 8;
    private const int MaxFileOverlap = 2;

    public ClusterPlanValidationResult Validate(ClusterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        var warnings = new List<string>();
        var conflicts = new List<FileConflictInfo>();

        if (!plan.Decomposition.IsDecomposable)
        {
            errors.Add("计划标记为不可分解，无法执行集群模式");
            return ClusterPlanValidationResult.Invalid(errors, warnings, conflicts);
        }

        var subTasks = plan.Decomposition.SubTasks;

        if (subTasks.Count == 0)
        {
            errors.Add("可分解计划没有子任务");
            return ClusterPlanValidationResult.Invalid(errors, warnings, conflicts);
        }

        if (subTasks.Count > MaxSubTasks)
        {
            errors.Add($"子任务数量 {subTasks.Count} 超过最大限制 {MaxSubTasks}");
        }

        ValidateIds(subTasks, errors);
        ValidateDependencies(subTasks, errors);
        ValidateFileOwnership(subTasks, conflicts, warnings);
        ValidatePriority(subTasks, warnings);
        ValidateComplexityConsistency(plan.Decomposition.Complexity, subTasks.Count, warnings);

        if (errors.Count > 0)
        {
            return ClusterPlanValidationResult.Invalid(errors, warnings, conflicts);
        }

        return ClusterPlanValidationResult.Valid(warnings, conflicts);
    }

    private static void ValidateIds(IReadOnlyList<SubTaskDefinition> subTasks, List<string> errors)
    {
        var ids = new HashSet<string>();
        var duplicates = new List<string>();

        foreach (var task in subTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                errors.Add("子任务 ID 不能为空");
                continue;
            }

            if (!ids.Add(task.Id))
            {
                duplicates.Add(task.Id);
            }
        }

        if (duplicates.Count > 0)
        {
            errors.Add($"子任务 ID 重复: {string.Join(", ", duplicates)}");
        }
    }

    private static void ValidateDependencies(IReadOnlyList<SubTaskDefinition> subTasks, List<string> errors)
    {
        var idSet = subTasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var task in subTasks)
        {
            foreach (var depId in task.DependsOn)
            {
                if (!idSet.Contains(depId))
                {
                    errors.Add($"子任务 '{task.Id}' 依赖不存在的子任务 '{depId}'");
                }
            }

            var depSet = new HashSet<string>(task.DependsOn, StringComparer.Ordinal);
            if (depSet.Contains(task.Id))
            {
                errors.Add($"子任务 '{task.Id}' 不能依赖自身");
            }
        }

        var cycleResult = DetectCycle(subTasks);
        if (cycleResult is not null)
        {
            errors.Add($"依赖关系存在环: {string.Join(" → ", cycleResult)}");
        }
    }

    private static List<string>? DetectCycle(IReadOnlyList<SubTaskDefinition> subTasks)
    {
        var dag = new Dag<SubTaskDefinition>();
        foreach (var task in subTasks)
        {
            dag.AddNode(new DagNode<SubTaskDefinition> { Id = task.Id, Payload = task });
        }

        foreach (var task in subTasks)
        {
            foreach (var depId in task.DependsOn)
            {
                var edgeResult = dag.AddEdge(new DagEdge { FromId = depId, ToId = task.Id, Label = "depends-on" });
                if (edgeResult.CyclePath.Count > 0)
                {
                    return edgeResult.CyclePath.ToList();
                }
            }
        }

        return null;
    }

    private static void ValidateFileOwnership(IReadOnlyList<SubTaskDefinition> subTasks, List<FileConflictInfo> conflicts, List<string> warnings)
    {
        var fileToTasks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in subTasks)
        {
            foreach (var file in task.OwnedFiles)
            {
                if (!fileToTasks.TryGetValue(file, out var taskList))
                {
                    taskList = [];
                    fileToTasks[file] = taskList;
                }

                taskList.Add(task.Id);
            }
        }

        foreach (var (file, taskIds) in fileToTasks)
        {
            if (taskIds.Count >= MaxFileOverlap)
            {
                conflicts.Add(new FileConflictInfo { FilePath = file, SubTaskIds = taskIds });
                warnings.Add($"文件 '{file}' 被 {taskIds.Count} 个子任务共享: {string.Join(", ", taskIds)}");
            }
        }
    }

    private static void ValidatePriority(IReadOnlyList<SubTaskDefinition> subTasks, List<string> warnings)
    {
        foreach (var task in subTasks)
        {
            if (!Enum.IsDefined(task.Priority))
            {
                warnings.Add($"子任务 '{task.Id}' 的优先级 '{task.Priority}' 无效");
            }
        }
    }

    private static void ValidateComplexityConsistency(ComplexityLevel complexity, int subTaskCount, List<string> warnings)
    {
        switch (complexity)
        {
            case ComplexityLevel.Low when subTaskCount > 5:
                warnings.Add($"complexity_mismatch: Low 档次子任务数应≤5，实际 {subTaskCount}，建议升级为 Medium");
                break;
            case ComplexityLevel.Medium when subTaskCount <= 5:
                warnings.Add($"complexity_mismatch: Medium 档次子任务数应 6-20，实际 {subTaskCount}，建议降级为 Low");
                break;
            case ComplexityLevel.Medium when subTaskCount > 20:
                warnings.Add($"complexity_mismatch: Medium 档次子任务数应 6-20，实际 {subTaskCount}，建议升级为 High");
                break;
            case ComplexityLevel.High when subTaskCount <= 20:
                warnings.Add($"complexity_mismatch: High 档次子任务数应>20，实际 {subTaskCount}，建议降级为 Medium");
                break;
        }
    }
}
