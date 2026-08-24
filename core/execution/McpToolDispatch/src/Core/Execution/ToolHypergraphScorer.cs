namespace McpToolDispatch;

/// <summary>
/// 工具链超图评分器 — 融合独立评分与超边共享评分，避免错误压制导致链路断裂
/// 算法: 最终评分 = (1 - Σ权重) × 独立评分 + Σ(超边权重 × 超边共享评分)
/// 定时更新超边共享评分（每小时），支持从配置文件热加载超边定义
/// </summary>
[Register(typeof(IHyperedgeReloadable), ServiceLifetime.Singleton)]
public sealed class ToolHypergraphScorer : ServiceEntity, IHyperedgeReloadable, IDisposable
{
    private readonly ILogger<ToolHypergraphScorer>? _logger;
    private readonly IToolHealthMonitor? _monitor;
    private ToolHypergraph _graph;
    private readonly Timer? _syncTimer;

    public ToolHypergraphScorer(ILogger<ToolHypergraphScorer>? logger = null, IToolHealthMonitor? monitor = null)
    {
        _logger = logger;
        _monitor = monitor;
        _graph = BuildGraph(ToolHypergraphPresets.GetPresets());

        if (_monitor is not null)
        {
            _syncTimer = new Timer(async _ => await SyncSharedScoresAsync().ConfigureAwait(false),
                null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// 从配置加载自定义超边 — 合并预设超边和用户自定义超边
    /// 用户自定义超边通过 Id 覆盖同名预设超边
    /// </summary>
    public void LoadCustomHyperedges(List<HyperedgeSettings> customHyperedges)
    {
        if (customHyperedges is null || customHyperedges.Count == 0) return;

        var presets = ToolHypergraphPresets.GetPresets();
        var presetById = presets.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var customEdges = customHyperedges.Select(c => c.ToHyperedge()).ToList();

        foreach (var custom in customEdges)
        {
            presetById[custom.Id] = custom;
        }

        var merged = presetById.Values.ToArray();
        _graph = BuildGraph(merged);
        _logger?.LogInformation("超图已加载自定义配置，{Custom} 条自定义 + {Preset} 条预设 = {Total} 条超边",
            customEdges.Count, presets.Length - customEdges.Count, merged.Length);
    }

    public void ReloadHyperedges(ToolHyperedge[] edges)
    {
        _graph = BuildGraph(edges);
        _logger?.LogInformation("超图已重新加载，{Count} 条超边", edges.Length);
    }

    /// <summary>
    /// 计算工具最终评分 — 融合独立评分与超边共享评分
    /// </summary>
    public int CalculateFinalScore(string toolName, int independentScore)
    {
        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges) || edges.Count == 0)
            return independentScore;

        var totalEdgeWeight = 0.0;
        var weightedSharedSum = 0.0;

        foreach (var edge in edges)
        {
            totalEdgeWeight += edge.Weight;
            weightedSharedSum += edge.Weight * edge.SharedScore;
        }

        totalEdgeWeight = Math.Min(totalEdgeWeight, 0.9);

        var independentWeight = 1.0 - totalEdgeWeight;
        var finalScore = (int)Math.Round(independentWeight * independentScore + weightedSharedSum);

        return Math.Clamp(finalScore, -100, 100);
    }

    /// <summary>
    /// 更新超边共享评分 — 根据成员工具的独立评分加权平均
    /// </summary>
    public void UpdateSharedScores(IReadOnlyDictionary<string, ToolHealthRecord> healthRecords)
    {
        foreach (var edge in _graph.Hyperedges)
        {
            var sum = 0;
            var count = 0;
            foreach (var toolName in edge.ToolNames)
            {
                if (healthRecords.TryGetValue(toolName, out var record))
                {
                    sum += record.Score;
                    count++;
                }
            }

            edge.SharedScore = count > 0 ? sum / count : 0;
        }
    }

    /// <summary>
    /// 获取工具的链路后续推荐 — LLM使用工具A后，推荐链路中的后续工具
    /// </summary>
    public string[]? GetChainRecommendations(string toolName)
    {
        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges))
            return null;

        foreach (var edge in edges)
        {
            if (edge.ChainOrder is null) continue;

            var idx = Array.FindIndex(edge.ChainOrder, n => string.Equals(n, toolName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < edge.ChainOrder.Length - 1)
                return edge.ChainOrder[(idx + 1)..];
        }

        return null;
    }

    /// <summary>
    /// 获取工具所属的所有超边
    /// </summary>
    public IReadOnlyList<ToolHyperedge> GetEdges(string toolName)
    {
        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges))
            return [];
        return edges;
    }

    private async Task SyncSharedScoresAsync()
    {
        if (_monitor is null) return;

        try
        {
            var allRecords = await _monitor.GetAllRecordsAsync().ConfigureAwait(false);
            UpdateSharedScores(allRecords);
            _logger?.LogDebug("超图共享评分已同步更新");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "超图共享评分同步更新失败");
        }
    }

    private static ToolHypergraph BuildGraph(ToolHyperedge[] edges)
    {
        var toolToEdges = new Dictionary<string, List<ToolHyperedge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            foreach (var toolName in edge.ToolNames)
            {
                if (!toolToEdges.TryGetValue(toolName, out var list))
                {
                    list = [];
                    toolToEdges[toolName] = list;
                }
                list.Add(edge);
            }
        }

        return new ToolHypergraph
        {
            Hyperedges = [.. edges],
            ToolToEdges = toolToEdges.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
        };
    }

    protected override void OnDispose()
    {
        _syncTimer?.Dispose();
    }
}
