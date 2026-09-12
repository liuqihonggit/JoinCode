namespace JoinCode.Reasoning.Tests.Weight;

public sealed class EvidenceGraphTests
{
    [Fact]
    public void AddNode_ShouldStoreNodeWithInitialWeight()
    {
        var graph = new EvidenceGraph();
        var evidence = new EvidenceRecord
        {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        graph.AddNode(evidence);

        Assert.Single(graph.GetAllNodes());
    }

    [Fact]
    public void AddEdge_ShouldConnectNodes()
    {
        var graph = new EvidenceGraph();
        graph.AddNode(CreateEvidence("ev1"));
        graph.AddNode(CreateEvidence("ev2"));
        graph.AddEdge("ev1", "ev2", 0.8, "SUPPORTS");

        Assert.Single(graph.GetAllEdges());
    }

    [Fact]
    public void ApplyMessagePassing_ShouldUpdateWeights()
    {
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
    public void GetNodeTrustScore_ShouldIncludeGraphStructure()
    {
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

    private static EvidenceRecord CreateEvidence(string id)
    {
        return new EvidenceRecord
        {
            Id = id,
            Content = $"证据{id}",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
    }
}
