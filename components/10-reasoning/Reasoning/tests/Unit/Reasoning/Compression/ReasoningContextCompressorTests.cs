namespace JoinCode.Reasoning.Tests.Compression;

public sealed class ReasoningContextCompressorTests
{
    [Fact]
    public async Task CompressForRoleAsync_NoCone_NoCompressionNeeded()
    {
        var compressor = CreateCompressor();
        var context = CreateSimpleContext();

        var result = await compressor.CompressForRoleAsync(context, AgentRole.Prosecutor, 4000);

        result.Method.Should().Be(CompressionMethod.None);
        result.UserPrompt.Should().NotBeEmpty();
        result.EstimatedTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompressForRoleAsync_WithCone_ConeFiltered()
    {
        var compressor = CreateCompressor();
        var cone = new ConeOrchestrator();
        cone.RegisterRole(AgentRole.Prosecutor, 5);

        var item = new DataItem { Id = "item1", Content = "假定1", State = DataState.Assumption, Confidence = 80 };
        var fragment = cone.CreateFragmentFromItem(AgentRole.Prosecutor, item);
        cone.GetRole(AgentRole.Prosecutor)!.AddFragment(fragment);

        var context = new ReasoningContext
        {
            AllItems = [item],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = cone,
        };

        var result = await compressor.CompressForRoleAsync(context, AgentRole.Prosecutor, 4000);

        result.Method.Should().Be(CompressionMethod.ConeFiltered);
    }

    [Fact]
    public async Task CompressForRoleAsync_ExceedsMaxTokens_Truncated()
    {
        var compressor = CreateCompressor();
        var items = Enumerable.Range(0, 100)
            .Select(i => new DataItem
            {
                Id = $"item{i}",
                Content = new string('a', 200),
                State = DataState.Assumption,
                Confidence = 80,
            })
            .ToList();

        var context = new ReasoningContext
        {
            AllItems = items,
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
        };

        var result = await compressor.CompressForRoleAsync(context, AgentRole.Prosecutor, 100);

        result.Method.Should().Be(CompressionMethod.Truncated);
        result.EstimatedTokens.Should().BeLessThan(200);
    }

    [Fact]
    public async Task CompressForRoleAsync_ProsecutorOnlySeesAssumptions()
    {
        var compressor = CreateCompressor();
        var items = new List<DataItem>
        {
            new() { Id = "a1", Content = "假定1", State = DataState.Assumption, Confidence = 80 },
            new() { Id = "f1", Content = "事实1", State = DataState.Fact, Confidence = 90 },
            new() { Id = "r1", Content = "驳回1", State = DataState.Rejected, Confidence = 10 },
        };

        var context = new ReasoningContext
        {
            AllItems = items,
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
        };

        var result = await compressor.CompressForRoleAsync(context, AgentRole.Prosecutor, 4000);

        result.UserPrompt.Should().Contain("假定1");
        result.UserPrompt.Should().NotContain("事实1");
        result.UserPrompt.Should().NotContain("驳回1");
    }

    [Fact]
    public async Task CompressForRoleAsync_JudgeSeesEvidenceSummary()
    {
        var compressor = CreateCompressor();
        var items = new List<DataItem>
        {
            new() { Id = "a1", Content = "假定1", State = DataState.Assumption, Confidence = 80 },
        };
        var evidence = new List<EvidenceRecord>
        {
            new() { Id = "e1", Content = "控方证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
            new() { Id = "e2", Content = "辩方反驳", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Weak, SubmittedBy = AgentRole.Defender },
        };

        var context = new ReasoningContext
        {
            AllItems = items,
            AllEvidence = evidence,
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
        };

        var result = await compressor.CompressForRoleAsync(context, AgentRole.Judge, 4000);

        result.UserPrompt.Should().Contain("控方1条");
        result.UserPrompt.Should().Contain("辩方1条");
    }

    [Fact]
    public async Task SummarizeResolvedNodesAsync_DelegatesToDagSummarizer()
    {
        var compressor = CreateCompressor();
        var dag = new Dag<ReasoningPayload>();
        for (var i = 0; i < 35; i++)
        {
            dag.AddNode(new DagNode<ReasoningPayload>
            {
                Id = $"n{i}",
                Payload = new ReasoningPayload
                {
                    Id = $"n{i}",
                    Type = ReasoningNodeType.Evidence,
                    Content = new string('x', 500),
                    State = DataState.Fact,
                },
            });
        }

        await compressor.SummarizeResolvedNodesAsync(dag, threshold: 30);

        foreach (var node in dag.Nodes.Values.Where(n => n.Payload.State == DataState.Fact))
        {
            node.Payload.OriginalContent.Should().NotBeNull();
        }
    }

    private static ReasoningContextCompressor CreateCompressor(IContextCompressor? contextCompressor = null)
    {
        var logger = new Mock<ILogger<ReasoningContextCompressor>>().Object;
        return new ReasoningContextCompressor(logger, contextCompressor);
    }

    private static ReasoningContext CreateSimpleContext()
    {
        return new ReasoningContext
        {
            AllItems = [new DataItem { Id = "item1", Content = "测试假定", State = DataState.Assumption, Confidence = 80 }],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
        };
    }
}
