namespace Mcp.Tests;

/// <summary>
/// GitHubRunListSummarizer 单元测试 — 验证 Run 列表 JSON 精简(字段保留 + 异常回退)
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunListSummarizerTests {
    [Fact]
    public void SummarizeRunList_ValidJson_KeepsOnlyKeyFields() {
        var json = """
        {
            "total_count": 2,
            "workflow_runs": [
                {
                    "id": 101,
                    "name": "CI",
                    "head_branch": "main",
                    "status": "completed",
                    "conclusion": "success",
                    "run_number": 5,
                    "created_at": "2026-09-30T10:00:00Z",
                    "html_url": "https://github.com/o/r/actions/runs/101",
                    "display_title": "Push by user",
                    "extra_field": "should_be_dropped"
                }
            ]
        }
        """;

        var result = GitHubRunListSummarizer.SummarizeRunList(json);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("total_count").GetInt32().Should().Be(2);
        var run = doc.RootElement.GetProperty("workflow_runs").EnumerateArray().First();
        run.GetProperty("id").GetInt64().Should().Be(101);
        run.GetProperty("name").GetString().Should().Be("CI");
        run.GetProperty("head_branch").GetString().Should().Be("main");
        run.GetProperty("status").GetString().Should().Be("completed");
        run.GetProperty("conclusion").GetString().Should().Be("success");
        run.GetProperty("run_number").GetInt32().Should().Be(5);
        run.GetProperty("html_url").GetString().Should().Be("https://github.com/o/r/actions/runs/101");
        run.GetProperty("display_title").GetString().Should().Be("Push by user");
        run.TryGetProperty("extra_field", out _).Should().BeFalse();
    }

    [Fact]
    public void SummarizeRunList_MissingFields_OmitsThem() {
        var json = """{"total_count": 1, "workflow_runs": [{"id": 1}]}""";

        var result = GitHubRunListSummarizer.SummarizeRunList(json);

        using var doc = JsonDocument.Parse(result);
        var run = doc.RootElement.GetProperty("workflow_runs").EnumerateArray().First();
        run.GetProperty("id").GetInt64().Should().Be(1);
        run.TryGetProperty("name", out _).Should().BeFalse();
    }

    [Fact]
    public void SummarizeRunList_NoWorkflowRuns_ReturnsOriginal() {
        var json = """{"total_count": 0}""";
        var result = GitHubRunListSummarizer.SummarizeRunList(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunList_RootNotObject_ReturnsOriginal() {
        var json = """[1, 2, 3]""";
        var result = GitHubRunListSummarizer.SummarizeRunList(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunList_WorkflowRunsNotArray_ReturnsOriginal() {
        var json = """{"workflow_runs": "not_array"}""";
        var result = GitHubRunListSummarizer.SummarizeRunList(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunList_InvalidJson_ReturnsOriginal() {
        var json = "not valid json {{{";
        var result = GitHubRunListSummarizer.SummarizeRunList(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunList_EmptyRunsArray_ReturnsEmptyArray() {
        var json = """{"total_count": 0, "workflow_runs": []}""";
        var result = GitHubRunListSummarizer.SummarizeRunList(json);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("total_count").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("workflow_runs").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void SummarizeRunListBrief_ValidJson_ProducesTable() {
        var json = """
        {
            "total_count": 2,
            "workflow_runs": [
                {
                    "id": 101,
                    "name": "CI",
                    "head_branch": "main",
                    "status": "completed",
                    "conclusion": "success",
                    "run_number": 5,
                    "created_at": "2026-09-30T10:00:00Z",
                    "updated_at": "2026-09-30T10:02:30Z",
                    "event": "push",
                    "display_title": "Push by user"
                },
                {
                    "id": 102,
                    "name": "Daily Test",
                    "head_branch": "main",
                    "status": "completed",
                    "conclusion": "failure",
                    "run_number": 6,
                    "created_at": "2026-09-30T11:00:00Z",
                    "updated_at": "2026-09-30T11:05:00Z",
                    "event": "schedule",
                    "display_title": "Daily Test #6"
                }
            ]
        }
        """;

        var result = GitHubRunListSummarizer.SummarizeRunListBrief(json);

        result.Should().Contain("共 2 个 run");
        result.Should().Contain("✓");
        result.Should().Contain("✗");
        result.Should().Contain("101");
        result.Should().Contain("102");
        result.Should().Contain("CI");
        result.Should().Contain("Daily Test");
        result.Should().Contain("2m30s");
        result.Should().Contain("5m0s");
    }

    [Fact]
    public void SummarizeRunListBrief_NoWorkflowRuns_ReturnsOriginal() {
        var json = """{"total_count": 0}""";
        var result = GitHubRunListSummarizer.SummarizeRunListBrief(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunListBrief_InvalidJson_ReturnsOriginal() {
        var json = "not valid json {{{";
        var result = GitHubRunListSummarizer.SummarizeRunListBrief(json);
        result.Should().Be(json);
    }
}
