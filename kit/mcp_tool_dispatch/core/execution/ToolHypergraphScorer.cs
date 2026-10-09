// JCC11004 抑制: 存量返回 null 集合, 后续改为空集合
#pragma warning disable JCC11004
namespace McpToolDispatch;

/// <summary>
/// 工具链超图评分器 — 融合独立评分与超边共享评分，避免错误压制导致链路断裂
/// 算法: 最终评分 = (1 - Σ权重) × 独立评分 + Σ(超边权重 × 超边共享评分)
/// 定时更新超边共享评分（每小时），支持从配置文件热加载超边定义
/// </summary>
[Register(typeof(IHyperedgeReloadable), ServiceLifetime.Singleton)]
public sealed class ToolHypergraphScorer : ServiceEntity, IHyperedgeReloadable, IDisposable {
    private readonly ILogger<ToolHypergraphScorer>? _logger;
    private readonly IToolHealthMonitor? _monitor;
    private ToolHypergraph _graph;
    private readonly Timer? _syncTimer;
    private readonly Timer? _rebuildTimer;
    private volatile HashSet<string> _lowFreqEdgeCandidates = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>获取当前低频超边候选移除集合。</summary>
    public IReadOnlyCollection<string> LowFreqEdgeCandidates => _lowFreqEdgeCandidates;

    /// <summary>
    /// 构造超图评分器 — 从预设加载超边并构建工具到超边的映射，若提供监控器则启动每小时共享评分同步定时器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="monitor">可选工具健康监控器，用于定时同步超边共享评分</param>
    public ToolHypergraphScorer(ILogger<ToolHypergraphScorer>? logger = null, IToolHealthMonitor? monitor = null) {
        _logger = logger;
        _monitor = monitor;
        _graph = BuildGraph(ToolHypergraphPresets.GetPresets());

        if (_monitor is not null) {
            _syncTimer = new Timer(async _ => await SyncSharedScoresAsync().ConfigureAwait(false),
                null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            _rebuildTimer = new Timer(async _ => await RebuildFromTransitionsAsync().ConfigureAwait(false),
                null, TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        }
    }

    /// <summary>
    /// 从配置加载自定义超边 — 合并预设超边和用户自定义超边
    /// 用户自定义超边通过 Id 覆盖同名预设超边
    /// </summary>
    public void LoadCustomHyperedges(List<HyperedgeSettings> customHyperedges) {
        if (customHyperedges is null || customHyperedges.Count == 0) return;

        var presets = ToolHypergraphPresets.GetPresets();
        var presetById = presets.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var customEdges = customHyperedges.Select(c => c.ToHyperedge()).ToList();

        foreach (var custom in customEdges) {
            presetById[custom.Id] = custom;
        }

        var merged = presetById.Values.ToArray();
        _graph = BuildGraph(merged);
        _logger?.LogInformation("超图已加载自定义配置，{Custom} 条自定义 + {Preset} 条预设 = {Total} 条超边",
            customEdges.Count, presets.Length - customEdges.Count, merged.Length);
    }

    /// <summary>
    /// 重新加载超边定义 — 用给定超边数组重建工具到超边的映射图
    /// </summary>
    /// <param name="edges">新的超边数组</param>
    public void ReloadHyperedges(ToolHyperedge[] edges) {
        _graph = BuildGraph(edges);
        _logger?.LogInformation("超图已重新加载，{Count} 条超边", edges.Length);
    }

    /// <summary>
    /// 计算工具最终评分 — 融合独立评分与超边共享评分
    /// </summary>
    public int CalculateFinalScore(string toolName, int independentScore) {
        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges) || edges.Count == 0)
            return independentScore;

        var totalEdgeWeight = 0.0;
        var weightedSharedSum = 0.0;

        foreach (var edge in edges) {
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
    public void UpdateSharedScores(IReadOnlyDictionary<string, ToolHealthRecord> healthRecords) {
        foreach (var edge in _graph.Hyperedges) {
            var sum = 0;
            var count = 0;
            foreach (var toolName in edge.ToolNames) {
                if (healthRecords.TryGetValue(toolName, out var record)) {
                    sum += record.Score;
                    count++;
                }
            }

            edge.SharedScore = count > 0 ? sum / count : 0;
        }
    }

    /// <summary>
    /// 获取工具的链路后续推荐 — 优先用运行时学习的转移频率推荐（频率>阈值时），频率不足时回退静态 ChainOrder
    /// </summary>
    /// <param name="toolName">当前工具名称</param>
    /// <param name="healthRecord">当前工具的健康记录（含转移频率），可为 null</param>
    /// <param name="frequencyThreshold">转移频率阈值，低于此值回退静态推荐（默认 3）</param>
    public string[]? GetChainRecommendations(string toolName, ToolHealthRecord? healthRecord = null, int frequencyThreshold = 3) {
        if (healthRecord is not null && healthRecord.NextToolFrequency.Count > 0) {
            var freqRecommendations = healthRecord.NextToolFrequency
                .Where(kvp => kvp.Value >= frequencyThreshold)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToArray();
            if (freqRecommendations.Length > 0)
                return freqRecommendations;
        }

        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges))
            return null;

        foreach (var edge in edges) {
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
    public IReadOnlyList<ToolHyperedge> GetEdges(string toolName) {
        if (!_graph.ToolToEdges.TryGetValue(toolName, out var edges))
            return [];
        return edges;
    }

    private async Task SyncSharedScoresAsync() {
        if (_monitor is null) return;

        try {
            var allRecords = await _monitor.GetAllRecordsAsync().ConfigureAwait(false);
            UpdateSharedScores(allRecords);
            _logger?.LogDebug("超图共享评分已同步更新");
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "超图共享评分同步更新失败");
        }
    }

    /// <summary>
    /// 从转移频率动态重建超图 — 扫描所有工具的 NextToolFrequency，频率>阈值的两工具间自动加超边
    /// 低频超边标记为"候选移除"，保留静态预设作为冷启动基线
    /// </summary>
    /// <param name="frequencyThreshold">转移频率阈值，低于此值不加超边（默认 5）</param>
    /// <param name="lowFreqThreshold">低频超边阈值，共享评分低于此值标记为候选移除（默认 -20）</param>
    public async Task RebuildFromTransitionsAsync(int frequencyThreshold = 5, int lowFreqThreshold = -20) {
        if (_monitor is null) return;

        try {
            var allRecords = await _monitor.GetAllRecordsAsync().ConfigureAwait(false);
            var presets = ToolHypergraphPresets.GetPresets();
            var presetIds = new HashSet<string>(presets.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            var dynamicEdges = new List<ToolHyperedge>();
            var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fromTool, record) in allRecords) {
                if (record.NextToolFrequency.Count == 0) continue;

                foreach (var (toTool, freq) in record.NextToolFrequency) {
                    if (freq < frequencyThreshold) continue;

                    var pairKey = string.Compare(fromTool, toTool, StringComparison.OrdinalIgnoreCase) < 0
                        ? $"{fromTool}→{toTool}" : $"{toTool}→{fromTool}";
                    if (!seenPairs.Add(pairKey)) continue;

                    var edgeId = $"freq_{fromTool}_{toTool}";
                    dynamicEdges.Add(new ToolHyperedge {
                        Id = edgeId,
                        ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, fromTool, toTool),
                        Weight = Math.Min(0.3 + freq * 0.01, 0.8),
                        ChainOrder = [fromTool, toTool]
                    });
                }
            }

            var allEdges = presets.Concat(dynamicEdges).ToArray();
            _graph = BuildGraph(allEdges);

            var lowFreq = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in _graph.Hyperedges) {
                if (presetIds.Contains(edge.Id)) continue;
                if (edge.SharedScore < lowFreqThreshold) {
                    lowFreq.Add(edge.Id);
                }
            }
            _lowFreqEdgeCandidates = lowFreq;

            _logger?.LogInformation("超图已从转移频率重建: {Preset} 条预设 + {Dynamic} 条动态 = {Total} 条超边, {LowFreq} 条低频候选",
                presets.Length, dynamicEdges.Count, allEdges.Length, lowFreq.Count);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "超图从转移频率重建失败");
        }
    }

    /// <summary>移除低频候选超边 — 将候选移除集合中的超边从图中剔除。</summary>
    public void RemoveLowFreqCandidates() {
        if (_lowFreqEdgeCandidates.Count == 0) return;

        var remaining = _graph.Hyperedges
            .Where(e => !_lowFreqEdgeCandidates.Contains(e.Id))
            .ToArray();
        _graph = BuildGraph(remaining);
        _lowFreqEdgeCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _logger?.LogInformation("已移除 {Count} 条低频超边", remaining.Length);
    }

    private static ToolHypergraph BuildGraph(ToolHyperedge[] edges) {
        var toolToEdges = new Dictionary<string, List<ToolHyperedge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges) {
            foreach (var toolName in edge.ToolNames) {
                if (!toolToEdges.TryGetValue(toolName, out var list)) {
                    list = [];
                    toolToEdges[toolName] = list;
                }
                list.Add(edge);
            }
        }

        return new ToolHypergraph {
            Hyperedges = [.. edges],
            ToolToEdges = toolToEdges.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// 释放定时器资源。
    /// </summary>
    public override void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _syncTimer?.Dispose();
        _rebuildTimer?.Dispose();
        base.Dispose();
    }
}