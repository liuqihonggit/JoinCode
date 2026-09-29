namespace JoinCode.Reasoning.Weight.Graph;

/// <summary>
/// 证据图节点 — 用于图神经网络风格的消息传递
/// </summary>
public sealed class EvidenceGraphNode {
    /// <summary>
    /// 证据标识
    /// </summary>
    public required string EvidenceId { get; init; }

    /// <summary>
    /// 初始权重
    /// </summary>
    public double InitialWeight { get; set; }

    /// <summary>
    /// 当前权重（消息传递后动态更新）
    /// </summary>
    public double CurrentWeight { get; set; }
}

/// <summary>
/// 证据图边 — 节点间关系
/// </summary>
public sealed class EvidenceGraphEdge {
    /// <summary>
    /// 源节点标识
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// 目标节点标识
    /// </summary>
    public required string TargetId { get; init; }

    /// <summary>
    /// 关系强度，默认 1.0
    /// </summary>
    public double RelationshipStrength { get; init; } = 1.0;

    /// <summary>
    /// 边标签；可选
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// 证据图 — 图神经网络风格的消息传递和信任度传播
/// </summary>
public sealed class EvidenceGraph {
    private ImmutableHamT<string, EvidenceGraphNode> _nodes = ImmutableHamT<string, EvidenceGraphNode>.Empty;
    private ImmutableHamT<(string SourceId, string TargetId), EvidenceGraphEdge> _edges = ImmutableHamT<(string SourceId, string TargetId), EvidenceGraphEdge>.Empty;
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
    public void AddNode(EvidenceRecord evidence, int corroborationCount = 0) {
        var weight = _calculator.CalculateWeight(evidence, corroborationCount);
        var node = new EvidenceGraphNode {
            EvidenceId = evidence.Id,
            InitialWeight = weight.Total,
            CurrentWeight = weight.Total,
        };
        while (true) {
            var current = _nodes;
            if (Interlocked.CompareExchange(ref _nodes, current.SetItem(evidence.Id, node), current) == current) return;
        }
    }

    /// <summary>
    /// 添加边
    /// </summary>
    public void AddEdge(string sourceId, string targetId, double strength = 1.0, string? label = null) {
        var edge = new EvidenceGraphEdge {
            SourceId = sourceId,
            TargetId = targetId,
            RelationshipStrength = strength,
            Label = label,
        };
        while (true) {
            var current = _edges;
            if (Interlocked.CompareExchange(ref _edges, current.SetItem((sourceId, targetId), edge), current) == current) return;
        }
    }

    /// <summary>
    /// 执行消息传递迭代
    /// </summary>
    public void ApplyMessagePassing(int? iterations = null) {
        var iters = iterations ?? DefaultIterations;

        for (var iter = 0; iter < iters; iter++) {
            var nodesSnapshot = _nodes;
            var newWeights = new Dictionary<string, double>();

            foreach (var node in nodesSnapshot.Values) {
                var neighborMessages = GetNeighbors(node.EvidenceId)
                    .Select(n => n.CurrentWeight * GetEdgeStrength(node.EvidenceId, n.EvidenceId))
                    .ToList();

                newWeights[node.EvidenceId] =
                    SelfRetention * node.CurrentWeight +
                    NeighborAggregation * (neighborMessages.Count > 0 ? neighborMessages.Average() : 0);
            }

            foreach (var kvp in newWeights) {
                if (nodesSnapshot.TryGetValue(kvp.Key, out var node)) {
                    node.CurrentWeight = kvp.Value;
                }
            }
        }
    }

    /// <summary>
    /// 获取节点信任评分 — 基础权重 + 图结构影响
    /// </summary>
    public double GetNodeTrustScore(string nodeId) {
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
    /// 获取所有边的快照拷贝
    /// </summary>
    public EvidenceGraphEdge[] GetAllEdges() => _edges.Values.ToArray();

    private List<EvidenceGraphNode> GetNeighbors(string nodeId) {
        var edgesSnapshot = _edges;
        var nodesSnapshot = _nodes;
        var neighborIds = edgesSnapshot.Values
            .Where(e => e.SourceId == nodeId || e.TargetId == nodeId)
            .Select(e => e.SourceId == nodeId ? e.TargetId : e.SourceId)
            .ToHashSet();

        return neighborIds
            .Where(id => nodesSnapshot.ContainsKey(id))
            .Select(id => nodesSnapshot[id])
            .ToList();
    }

    internal double GetEdgeStrength(string fromId, string toId) {
        var edges = _edges;
        if (edges.TryGetValue((fromId, toId), out var edge))
            return edge.RelationshipStrength;
        if (edges.TryGetValue((toId, fromId), out var reverseEdge))
            return reverseEdge.RelationshipStrength;
        return 1.0;
    }

    internal double CalculateGraphCentrality(string nodeId) {
        var edges = _edges;
        var nodes = _nodes;
        var inDegree = edges.Values.Count(e => e.TargetId == nodeId);
        var outDegree = edges.Values.Count(e => e.SourceId == nodeId);
        return nodes.Count > 0 ? (inDegree + outDegree) / (double)nodes.Count : 0;
    }

    internal double CalculateNeighborConsensus(string nodeId) {
        var neighbors = GetNeighbors(nodeId);
        if (neighbors.Count == 0) return 0;

        var avgWeight = neighbors.Average(n => n.CurrentWeight);
        return avgWeight;
    }
}