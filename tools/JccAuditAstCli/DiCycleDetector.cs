namespace JccAuditCli;

using Structura.Dag;

/// <summary>
/// DI 循环依赖检测器：构建全局有向图，检测循环
/// </summary>
public static class DiCycleDetector
{
    /// <summary>
    /// 从所有项目的注册和依赖信息中构建全局依赖图，检测所有循环
    /// </summary>
    public static List<DiCycleInfo> DetectCycles(
        IReadOnlyList<ServiceRegistration> registrations,
        IReadOnlyList<ConstructorDependency> constructorDeps,
        Dictionary<string, string> serviceToImplMapping)
    {
        var dag = new Dag<string>();

        foreach (var reg in registrations)
        {
            EnsureNode(dag, reg.ServiceType);
            EnsureNode(dag, reg.ImplementationType);
            dag.TryAddEdge(new DagEdge { FromId = reg.ServiceType, ToId = reg.ImplementationType, Label = "REGISTER" });
        }

        foreach (var dep in constructorDeps)
        {
            if (dep.IsOptional)
                continue;

            EnsureNode(dag, dep.ClassName);
            EnsureNode(dag, dep.DependencyType);
            dag.TryAddEdge(new DagEdge { FromId = dep.ClassName, ToId = dep.DependencyType, Label = "INJECT" });
        }

        foreach (var reg in registrations)
        {
            if (!string.IsNullOrEmpty(reg.ServiceType) &&
                reg.ServiceType != reg.ImplementationType &&
                reg.ServiceType.StartsWith("I", StringComparison.Ordinal))
            {
                if (!serviceToImplMapping.ContainsKey(reg.ServiceType))
                    serviceToImplMapping[reg.ServiceType] = reg.ImplementationType;
            }
        }

        var edgeReplacements = new List<(string EdgeId, string NewTo)>();
        foreach (var edge in dag.Edges.Values)
        {
            if (serviceToImplMapping.TryGetValue(edge.ToId, out var impl) && impl != edge.ToId)
            {
                edgeReplacements.Add((edge.Id, impl));
            }
        }
        foreach (var (edgeId, newTo) in edgeReplacements)
        {
            var oldEdge = dag.Edges[edgeId];
            dag.RemoveEdge(edgeId);
            EnsureNode(dag, newTo);
            dag.TryAddEdge(new DagEdge { FromId = oldEdge.FromId, ToId = newTo, Label = oldEdge.Label });
        }

        var rawCycles = dag.FindAllCycles();

        var dedup = new HashSet<string>();
        var uniqueCycles = new List<string[]>();
        foreach (var cycle in rawCycles)
        {
            var closed = cycle.ToList();
            closed.Add(cycle[0]);
            var normalized = NormalizeCycle(closed.ToArray());
            var key = string.Join("->", normalized);
            if (dedup.Add(key))
                uniqueCycles.Add(normalized);
        }

        var results = new List<DiCycleInfo>(uniqueCycles.Count);
        foreach (var cycle in uniqueCycles)
        {
            var edges = BuildCycleEdges(cycle, constructorDeps);
            results.Add(new DiCycleInfo(
                cycle,
                edges,
                3
            ));
        }

        return results;
    }

    private static void EnsureNode(Dag<string> dag, string nodeId)
    {
        if (!dag.Nodes.ContainsKey(nodeId))
            dag.AddNode(new DagNode<string> { Id = nodeId, Payload = nodeId });
    }

    private static string[] NormalizeCycle(string[] cycle)
    {
        var core = cycle.Length > 1 ? cycle.Take(cycle.Length - 1).ToArray() : cycle;
        if (core.Length == 0)
            return cycle;

        var minIdx = 0;
        for (var i = 1; i < core.Length; i++)
        {
            if (string.CompareOrdinal(core[i], core[minIdx]) < 0)
                minIdx = i;
        }

        var rotated = new string[core.Length];
        for (var i = 0; i < core.Length; i++)
            rotated[i] = core[(minIdx + i) % core.Length];

        var result = new string[rotated.Length + 1];
        Array.Copy(rotated, result, rotated.Length);
        result[result.Length - 1] = rotated[0];
        return result;
    }

    private static (string From, string To, string? File, int? Line)[] BuildCycleEdges(
        string[] cycle,
        IReadOnlyList<ConstructorDependency> constructorDeps)
    {
        var edges = new (string From, string To, string? File, int? Line)[cycle.Length - 1];
        for (var i = 0; i < cycle.Length - 1; i++)
        {
            edges[i] = (cycle[i], cycle[i + 1], null, null);
        }

        for (var i = 0; i < edges.Length; i++)
        {
            var from = edges[i].From;
            var to = edges[i].To;
            foreach (var dep in constructorDeps)
            {
                if (dep.ClassName == from && dep.DependencyType == to && dep.FilePath is not null)
                {
                    edges[i] = (from, to, dep.FilePath, dep.LineNumber);
                    break;
                }
            }
        }

        return edges;
    }
}
