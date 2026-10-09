// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003

namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// GAP-041-03 频率驱动动态重构图单元测试 — 验证从转移频率重建超图、低频候选移除
/// </summary>
public sealed class ToolHypergraphRebuildTest : IAsyncLifetime {
    private InMemoryFileSystem _fs = null!;
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

    [Fact]
    public async Task RebuildFromTransitionsAsync_AddsDynamicEdges_WhenFrequencyAboveThreshold() {
        for (var i = 0; i < 10; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b");
        for (var i = 0; i < 8; i++)
            await _monitor.RecordTransitionAsync("tool_b", "tool_c");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5);

        var edgesA = scorer.GetEdges("tool_a");
        edgesA.Should().NotBeEmpty();
        edgesA.Any(e => e.Id.StartsWith("freq_")).Should().BeTrue();
    }

    [Fact]
    public async Task RebuildFromTransitionsAsync_DoesNotAddEdges_WhenFrequencyBelowThreshold() {
        await _monitor.RecordTransitionAsync("tool_a", "tool_b");
        await _monitor.RecordTransitionAsync("tool_a", "tool_b");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5);

        var edges = scorer.GetEdges("tool_a");
        edges.Any(e => e.Id.StartsWith("freq_")).Should().BeFalse();
    }

    [Fact]
    public async Task RebuildFromTransitionsAsync_PreservesStaticPresets() {
        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync();

        var edges = scorer.GetEdges(FileToolName.FileRead.ToValue());
        edges.Should().NotBeEmpty();
        edges.Any(e => e.Id == "file_ops").Should().BeTrue();
    }

    [Fact]
    public async Task RebuildFromTransitionsAsync_LowFreqCandidates_TrackedCorrectly() {
        for (var i = 0; i < 10; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5, lowFreqThreshold: 100);

        scorer.LowFreqEdgeCandidates.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RebuildFromTransitionsAsync_NoLowFreqCandidates_WhenAllEdgesHealthy() {
        for (var i = 0; i < 10; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5, lowFreqThreshold: -100);

        scorer.LowFreqEdgeCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLowFreqCandidates_RemovesMarkedEdges() {
        for (var i = 0; i < 10; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5, lowFreqThreshold: 100);

        var candidatesBefore = scorer.LowFreqEdgeCandidates.Count;
        candidatesBefore.Should().BeGreaterThan(0);

        scorer.RemoveLowFreqCandidates();

        scorer.LowFreqEdgeCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLowFreqCandidates_NoOp_WhenNoCandidates() {
        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        scorer.RemoveLowFreqCandidates();
        scorer.LowFreqEdgeCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task RebuildFromTransitionsAsync_DynamicEdgeWeight_ScalesWithFrequency() {
        for (var i = 0; i < 50; i++)
            await _monitor.RecordTransitionAsync("tool_a", "tool_b");

        using var scorer = new ToolHypergraphScorer(monitor: _monitor);
        await scorer.RebuildFromTransitionsAsync(frequencyThreshold: 5);

        var edges = scorer.GetEdges("tool_a");
        var dynamicEdge = edges.FirstOrDefault(e => e.Id.StartsWith("freq_"));
        dynamicEdge.Should().NotBeNull();
        dynamicEdge!.Weight.Should().BeGreaterThan(0.3);
    }
}
