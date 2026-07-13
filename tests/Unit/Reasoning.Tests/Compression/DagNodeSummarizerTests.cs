namespace JoinCode.Reasoning.Tests.Compression;

public sealed class DagNodeSummarizerTests
{
    [Fact]
    public async Task SummarizeResolvedNodesAsync_BelowThreshold_NoSummarization()
    {
        var dag = CreateDagWithResolvedNodes(5, 200);
        var summarizer = new DagNodeSummarizer();

        await summarizer.SummarizeResolvedNodesAsync(dag, threshold: 30);

        foreach (var node in dag.Nodes.Values)
        {
            node.Payload.OriginalContent.Should().BeNull("below threshold, no summarization");
        }
    }

    [Fact]
    public async Task SummarizeResolvedNodesAsync_AboveThreshold_ShortContentUnchanged()
    {
        var dag = new Dag<ReasoningPayload>();
        for (var i = 0; i < 35; i++)
        {
            var node = new DagNode<ReasoningPayload>
            {
                Id = $"node{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"node{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = "短内容",
                    State = DataState.Fact,
                },
            };
            dag.AddNode(node);
        }

        var summarizer = new DagNodeSummarizer();
        await summarizer.SummarizeResolvedNodesAsync(dag, threshold: 30);

        foreach (var node in dag.Nodes.Values)
        {
            node.Payload.OriginalContent.Should().BeNull("short content should not be summarized");
        }
    }

    [Fact]
    public async Task SummarizeResolvedNodesAsync_AboveThreshold_LongResolvedContentTruncated()
    {
        var dag = new Dag<ReasoningPayload>();
        var longContent = new string('a', 500);
        for (var i = 0; i < 35; i++)
        {
            var node = new DagNode<ReasoningPayload>
            {
                Id = $"node{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"node{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = longContent,
                    State = DataState.Fact,
                },
            };
            dag.AddNode(node);
        }

        var summarizer = new DagNodeSummarizer();
        await summarizer.SummarizeResolvedNodesAsync(dag, threshold: 30);

        foreach (var node in dag.Nodes.Values.Where(n => n.Payload.State is DataState.Fact))
        {
            node.Payload.OriginalContent.Should().NotBeNull("original content should be preserved");
            node.Payload.OriginalContent.Should().Be(longContent);
            node.Payload.Content.Should().EndWith("...");
            node.Payload.Content.Length.Should().BeLessThan(longContent.Length);
        }
    }

    [Fact]
    public async Task SummarizeResolvedNodesAsync_AlreadySummarized_NotReSummarized()
    {
        var dag = new Dag<ReasoningPayload>();
        var longContent = new string('a', 500);
        for (var i = 0; i < 35; i++)
        {
            var node = new DagNode<ReasoningPayload>
            {
                Id = $"node{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"node{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = "already summarized",
                    State = DataState.Fact,
                    OriginalContent = longContent,
                },
            };
            dag.AddNode(node);
        }

        var summarizer = new DagNodeSummarizer();
        await summarizer.SummarizeResolvedNodesAsync(dag, threshold: 30);

        foreach (var node in dag.Nodes.Values)
        {
            node.Payload.Content.Should().Be("already summarized", "already summarized nodes should not be re-summarized");
        }
    }

    [Fact]
    public async Task SummarizeResolvedNodesAsync_OnlyResolvedNodesSummarized()
    {
        var dag = new Dag<ReasoningPayload>();
        var longContent = new string('a', 500);

        for (var i = 0; i < 35; i++)
        {
            var node = new DagNode<ReasoningPayload>
            {
                Id = $"resolved{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"resolved{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = longContent,
                    State = DataState.Fact,
                },
            };
            dag.AddNode(node);
        }

        var assumptionNode = new DagNode<ReasoningPayload>
        {
            Id = "assumption1",
            Payload = new ReasoningPayload
            {
                Id = "assumption1",
                Type = ReasoningNodeType.Assumption,
                Content = longContent,
                State = DataState.Assumption,
            },
        };
        dag.AddNode(assumptionNode);

        var summarizer = new DagNodeSummarizer();
        await summarizer.SummarizeResolvedNodesAsync(dag, threshold: 30);

        assumptionNode.Payload.OriginalContent.Should().BeNull("assumption nodes should not be summarized");
    }

    private static Dag<ReasoningPayload> CreateDagWithResolvedNodes(int count, int contentLength)
    {
        var dag = new Dag<ReasoningPayload>();
        var content = new string('x', contentLength);

        for (var i = 0; i < count; i++)
        {
            var node = new DagNode<ReasoningPayload>
            {
                Id = $"node{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"node{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = content,
                    State = DataState.Fact,
                },
            };
            dag.AddNode(node);
        }

        return dag;
    }
}
