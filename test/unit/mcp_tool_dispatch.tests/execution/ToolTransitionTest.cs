// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复

namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// GAP-041-01 工具转移频率记录 + refine 角色单元测试
/// </summary>
public sealed class ToolTransitionTest : IAsyncLifetime {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    private InMemoryFileSystem _fs = null!;
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    private ToolHealthMonitor _monitor = null!;

    public Task InitializeAsync() {
        _fs = new InMemoryFileSystem();
        _monitor = new ToolHealthMonitor(_fs);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        _monitor.DisposeSafe();
        return Task.CompletedTask;
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RecordTransitionAsync_RecordsFrequency() {
        await _monitor.RecordTransitionAsync("tool_a", "tool_b"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("tool_a", "tool_b"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("tool_a", "tool_b"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("tool_a", "tool_c"); await _monitor.FlushAsync();

        var record = await _monitor.GetRecordAsync("tool_a"); ArgumentNullException.ThrowIfNull(record);
        record!.NextToolFrequency["tool_b"].Should().Be(3);
        record.NextToolFrequency["tool_c"].Should().Be(1);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RecordTransitionAsync_DifferentTools_TrackedSeparately() {
        await _monitor.RecordTransitionAsync("tool_a", "tool_b"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("tool_b", "tool_c"); await _monitor.FlushAsync();

        var recordA = await _monitor.GetRecordAsync("tool_a"); ArgumentNullException.ThrowIfNull(recordA);
        var recordB = await _monitor.GetRecordAsync("tool_b"); ArgumentNullException.ThrowIfNull(recordB);
        recordA!.NextToolFrequency["tool_b"].Should().Be(1);
        recordB!.NextToolFrequency["tool_c"].Should().Be(1);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RecordTransitionAsync_SameTool_NoTransitionRecorded() {
        await _monitor.RecordSuccessAsync("tool_a"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("tool_a", "tool_a"); await _monitor.FlushAsync();

        var record = await _monitor.GetRecordAsync("tool_a"); ArgumentNullException.ThrowIfNull(record);
        record!.NextToolFrequency.Should().BeEmpty();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task GetChainRecommendations_FrequencyBased_WhenAboveThreshold() {
        for (var i = 0; i < 5; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b"); await _monitor.FlushAsync();
        for (var i = 0; i < 3; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_c"); await _monitor.FlushAsync();

        using var scorer = new ToolHypergraphScorer();
        var record = await _monitor.GetRecordAsync("tool_a"); ArgumentNullException.ThrowIfNull(record);
        var chain = scorer.GetChainRecommendations("tool_a", record, frequencyThreshold: 3);

        chain.Should().NotBeNull();
        chain![0].Should().Be("tool_b");
        chain[1].Should().Be("tool_c");
    }

    [Fact]
    public async Task GetChainRecommendations_FallsBackToStatic_WhenFrequencyBelowThreshold() {
        await _monitor.RecordTransitionAsync("file_read", "file_edit"); await _monitor.FlushAsync();
        await _monitor.RecordTransitionAsync("file_read", "file_edit"); await _monitor.FlushAsync();

        using var scorer = new ToolHypergraphScorer();
        var record = await _monitor.GetRecordAsync("file_read"); ArgumentNullException.ThrowIfNull(record);
        var chain = scorer.GetChainRecommendations(FileToolName.FileRead.ToValue(), record, frequencyThreshold: 3);

        chain.Should().NotBeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void GetChainRecommendations_FallsBackToStatic_WhenNoFrequencyData() {
        using var scorer = new ToolHypergraphScorer();
        var chain = scorer.GetChainRecommendations(FileToolName.FileRead.ToValue(), null);

        chain.Should().NotBeNull();
        chain![0].Should().Be(FileToolName.FileEdit.ToValue());
    }
}

/// <summary>
/// ToolTransitionModel 单元测试 — 验证三角色 primary/fallback/refine
/// </summary>
public sealed class ToolTransitionModelTest {
    [Fact]
    public void AddOrUpdate_StoresCondition() {
        var model = new ToolTransitionModel();
        var condition = new ToolTransitionCondition {
            Id = "data_large",
            Primary = "gh_run_view",
            Fallback = "gh_run_list",
            Refine = "gh_run_view_filter_error"
        };
        model.AddOrUpdate(condition);
        model.Conditions.Should().ContainKey("data_large");
    }

    [Fact]
    public void GetRecommendation_ReturnsCorrectRole() {
        var model = new ToolTransitionModel();
        model.AddOrUpdate(new ToolTransitionCondition {
            Id = "ci_fail",
            Primary = "gh_run_view",
            Fallback = "gh_pr_checks",
            Refine = "gh_run_view_filter_error"
        });

        model.TryGetRecommendation("ci_fail", ToolTransitionRole.Primary, out var primary).Should().BeTrue();
        primary.Should().Be("gh_run_view");
        model.TryGetRecommendation("ci_fail", ToolTransitionRole.Fallback, out var fallback).Should().BeTrue();
        fallback.Should().Be("gh_pr_checks");
        model.TryGetRecommendation("ci_fail", ToolTransitionRole.Refine, out var refine).Should().BeTrue();
        refine.Should().Be("gh_run_view_filter_error");
    }

    [Fact]
    public void GetRecommendation_ReturnsNull_WhenConditionNotFound() {
        var model = new ToolTransitionModel();
        model.TryGetRecommendation("nonexistent", ToolTransitionRole.Primary, out _).Should().BeFalse();
    }

    [Fact]
    public void GetAllRecommendations_ReturnsAllNonNullRoles() {
        var model = new ToolTransitionModel();
        model.AddOrUpdate(new ToolTransitionCondition {
            Id = "partial",
            Primary = "tool_a",
            Fallback = null,
            Refine = "tool_c"
        });

        var all = model.GetAllRecommendations("partial");
        all.Should().HaveCount(2);
        all.Should().Contain((ToolTransitionRole.Primary, "tool_a"));
        all.Should().Contain((ToolTransitionRole.Refine, "tool_c"));
    }
}

/// <summary>
/// ToolRefineRecommender 单元测试 — 验证数据过大时推荐精炼工具
/// </summary>
public sealed class ToolRefineRecommenderTest {
    [Fact]
    public void RecommendRefine_ReturnsRefineTool_WhenOutputExceedsThreshold() {
        var recommender = new ToolRefineRecommender();
        recommender.AddRule(new ToolRefineRule {
            SourceTool = "gh_run_view",
            RefineTool = "gh_run_view_filter_error",
            OutputSizeThreshold = 10_000
        });

        recommender.TryRecommendRefine("gh_run_view", 15_000, out var result1).Should().BeTrue();
        result1.Should().Be("gh_run_view_filter_error");
    }

    [Fact]
    public void RecommendRefine_ReturnsNull_WhenOutputBelowThreshold() {
        var recommender = new ToolRefineRecommender();
        recommender.AddRule(new ToolRefineRule {
            SourceTool = "gh_run_view",
            RefineTool = "gh_run_view_filter_error",
            OutputSizeThreshold = 10_000
        });

        recommender.TryRecommendRefine("gh_run_view", 5_000, out _).Should().BeFalse();
    }

    [Fact]
    public void RecommendRefine_ReturnsNull_WhenNoMatchingRule() {
        var recommender = new ToolRefineRecommender();
        recommender.AddRule(new ToolRefineRule {
            SourceTool = "gh_run_view",
            RefineTool = "gh_run_view_filter_error",
            OutputSizeThreshold = 10_000
        });

        recommender.TryRecommendRefine("other_tool", 100_000, out _).Should().BeFalse();
    }

    [Fact]
    public void RecommendRefine_MultipleRules_FirstMatchingRuleWins() {
        var recommender = new ToolRefineRecommender();
        recommender.AddRule(new ToolRefineRule {
            SourceTool = "gh_run_view",
            RefineTool = "filter_error",
            OutputSizeThreshold = 10_000
        });
        recommender.AddRule(new ToolRefineRule {
            SourceTool = "gh_run_view",
            RefineTool = "filter_warning",
            OutputSizeThreshold = 50_000
        });

        recommender.TryRecommendRefine("gh_run_view", 15_000, out var r1).Should().BeTrue();
        r1.Should().Be("filter_error");
        recommender.TryRecommendRefine("gh_run_view", 60_000, out var r2).Should().BeTrue();
        r2.Should().Be("filter_error");
    }
}
