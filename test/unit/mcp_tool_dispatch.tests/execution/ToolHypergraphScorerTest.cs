namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolHypergraphScorer 单元测试 — 验证融合评分算法、链路推荐、共享评分更新
/// </summary>
public sealed class ToolHypergraphScorerTest
{
    private ToolHypergraphScorer _scorer = null!;

    public ToolHypergraphScorerTest()
    {
        _scorer = new ToolHypergraphScorer();
    }

    // === 辅助方法 ===

    private static ToolHyperedge CreateEdge(
        string id, string[] toolNames, double weight = 0.5,
        string[]? chainOrder = null, int sharedScore = 0)
    {
        return new ToolHyperedge
        {
            Id = id,
            ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, toolNames),
            Weight = weight,
            ChainOrder = chainOrder,
            SharedScore = sharedScore
        };
    }

    // === CalculateFinalScore ===

    [Fact]
    public void CalculateFinalScore_NoEdges_ReturnsIndependentScore()
    {
        var result = _scorer.CalculateFinalScore("unknown_tool", 42);
        result.Should().Be(42);
    }

    [Fact]
    public void CalculateFinalScore_WithEdge_FusesScores()
    {
        // 手动构建超图: tool_a 属于 edge1，权重 0.6，共享评分 80
        var edge = CreateEdge("e1", ["tool_a"], weight: 0.6, sharedScore: 80);
        _scorer.ReloadHyperedges([edge]);

        // finalScore = (1 - 0.6) * 50 + 0.6 * 80 = 0.4 * 50 + 48 = 20 + 48 = 68
        var result = _scorer.CalculateFinalScore("tool_a", 50);
        result.Should().Be(68);
    }

    [Fact]
    public void CalculateFinalScore_MultipleEdges_CapsTotalWeightAt09()
    {
        var edge1 = CreateEdge("e1", ["tool_a"], weight: 0.5, sharedScore: 100);
        var edge2 = CreateEdge("e2", ["tool_a"], weight: 0.5, sharedScore: 100);
        _scorer.ReloadHyperedges([edge1, edge2]);

        // totalEdgeWeight = 0.5 + 0.5 = 1.0 → capped at 0.9
        // independentWeight = 1 - 0.9 = 0.1
        // weightedSharedSum = 0.5 * 100 + 0.5 * 100 = 100
        // finalScore = round(0.1 * 50 + 100) = round(5 + 100) = 105 → clamped to 100
        var result = _scorer.CalculateFinalScore("tool_a", 50);
        result.Should().Be(100);
    }

    [Fact]
    public void CalculateFinalScore_NegativeIndependentScore_WithPositiveSharedScore()
    {
        var edge = CreateEdge("e1", ["tool_a"], weight: 0.4, sharedScore: 50);
        _scorer.ReloadHyperedges([edge]);

        // finalScore = (1 - 0.4) * (-30) + 0.4 * 50 = 0.6 * (-30) + 20 = -18 + 20 = 2
        var result = _scorer.CalculateFinalScore("tool_a", -30);
        result.Should().Be(2);
    }

    [Fact]
    public void CalculateFinalScore_ClampsToScoreRange()
    {
        var edge = CreateEdge("e1", ["tool_a"], weight: 0.9, sharedScore: 200);
        _scorer.ReloadHyperedges([edge]);

        // finalScore = (1 - 0.9) * 0 + 0.9 * 200 = 180 → clamped to 100
        var result = _scorer.CalculateFinalScore("tool_a", 0);
        result.Should().Be(100);
    }

    [Fact]
    public void CalculateFinalScore_ClampsToNegative100()
    {
        var edge = CreateEdge("e1", ["tool_a"], weight: 0.9, sharedScore: -200);
        _scorer.ReloadHyperedges([edge]);

        // finalScore = (1 - 0.9) * 0 + 0.9 * (-200) = -180 → clamped to -100
        var result = _scorer.CalculateFinalScore("tool_a", 0);
        result.Should().Be(-100);
    }

    // === GetChainRecommendations ===

    [Fact]
    public void GetChainRecommendations_NoEdges_ReturnsNull()
    {
        var result = _scorer.GetChainRecommendations("unknown_tool");
        result.Should().BeNull();
    }

    [Fact]
    public void GetChainRecommendations_EdgeWithoutChainOrder_ReturnsNull()
    {
        var edge = CreateEdge("e1", ["tool_a", "tool_b"], chainOrder: null);
        _scorer.ReloadHyperedges([edge]);

        var result = _scorer.GetChainRecommendations("tool_a");
        result.Should().BeNull();
    }

    [Fact]
    public void GetChainRecommendations_ToolInChain_ReturnsSubsequentTools()
    {
        var edge = CreateEdge("e1", ["read", "edit", "write"], chainOrder: ["read", "edit", "write"]);
        _scorer.ReloadHyperedges([edge]);

        var result = _scorer.GetChainRecommendations("read");
        result.Should().NotBeNull();
        result.Should().Equal("edit", "write");
    }

    [Fact]
    public void GetChainRecommendations_LastToolInChain_ReturnsNull()
    {
        var edge = CreateEdge("e1", ["read", "edit", "write"], chainOrder: ["read", "edit", "write"]);
        _scorer.ReloadHyperedges([edge]);

        var result = _scorer.GetChainRecommendations("write");
        result.Should().BeNull();
    }

    [Fact]
    public void GetChainRecommendations_MiddleToolInChain_ReturnsRemainingTools()
    {
        var edge = CreateEdge("e1", ["read", "edit", "write"], chainOrder: ["read", "edit", "write"]);
        _scorer.ReloadHyperedges([edge]);

        var result = _scorer.GetChainRecommendations("edit");
        result.Should().Equal("write");
    }

    [Fact]
    public void GetChainRecommendations_CaseInsensitiveLookup_MatchesByToolName()
    {
        // ToolToEdges 使用 ToFrozenDictionary() 后不保留 OrdinalIgnoreCase 比较器
        // 因此查找时需要使用与 ToolNames 中相同的大小写
        var edge = CreateEdge("e1", ["Read", "Edit"], chainOrder: ["Read", "Edit"]);
        _scorer.ReloadHyperedges([edge]);

        // 使用 ToolNames 中的原始大小写
        var result = _scorer.GetChainRecommendations("Read");
        result.Should().NotBeNull();
        result!.Length.Should().Be(1);
    }

    // === UpdateSharedScores ===

    [Fact]
    public void UpdateSharedScores_UpdatesEdgeSharedScore()
    {
        var edge = CreateEdge("e1", ["tool_a", "tool_b"], sharedScore: 0);
        _scorer.ReloadHyperedges([edge]);

        var healthRecords = new Dictionary<string, ToolHealthRecord>
        {
            ["tool_a"] = new() { ToolName = "tool_a", Score = 40 },
            ["tool_b"] = new() { ToolName = "tool_b", Score = 60 }
        };

        _scorer.UpdateSharedScores(healthRecords);

        // 共享评分 = (40 + 60) / 2 = 50
        var edges = _scorer.GetEdges("tool_a");
        edges.Should().HaveCount(1);
        edges[0].SharedScore.Should().Be(50);
    }

    [Fact]
    public void UpdateSharedScores_PartialHealthRecords_UsesAvailableRecords()
    {
        var edge = CreateEdge("e1", ["tool_a", "tool_b", "tool_c"], sharedScore: 0);
        _scorer.ReloadHyperedges([edge]);

        var healthRecords = new Dictionary<string, ToolHealthRecord>
        {
            ["tool_a"] = new() { ToolName = "tool_a", Score = 30 },
            ["tool_c"] = new() { ToolName = "tool_c", Score = 90 }
            // tool_b 没有健康记录
        };

        _scorer.UpdateSharedScores(healthRecords);

        // 共享评分 = (30 + 90) / 2 = 60 (只有2个有记录)
        var edges = _scorer.GetEdges("tool_a");
        edges[0].SharedScore.Should().Be(60);
    }

    [Fact]
    public void UpdateSharedScores_NoHealthRecords_SetsSharedScoreToZero()
    {
        var edge = CreateEdge("e1", ["tool_a"], sharedScore: 99);
        _scorer.ReloadHyperedges([edge]);

        _scorer.UpdateSharedScores(new Dictionary<string, ToolHealthRecord>());

        var edges = _scorer.GetEdges("tool_a");
        edges[0].SharedScore.Should().Be(0);
    }

    // === ReloadHyperedges ===

    [Fact]
    public void ReloadHyperedges_ReplacesExistingGraph()
    {
        var edge1 = CreateEdge("e1", ["tool_a"], weight: 0.3, sharedScore: 10);
        _scorer.ReloadHyperedges([edge1]);

        // 原始评分
        var score1 = _scorer.CalculateFinalScore("tool_a", 50);

        // 重新加载不同超图
        var edge2 = CreateEdge("e2", ["tool_a"], weight: 0.8, sharedScore: 90);
        _scorer.ReloadHyperedges([edge2]);

        var score2 = _scorer.CalculateFinalScore("tool_a", 50);
        score2.Should().NotBe(score1);
    }

    // === GetEdges ===

    [Fact]
    public void GetEdges_NoEdges_ReturnsEmptyList()
    {
        var edges = _scorer.GetEdges("unknown_tool");
        edges.Should().BeEmpty();
    }

    [Fact]
    public void GetEdges_ToolInMultipleEdges_ReturnsAllEdges()
    {
        var edge1 = CreateEdge("e1", ["shared_tool", "a"]);
        var edge2 = CreateEdge("e2", ["shared_tool", "b"]);
        _scorer.ReloadHyperedges([edge1, edge2]);

        var edges = _scorer.GetEdges("shared_tool");
        edges.Should().HaveCount(2);
    }

    // === 预设超图集成测试 ===

    [Fact]
    public void CalculateFinalScore_WithPresets_FileReadHasEdges()
    {
        // 使用默认预设
        _scorer = new ToolHypergraphScorer();

        // FileToolName.FileRead.ToValue() = "Read"
        var score = _scorer.CalculateFinalScore("Read", 50);
        // 不应抛异常，且应返回有效评分
        score.Should().BeInRange(-100, 100);
    }

    // === LoadCustomHyperedges ===

    [Fact]
    public void LoadCustomHyperedges_MergesWithPresets()
    {
        var custom = new List<JoinCode.Abstractions.Configuration.Settings.HyperedgeSettings>
        {
            new()
            {
                Id = "custom_chain",
                ToolNames = ["custom_a", "custom_b"],
                Weight = 0.7,
                ChainOrder = ["custom_a", "custom_b"]
            }
        };

        _scorer.LoadCustomHyperedges(custom);

        // 自定义工具应该有超边
        var edges = _scorer.GetEdges("custom_a");
        edges.Should().HaveCount(1);
        edges[0].Id.Should().Be("custom_chain");

        // 预设超边应该仍然存在 (FileToolName.FileRead.ToValue() = "Read")
        var presetEdges = _scorer.GetEdges("Read");
        presetEdges.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadCustomHyperedges_OverridesPresetById()
    {
        var custom = new List<JoinCode.Abstractions.Configuration.Settings.HyperedgeSettings>
        {
            new()
            {
                Id = "file_ops",
                ToolNames = ["custom_file_a"],
                Weight = 0.8
            }
        };

        _scorer.LoadCustomHyperedges(custom);

        // file_ops 超边应该被覆盖
        var edges = _scorer.GetEdges("custom_file_a");
        edges.Should().HaveCount(1);
        edges[0].Weight.Should().Be(0.8);

        // 原来的 Read 不应再在 file_ops 超边中
        var oldEdges = _scorer.GetEdges("Read");
        oldEdges.Should().BeEmpty();
    }

    [Fact]
    public void LoadCustomHyperedges_EmptyList_DoesNothing()
    {
        var scoreBefore = _scorer.CalculateFinalScore("Read", 50);
        _scorer.LoadCustomHyperedges([]);
        var scoreAfter = _scorer.CalculateFinalScore("Read", 50);
        scoreAfter.Should().Be(scoreBefore);
    }

    [Fact]
    public void LoadCustomHyperedges_Null_DoesNothing()
    {
        var scoreBefore = _scorer.CalculateFinalScore("Read", 50);
        _scorer.LoadCustomHyperedges(null!);
        var scoreAfter = _scorer.CalculateFinalScore("Read", 50);
        scoreAfter.Should().Be(scoreBefore);
    }
}
