namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// GAP-041-02 工具锚点索引单元测试 — 验证锚点注册、向量匹配、余弦相似度
/// </summary>
public sealed class ToolAnchorIndexTest {
    [Fact]
    public void Register_AddsEntry() {
        var index = new ToolAnchorIndex();
        index.Register("gh_run_view", "CI 失败", "job 日志", "run 状态");
        index.Count.Should().Be(1);
    }

    [Fact]
    public void Seal_BuildsVocabularyAndVectors() {
        var index = new ToolAnchorIndex();
        index.Register("gh_run_view", "CI 失败", "job 日志");
        index.Register("gh_pr_checks", "PR 检查", "CI 状态");
        index.Seal();
        index.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Register_AfterSeal_Throws() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "anchor1");
        index.Seal();
        var act = () => index.Register("tool_b", "anchor2");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_ReturnsEmpty_WhenNotSealed() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "anchor1");
        var results = index.Match("anchor1");
        results.Should().BeEmpty();
    }

    [Fact]
    public void Match_ReturnsEmpty_WhenNoEntries() {
        var index = new ToolAnchorIndex();
        index.Seal();
        var results = index.Match("query");
        results.Should().BeEmpty();
    }

    [Fact]
    public void Match_ReturnsMatchingTool_WhenQueryMatchesAnchor() {
        var index = new ToolAnchorIndex();
        index.Register("gh_run_view", "CI 失败", "job 日志", "run 状态", "workflow 排错");
        index.Seal();

        var results = index.Match("CI 失败了怎么排查");

        results.Should().NotBeEmpty();
        results[0].ToolName.Should().Be("gh_run_view");
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Match_ReturnsSortedByScoreDescending() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "CI 失败", "job 日志");
        index.Register("tool_b", "CI 失败", "job 日志", "run 状态", "workflow 排错", "构建失败");
        index.Seal();

        var results = index.Match("CI 失败 job 日志 run 状态 workflow 排错 构建失败");

        results.Should().NotBeEmpty();
        if (results.Count > 1) {
            results[0].Score.Should().BeGreaterThanOrEqualTo(results[1].Score);
        }
    }

    [Fact]
    public void Match_RespectsTopK() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "common anchor");
        index.Register("tool_b", "common anchor");
        index.Register("tool_c", "common anchor");
        index.Seal();

        var results = index.Match("common anchor", topK: 2);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Match_RespectsThreshold() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "CI 失败", "job 日志");
        index.Seal();

        var results = index.Match("完全不同的查询内容", threshold: 0.99f);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Match_HitAnchors_ContainsMatchedKeywords() {
        var index = new ToolAnchorIndex();
        index.Register("gh_run_view", "CI 失败", "job 日志", "run 状态");
        index.Seal();

        var results = index.Match("CI 失败 job 日志");

        results.Should().NotBeEmpty();
        results[0].HitAnchors.Should().Contain("CI 失败");
        results[0].HitAnchors.Should().Contain("job 日志");
    }

    [Fact]
    public void Match_ReturnsEmpty_WhenQueryIsEmpty() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "anchor1");
        index.Seal();

        var results = index.Match("");

        results.Should().BeEmpty();
    }

    [Fact]
    public void Match_MultipleTools_ReturnsBestMatch() {
        var index = new ToolAnchorIndex();
        index.Register("gh_run_view", "CI 失败", "job 日志", "run 状态", "workflow 排错");
        index.Register("gh_pr_checks", "PR 检查", "CI 状态", "check 结果");
        index.Seal();

        var results = index.Match("CI 失败了需要看 job 日志");

        results.Should().NotBeEmpty();
        results[0].ToolName.Should().Be("gh_run_view");
    }

    [Fact]
    public void Seal_CalledTwice_IsIdempotent() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "anchor1");
        index.Seal();
        index.Seal();
        index.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Entries_ReturnsAllRegisteredEntries() {
        var index = new ToolAnchorIndex();
        index.Register("tool_a", "anchor1", "anchor2");
        index.Register("tool_b", "anchor3");

        index.Entries.Should().HaveCount(2);
        index.Entries[0].ToolName.Should().Be("tool_a");
        index.Entries[1].ToolName.Should().Be("tool_b");
    }
}
