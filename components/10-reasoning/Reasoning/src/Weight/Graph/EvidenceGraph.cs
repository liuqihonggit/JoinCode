namespace JoinCode.Reasoning.Weight.Graph;

/// <summary>
/// 证据图节点 — 用于图神经网络风格的消息传递
/// </summary>
public sealed class EvidenceGraphNode
{
    public required string EvidenceId { get; init; }
    public double InitialWeight { get; set; }
    public double CurrentWeight { get; set; }
}

/// <summary>
/// 证据图边 — 节点间关系
/// </summary>
public sealed class EvidenceGraphEdge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public double RelationshipStrength { get; init; } = 1.0;
    public string? Label { get; init; }
}

/// <summary>
/// 证据图 — 图神经网络风格的消息传递和信任度传播
/// </summary>
public sealed class EvidenceGraph
{
    private readonly Dictionary<string, EvidenceGraphNode> _nodes = [];
    private readonly List<EvidenceGraphEdge> _edges = [];
    private readonly EvidenceWeightCalculator _calculator = new();

    /// <summary>
    /// 消息传递自保持权重
    /// </summary>
    public double SelfRetention { get; init; } = 0.6;

    /// <summary>
    /// 消息传递邻居聚合权重
    /// </summary>
    public double NeighborAggregation { get; init; } = 0.4;

    /// <summary>
    /// 默认消息传递迭代次数
    /// </summary>
    public int DefaultIterations { get; init; } = 3;

    /// <summary>
    /// 添加节点
    /// </summary>
    public void AddNode(EvidenceRecord evidence, int corroborationCount = 0)
    {
        var weight = _calculator.CalculateWeight(evidence, corroborationCount);
        var node = new EvidenceGraphNode
        {
            EvidenceId = evidence.Id,
            InitialWeight = weight.Total,
            CurrentWeight = weight.Total,
        };
        _nodes[evidence.Id] = node;
    }

    /// <summary>
    /// 添加边
    /// </summary>
    public void AddEdge(string sourceId, string targetId, double strength = 1.0, string? label = null)
    {
        _edges.Add(new EvidenceGraphEdge
        {
            SourceId = sourceId,
            TargetId = targetId,
            RelationshipStrength = strength,
            Label = label,
        });
    }

    /// <summary>
    /// 执行消息传递迭代
    /// </summary>
    public void ApplyMessagePassing(int? iterations = null)
    {
        var iters = iterations ?? DefaultIterations;

        for (var iter = 0; iter < iters; iter++)
        {
            var newWeights = new Dictionary<string, double>();

            foreach (var node in _nodes.Values)
            {
                var neighborMessages = GetNeighbors(node.EvidenceId)
                    .Select(n => n.CurrentWeight * GetEdgeStrength(node.EvidenceId, n.EvidenceId))
                    .ToList();

                newWeights[node.EvidenceId] =
                    SelfRetention * node.CurrentWeight +
                    NeighborAggregation * (neighborMessages.Count > 0 ? neighborMessages.Average() : 0);
            }

            foreach (var kvp in newWeights)
            {
                if (_nodes.ContainsKey(kvp.Key))
                {
                    _nodes[kvp.Key].CurrentWeight = kvp.Value;
                }
            }
        }
    }

    /// <summary>
    /// 获取节点信任评分 — 基础权重 + 图结构影响
    /// </summary>
    public double GetNodeTrustScore(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node)) return 0;

        var baseWeight = node.InitialWeight;
        var graphBoost = CalculateGraphCentrality(nodeId);
        var consensusBoost = CalculateNeighborConsensus(nodeId);

        return baseWeight * (0.6 + 0.4 * graphBoost) * (1.0 + consensusBoost * 0.2);
    }

    /// <summary>
    /// 获取所有节点
    /// </summary>
    public IReadOnlyDictionary<string, EvidenceGraphNode> GetAllNodes() => _nodes;

    /// <summary>
    /// 获取所有边
    /// </summary>
    public IReadOnlyList<EvidenceGraphEdge> GetAllEdges() => _edges;

    private List<EvidenceGraphNode> GetNeighbors(string nodeId)
    {
        var neighborIds = _edges
            .Where(e => e.SourceId == nodeId || e.TargetId == nodeId)
            .Select(e => e.SourceId == nodeId ? e.TargetId : e.SourceId)
            .ToHashSet();

        return neighborIds
            .Where(id => _nodes.ContainsKey(id))
            .Select(id => _nodes[id])
            .ToList();
    }

    private double GetEdgeStrength(string fromId, string toId)
    {
        var edge = _edges.FirstOrDefault(e =>
            (e.SourceId == fromId && e.TargetId == toId) ||
            (e.SourceId == toId && e.TargetId == fromId));
        return edge?.RelationshipStrength ?? 1.0;
    }

    private double CalculateGraphCentrality(string nodeId)
    {
        var inDegree = _edges.Count(e => e.TargetId == nodeId);
        var outDegree = _edges.Count(e => e.SourceId == nodeId);
        return _nodes.Count > 0 ? (inDegree + outDegree) / (double)_nodes.Count : 0;
    }

    private double CalculateNeighborConsensus(string nodeId)
    {
        var neighbors = GetNeighbors(nodeId);
        if (neighbors.Count == 0) return 0;

        var avgWeight = neighbors.Average(n => n.CurrentWeight);
        return avgWeight;
    }
}
