namespace JccAuditCli;

/// <summary>
/// DI 循环依赖检测器：构建全局有向图，DFS 检测循环
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
        // 构建邻接表
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var reg in registrations)
        {
            AddNode(graph, reg.ServiceType);
            AddNode(graph, reg.ImplementationType);
            AddEdge(graph, reg.ServiceType, reg.ImplementationType);
        }

        // 构造函数依赖边
        foreach (var dep in constructorDeps)
        {
            if (dep.IsOptional)
                continue; // 可选依赖不加入硬边

            AddNode(graph, dep.ClassName);
            AddNode(graph, dep.DependencyType);
            AddEdge(graph, dep.ClassName, dep.DependencyType);
        }

        // 展开接口别名
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

        // 替换所有边：接口 -> 实现
        var edgeReplacements = new List<(string From, string To, string NewTo)>();
        foreach (var (node, neighbors) in graph)
        {
            foreach (var neighbor in neighbors)
            {
                if (serviceToImplMapping.TryGetValue(neighbor, out var impl) && impl != neighbor)
                {
                    edgeReplacements.Add((node, neighbor, impl));
                }
            }
        }
        foreach (var (from, oldTo, newTo) in edgeReplacements)
        {
            if (graph.TryGetValue(from, out var neighborSet))
            {
                neighborSet.Remove(oldTo);
                neighborSet.Add(newTo);
            }
        }

        // DFS 检测所有循环
        var cycles = FindAllCycles(graph);

        // 转换为 DiCycleInfo
        var results = new List<DiCycleInfo>(cycles.Count);
        foreach (var cycle in cycles)
        {
            var edges = BuildCycleEdges(cycle, constructorDeps);
            results.Add(new DiCycleInfo(
                cycle,
                edges,
                3 // Error
            ));
        }

        return results;
    }

    private static void AddNode(Dictionary<string, HashSet<string>> graph, string node)
    {
        if (!graph.ContainsKey(node))
            graph[node] = new HashSet<string>(StringComparer.Ordinal);
    }

    private static void AddEdge(Dictionary<string, HashSet<string>> graph, string from, string to)
    {
        AddNode(graph, from);
        graph[from].Add(to);
    }

    /// <summary>
    /// Johnson's 算法简化版：DFS 检测所有基本循环
    /// </summary>
    private static List<string[]> FindAllCycles(Dictionary<string, HashSet<string>> graph)
    {
        var cycles = new List<string[]>();
        var visited = new HashSet<string>();
        var recStack = new HashSet<string>();
        var path = new List<string>();

        var sortedNodes = graph.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        foreach (var node in sortedNodes)
        {
            if (visited.Contains(node))
                continue;
            Dfs(node);
        }

        void Dfs(string currentNode)
        {
            if (recStack.Contains(currentNode))
            {
                var cycleIdx = path.IndexOf(currentNode);
                if (cycleIdx >= 0)
                {
                    var cycle = path.Skip(cycleIdx).ToList();
                    cycle.Add(currentNode); // 闭合
                    cycles.Add(cycle.ToArray());
                }
                return;
            }

            if (visited.Contains(currentNode))
                return;

            visited.Add(currentNode);
            recStack.Add(currentNode);
            path.Add(currentNode);

            if (graph.TryGetValue(currentNode, out var neighbors))
            {
                foreach (var neighbor in neighbors.OrderBy(n => n, StringComparer.Ordinal))
                {
                    Dfs(neighbor);
                }
            }

            path.RemoveAt(path.Count - 1);
            recStack.Remove(currentNode);
        }

        // 规范化去重
        var dedup = new HashSet<string>();
        var uniqueCycles = new List<string[]>();
        foreach (var cycle in cycles)
        {
            var normalized = NormalizeCycle(cycle);
            var key = string.Join("->", normalized);
            if (dedup.Add(key))
                uniqueCycles.Add(normalized);
        }

        return uniqueCycles;
    }

    private static string[] NormalizeCycle(string[] cycle)
    {
        // ["A", "B", "C", "A"] → 去掉最后一个
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

        // 从 constructorDeps 补充位置信息
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
