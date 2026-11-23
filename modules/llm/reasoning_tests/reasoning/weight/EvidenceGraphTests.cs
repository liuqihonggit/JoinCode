namespace JoinCode.Reasoning.Tests.Weight;

public sealed class EvidenceGraphTests {
    [Fact]
    public void AddNode_ShouldStoreNodeWithInitialWeight() {
        var graph = new EvidenceGraph();
        var evidence = new EvidenceRecord {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        graph.AddNode(evidence);

        Assert.Single(graph.GetAllNodes());
    }

    [Fact]
    public void AddEdge_ShouldConnectNodes() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddEdge("ev1", "ev2", 0.8, "SUPPORTS");

        Assert.Single(graph.GetAllEdges());
    }

    [Fact]
    public void ApplyMessagePassing_ShouldUpdateWeights() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddEdge("ev1", "ev2", 0.8);

        var before = graph.GetAllNodes()["ev1"].CurrentWeight;
        graph.ApplyMessagePassing(3);
        var after = graph.GetAllNodes()["ev1"].CurrentWeight;

        Assert.True(after > 0);
    }

    [Fact]
    public void GetNodeTrustScore_ShouldIncludeGraphStructure() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddNode(CreateEvidence("ev3"));
        graph.AddEdge("ev1", "ev2", 0.8);
        graph.AddEdge("ev2", "ev3", 0.7);

        graph.ApplyMessagePassing(3);
        var score = graph.GetNodeTrustScore("ev2");

        Assert.True(score > 0);
    }

    [Fact]
    public void GetEdgeStrength_ForwardEdge_ShouldReturnStrength() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddEdge("ev1", "ev2", 0.8);

        var strength = graph.GetEdgeStrength("ev1", "ev2");

        Assert.Equal(0.8, strength);
    }

    [Fact]
    public void GetEdgeStrength_ReverseLookup_ShouldReturnStrength() {
        // 只存 ev1→ev2,反向查找 ev2→ev1 应返回同一边强度
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddEdge("ev1", "ev2", 0.6);

        var strength = graph.GetEdgeStrength("ev2", "ev1");

        Assert.Equal(0.6, strength);
    }

    [Fact]
    public void GetEdgeStrength_NoEdge_ShouldReturnOne() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));

        var strength = graph.GetEdgeStrength("ev1", "ev2");

        Assert.Equal(1.0, strength);
    }

    [Fact]
    public void CalculateGraphCentrality_NoNodes_ShouldReturnZero() {
        var graph = new EvidenceGraph();

        var centrality = graph.CalculateGraphCentrality("ev1");

        Assert.Equal(0, centrality);
    }

    [Fact]
    public void CalculateGraphCentrality_IsolatedNode_ShouldReturnZero() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));

        var centrality = graph.CalculateGraphCentrality("ev1");

        // inDegree=0, outDegree=0, nodes=1 → 0
        Assert.Equal(0, centrality);
    }

    [Fact]
    public void CalculateGraphCentrality_NodeWithEdges_ShouldComputeDegreeRatio() {
        // a→b, b→c: b 的 centrality = (inDegree=1 + outDegree=1) / nodes=3 = 2/3
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddNode(CreateEvidence("ev3"));
        graph.AddEdge("ev1", "ev2", 0.8);
        graph.AddEdge("ev2", "ev3", 0.7);

        var centrality = graph.CalculateGraphCentrality("ev2");

        Assert.Equal(0.6666667, centrality, 0.0001);
    }

    [Fact]
    public void CalculateNeighborConsensus_NoNeighbors_ShouldReturnZero() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));

        var consensus = graph.CalculateNeighborConsensus("ev1");

        Assert.Equal(0, consensus);
    }

    [Fact]
    public void CalculateNeighborConsensus_WithNeighbors_ShouldReturnAverageWeight() {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddNode(CreateEvidence("ev3"));
        graph.AddEdge("ev1", "ev2", 0.8);
        graph.AddEdge("ev1", "ev3", 0.7);
        // 设置邻居当前权重
        graph.GetAllNodes()["ev2"].CurrentWeight = 0.4;
        graph.GetAllNodes()["ev3"].CurrentWeight = 0.6;

        var consensus = graph.CalculateNeighborConsensus("ev1");

        // 邻居 ev2,ev3 平均权重 = (0.4+0.6)/2 = 0.5
        Assert.Equal(0.5, consensus, 0.0001);
    }

    private static EvidenceRecord CreateEvidence(string id) {
        return new EvidenceRecord {
            Id = id,
            Content = $"证据{id}",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
    }
}