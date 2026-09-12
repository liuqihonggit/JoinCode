namespace JccAuditCli;

using Structura.Dag;

/// <summary>
/// 层依赖审计器：检测七层架构（Generators→Foundation→Infrastructure→Core→Services→Composition→App）
/// 之间的非法依赖（反向依赖、跨层引用、循环依赖）
/// </summary>
public static class LayerDependencyAuditor
{
    /// <summary>
    /// 七层定义（与 AGENTS.md 编译顺序一致，索引越小越底层）
    /// </summary>
    public static readonly string[] OrderedLayers =
        ["Generators", "Foundation", "Infrastructure", "Core", "Services", "Composition", "App"];

    /// <summary>
    /// 从已加载的 Roslyn 项目列表构建层依赖图，检测违规
    /// </summary>
    public static List<LayerViolationInfo> Audit(IReadOnlyList<Project> projects)
    {
        var projectToLayer = BuildProjectLayerMapping(projects);
        var dag = new Dag<string>();
        foreach (var layer in OrderedLayers)
            dag.AddNode(new DagNode<string> { Id = layer, Payload = layer });

        var violations = new List<LayerViolationInfo>();
        var solution = projects.Count > 0 ? projects[0].Solution : null;

        foreach (var project in projects)
        {
            if (project.FilePath is null) continue;
            var fromLayer = InferLayerFromPath(project.FilePath);
            if (fromLayer is null) continue;

            foreach (var projRef in project.ProjectReferences)
            {
                if (solution is null) continue;
                var refProject = solution.GetProject(projRef.ProjectId);
                if (refProject?.FilePath is null) continue;
                var toLayer = InferLayerFromPath(refProject.FilePath);
                if (toLayer is null) continue;

                var fromIdx = Array.IndexOf(OrderedLayers, fromLayer);
                var toIdx = Array.IndexOf(OrderedLayers, toLayer);

                // 反向依赖：上层引用了下层之下的层（如 Core 引用 Services）
                if (toIdx > fromIdx)
                {
                    violations.Add(new LayerViolationInfo
                    {
                        RuleId = "JCC9201",
                        Severity = "Error",
                        FromLayer = fromLayer,
                        ToLayer = toLayer,
                        FromProject = project.Name,
                        ToProject = refProject.Name,
                        Message = $"反向层依赖: {fromLayer}({project.Name}) → {toLayer}({refProject.Name})。七层架构要求依赖只能从上层指向下层。"
                    });
                }

                // 记录边到 DAG（用于环检测，允许环）
                dag.TryAddEdge(new DagEdge
                {
                    FromId = fromLayer,
                    ToId = toLayer,
                    Label = $"{project.Name}->{refProject.Name}"
                });
            }
        }

        // 层间循环依赖检测
        var cycles = dag.FindAllCycles();
        foreach (var cycle in cycles)
        {
            violations.Add(new LayerViolationInfo
            {
                RuleId = "JCC9202",
                Severity = "Error",
                FromLayer = cycle.Count > 0 ? cycle[0] : string.Empty,
                ToLayer = string.Join(" → ", cycle),
                FromProject = string.Empty,
                ToProject = string.Empty,
                Message = $"层循环依赖: {string.Join(" → ", cycle)}。违反七层隔离架构。"
            });
        }

        return violations;
    }

    /// <summary>
    /// 根据项目文件路径推断所属层
    /// </summary>
    public static string? InferLayerFromPath(string path)
    {
        foreach (var layer in OrderedLayers)
        {
            var lower = layer.ToLowerInvariant();
            if (path.Contains($"\\{lower}\\", StringComparison.OrdinalIgnoreCase) ||
                path.Contains($"/{lower}/", StringComparison.OrdinalIgnoreCase))
                return layer;
        }
        return null;
    }

    private static Dictionary<ProjectId, string> BuildProjectLayerMapping(IReadOnlyList<Project> projects)
    {
        var map = new Dictionary<ProjectId, string>();
        foreach (var p in projects)
        {
            if (p.FilePath is null) continue;
            var layer = InferLayerFromPath(p.FilePath);
            if (layer is not null)
                map[p.Id] = layer;
        }
        return map;
    }
}

/// <summary>
/// 层依赖违规信息
/// </summary>
public sealed record LayerViolationInfo
{
    public string RuleId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string FromLayer { get; init; } = string.Empty;
    public string ToLayer { get; init; } = string.Empty;
    public string FromProject { get; init; } = string.Empty;
    public string ToProject { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 层依赖审计报告
/// </summary>
public sealed record LayerAuditReport
{
    public string TargetPath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int TotalProjects { get; init; }
    public int TotalViolations { get; init; }
    public int ErrorCount { get; init; }
    public List<LayerViolationInfo> Violations { get; init; } = [];
}
