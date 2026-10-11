// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace McpToolRegistry.Tests;

/// <summary>
/// OnErrorToolInjectionMiddleware 辅助纯函数确定性测试 — 关键词提取、相似模式判定、工具筛选、历史分析拆分
/// </summary>
public class OnErrorToolInjectionMiddlewareTest {
    // === ExtractErrorKeywords (internal static) ===

    [Fact]
    public void ExtractErrorKeywords_FiltersShortWordsAndTakesTen() {
        var keywords = OnErrorToolInjectionMiddleware.ExtractErrorKeywords("File not found error timeout");
        keywords.Should().Equal(["File", "found", "error", "timeout"]);
    }

    [Fact]
    public void ExtractErrorKeywords_ShortWordsFilteredOut() {
        var keywords = OnErrorToolInjectionMiddleware.ExtractErrorKeywords("a ab abc abcd");
        keywords.Should().Equal(["abcd"]);
    }

    [Fact]
    public void ExtractErrorKeywords_SplitsOnSeparators() {
        var keywords = OnErrorToolInjectionMiddleware.ExtractErrorKeywords("Error:file;not,found.(x)");
        keywords.Should().Equal(["Error", "file", "found"]);
    }

    [Fact]
    public void ExtractErrorKeywords_TakesAtMostTen() {
        var words = string.Join(" ", Enumerable.Range(0, 15).Select(i => $"word{i}"));
        var keywords = OnErrorToolInjectionMiddleware.ExtractErrorKeywords(words);
        keywords.Should().HaveCount(10);
    }

    [Fact]
    public void ExtractErrorKeywords_NewlineSeparated() {
        var keywords = OnErrorToolInjectionMiddleware.ExtractErrorKeywords("Error\nfile\nfound");
        keywords.Should().Equal(["Error", "file", "found"]);
    }

    // === HasSimilarErrorPattern (internal static) ===

    [Fact]
    public void HasSimilarErrorPattern_TwoCommonKeywords_ReturnsTrue() {
        OnErrorToolInjectionMiddleware.HasSimilarErrorPattern(
            "File not found error", "File not found exception").Should().BeTrue();
    }

    [Fact]
    public void HasSimilarErrorPattern_NoCommonKeywords_ReturnsFalse() {
        OnErrorToolInjectionMiddleware.HasSimilarErrorPattern(
            "File not found", "network timeout occurred").Should().BeFalse();
    }

    [Fact]
    public void HasSimilarErrorPattern_OneCommonKeyword_ReturnsFalse() {
        OnErrorToolInjectionMiddleware.HasSimilarErrorPattern(
            "File error found", "network error timeout").Should().BeFalse();
    }

    [Fact]
    public void HasSimilarErrorPattern_CaseInsensitiveComparison() {
        OnErrorToolInjectionMiddleware.HasSimilarErrorPattern(
            "FILE found error", "file found other").Should().BeTrue();
    }

    [Fact]
    public void HasSimilarErrorPattern_IdenticalErrors_ReturnsTrue() {
        OnErrorToolInjectionMiddleware.HasSimilarErrorPattern(
            "File not found error", "File not found error").Should().BeTrue();
    }

    // === FindRelevantOnErrorTools (internal static) ===

    [Fact]
    public void FindRelevantOnErrorTools_GroupNameMatchesFailedTool_ReturnsOnlyMatching() {
        var matching = new Mock<IToolHandler>();
        matching.SetupGet(h => h.GroupName).Returns("failing_tool");
        var other = new Mock<IToolHandler>();
        other.SetupGet(h => h.GroupName).Returns("other_group");

        var onErrorTools = new Dictionary<string, IToolHandler> {
            ["fix_a"] = matching.Object,
            ["fix_b"] = other.Object
        };

        var result = OnErrorToolInjectionMiddleware.FindRelevantOnErrorTools("failing_tool", onErrorTools);
        result.Should().ContainSingle().Which.Key.Should().Be("fix_a");
    }

    [Fact]
    public void FindRelevantOnErrorTools_NoGroupMatch_ReturnsAll() {
        var t1 = new Mock<IToolHandler>();
        t1.SetupGet(h => h.GroupName).Returns("other");
        var t2 = new Mock<IToolHandler>();
        t2.SetupGet(h => h.GroupName).Returns((string?)null);

        var onErrorTools = new Dictionary<string, IToolHandler> {
            ["a"] = t1.Object,
            ["b"] = t2.Object
        };

        var result = OnErrorToolInjectionMiddleware.FindRelevantOnErrorTools("failing_tool", onErrorTools);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void FindRelevantOnErrorTools_GroupNameCaseInsensitiveMatch() {
        var matching = new Mock<IToolHandler>();
        matching.SetupGet(h => h.GroupName).Returns("Failing_Tool");

        var onErrorTools = new Dictionary<string, IToolHandler> { ["fix"] = matching.Object };

        var result = OnErrorToolInjectionMiddleware.FindRelevantOnErrorTools("FAILING_TOOL", onErrorTools);
        result.Should().ContainSingle();
    }

    [Fact]
    public void FindRelevantOnErrorTools_EmptyInput_ReturnsEmpty() {
        var result = OnErrorToolInjectionMiddleware.FindRelevantOnErrorTools("x", new Dictionary<string, IToolHandler>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindRelevantOnErrorTools_MultipleMatchingGroups_ReturnsAllMatching() {
        var m1 = new Mock<IToolHandler>();
        m1.SetupGet(h => h.GroupName).Returns("failed");
        var m2 = new Mock<IToolHandler>();
        m2.SetupGet(h => h.GroupName).Returns("failed");
        var other = new Mock<IToolHandler>();
        other.SetupGet(h => h.GroupName).Returns("other");

        var onErrorTools = new Dictionary<string, IToolHandler> {
            ["a"] = m1.Object, ["b"] = m2.Object, ["c"] = other.Object
        };

        var result = OnErrorToolInjectionMiddleware.FindRelevantOnErrorTools("failed", onErrorTools);
        result.Should().HaveCount(2);
    }

    // === BuildFailureRateAnalysis (internal static) ===

    [Fact]
    public void BuildFailureRateAnalysis_NullRecord_ReturnsNull() {
        OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", null).Should().BeNull();
    }

    [Fact]
    public void BuildFailureRateAnalysis_ZeroTotal_ReturnsNull() {
        var record = new ToolHealthRecord { ToolName = "t", SuccessCount = 0, FailCount = 0 };
        OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", record).Should().BeNull();
    }

    [Fact]
    public void BuildFailureRateAnalysis_LowFailRate_ReturnsNull() {
        var record = new ToolHealthRecord { ToolName = "t", SuccessCount = 10, FailCount = 1 };
        OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", record).Should().BeNull();
    }

    [Fact]
    public void BuildFailureRateAnalysis_HighFailRate_ReturnsAnalysis() {
        var record = new ToolHealthRecord { ToolName = "t", SuccessCount = 1, FailCount = 4, LastErrorMessage = "boom" };
        var analysis = OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", record);
        analysis.Should().NotBeNull();
        analysis.Should().Contain("历史分析");
        analysis.Should().Contain("上次错误");
        analysis.Should().Contain("boom");
    }

    [Fact]
    public void BuildFailureRateAnalysis_AllFailures_ReturnsAnalysis() {
        var record = new ToolHealthRecord { ToolName = "t", SuccessCount = 0, FailCount = 5 };
        var analysis = OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", record);
        analysis.Should().NotBeNull();
        analysis.Should().Contain("失败率");
    }

    [Fact]
    public void BuildFailureRateAnalysis_NoLastErrorMessage_OmitsLastLine() {
        var record = new ToolHealthRecord { ToolName = "t", SuccessCount = 0, FailCount = 5, LastErrorMessage = null };
        var analysis = OnErrorToolInjectionMiddleware.BuildFailureRateAnalysis("tool", record);
        analysis.Should().NotBeNull();
        analysis.Should().NotContain("上次错误");
    }

    // === BuildPeerAnalysis (internal static) ===

    [Fact]
    public void BuildPeerAnalysis_EmptyEdges_ReturnsNull() {
        var allRecords = new Dictionary<string, ToolHealthRecord>();
        OnErrorToolInjectionMiddleware.BuildPeerAnalysis("tool", allRecords, []).Should().BeNull();
    }

    [Fact]
    public void BuildPeerAnalysis_AllPeersEnabled_ReturnsNull() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["peer"] = new() { ToolName = "peer", IsEnabled = true }
        };
        var edges = new List<ToolHyperedge> {
            new() { Id = "e1", ToolNames = FrozenSet.ToFrozenSet(["tool", "peer"]) }
        };
        OnErrorToolInjectionMiddleware.BuildPeerAnalysis("tool", allRecords, edges).Should().BeNull();
    }

    [Fact]
    public void BuildPeerAnalysis_DisabledPeer_ReturnsAnalysis() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["peer"] = new() { ToolName = "peer", IsEnabled = false, Score = -50 }
        };
        var edges = new List<ToolHyperedge> {
            new() { Id = "e1", ToolNames = FrozenSet.ToFrozenSet(["tool", "peer"]) }
        };
        var analysis = OnErrorToolInjectionMiddleware.BuildPeerAnalysis("tool", allRecords, edges);
        analysis.Should().NotBeNull();
        analysis.Should().Contain("关联工具异常");
        analysis.Should().Contain("peer");
        analysis.Should().Contain("-50");
    }

    [Fact]
    public void BuildPeerAnalysis_SkipsSelfTool() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["tool"] = new() { ToolName = "tool", IsEnabled = false, Score = -100 }
        };
        var edges = new List<ToolHyperedge> {
            new() { Id = "e1", ToolNames = FrozenSet.ToFrozenSet(["tool"]) }
        };
        OnErrorToolInjectionMiddleware.BuildPeerAnalysis("tool", allRecords, edges).Should().BeNull();
    }

    // === BuildSimilarErrorAnalysis (internal static) ===

    [Fact]
    public void BuildSimilarErrorAnalysis_EmptyErrorMsg_ReturnsNull() {
        var allRecords = new Dictionary<string, ToolHealthRecord>();
        OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", "", allRecords).Should().BeNull();
        OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", null, allRecords).Should().BeNull();
    }

    [Fact]
    public void BuildSimilarErrorAnalysis_NoSimilarErrors_ReturnsNull() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["other"] = new() { ToolName = "other", LastErrorMessage = "network timeout occurred" }
        };
        OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", "File not found error", allRecords).Should().BeNull();
    }

    [Fact]
    public void BuildSimilarErrorAnalysis_SimilarErrorExists_ReturnsAnalysis() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["other"] = new() { ToolName = "other", LastErrorMessage = "File not found exception" }
        };
        var analysis = OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", "File not found error", allRecords);
        analysis.Should().NotBeNull();
        analysis.Should().Contain("相似错误模式");
        analysis.Should().Contain("other");
    }

    [Fact]
    public void BuildSimilarErrorAnalysis_SkipsSelfTool() {
        var allRecords = new Dictionary<string, ToolHealthRecord> {
            ["tool"] = new() { ToolName = "tool", LastErrorMessage = "File not found error" }
        };
        OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", "File not found error", allRecords).Should().BeNull();
    }

    [Fact]
    public void BuildSimilarErrorAnalysis_ManySimilar_TakesAtMostThree() {
        var allRecords = new Dictionary<string, ToolHealthRecord>();
        for (var i = 0; i < 5; i++) {
            allRecords[$"tool{i}"] = new ToolHealthRecord {
                ToolName = $"tool{i}", LastErrorMessage = "File not found error"
            };
        }
        var analysis = OnErrorToolInjectionMiddleware.BuildSimilarErrorAnalysis("tool", "File not found error", allRecords);
        analysis.Should().NotBeNull();
        var matchCount = analysis!.Count(c => c == '\n');
        matchCount.Should().BeLessThanOrEqualTo(4);
    }
}
