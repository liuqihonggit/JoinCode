namespace Mcp.Tests;

/// <summary>
/// GitHubRunViewSummarizer 单元测试 — 验证 run 详情 JSON 转人类可读文本
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunViewSummarizerTests {
    [Fact]
    public void SummarizeRunView_ValidJson_ProducesHumanReadableText() {
        var json = """
        {
            "id": 37634292566,
            "name": "Daily Command Test",
            "head_branch": "main",
            "head_sha": "abc123def456",
            "status": "completed",
            "conclusion": "failure",
            "run_number": 32,
            "event": "schedule",
            "created_at": "2026-10-08T00:00:00Z",
            "updated_at": "2026-10-08T00:05:30Z",
            "html_url": "https://github.com/o/r/actions/runs/37634292566",
            "display_title": "Daily Command Test #32: Scheduled"
        }
        """;

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().Contain("✗");
        result.Should().Contain("Daily Command Test #32: Scheduled");
        result.Should().Contain("· 32");
        result.Should().Contain("Event: schedule");
        result.Should().Contain("Branch: main");
        result.Should().Contain("SHA: abc123d");
        result.Should().Contain("Status: completed");
        result.Should().Contain("Conclusion: failure");
        result.Should().Contain("ID: 37634292566");
        result.Should().Contain("Elapsed: 5m30s");
        result.Should().Contain("URL: https://github.com/o/r/actions/runs/37634292566");
    }

    [Fact]
    public void SummarizeRunView_SuccessRun_ShowsCheckmark() {
        var json = """{"status":"completed","conclusion":"success","display_title":"CI #31","run_number":31,"id":123,"event":"push","head_branch":"main","head_sha":"abcdefg","created_at":"2026-10-08T10:00:00Z","updated_at":"2026-10-08T10:02:15Z"}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().StartWith("✓");
        result.Should().Contain("CI #31 · 31");
        result.Should().Contain("Elapsed: 2m15s");
    }

    [Fact]
    public void SummarizeRunView_InProgress_ShowsStar() {
        var json = """{"status":"in_progress","display_title":"Running","id":999}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().StartWith("*");
        result.Should().Contain("Running");
        result.Should().Contain("Status: in_progress");
    }

    [Fact]
    public void SummarizeRunView_Queued_ShowsEllipsis() {
        var json = """{"status":"queued","display_title":"Queued","id":888}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().StartWith("…");
    }

    [Fact]
    public void SummarizeRunView_MinimalJson_OnlyIdAndTitle() {
        var json = """{"id": 42, "display_title": "Test"}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().Contain("Test");
        result.Should().Contain("ID: 42");
    }

    [Fact]
    public void SummarizeRunView_RootNotObject_ReturnsOriginal() {
        var json = """[1, 2, 3]""";
        var result = GitHubRunViewSummarizer.SummarizeRunView(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunView_InvalidJson_ReturnsOriginal() {
        var json = "not valid json {{{";
        var result = GitHubRunViewSummarizer.SummarizeRunView(json);
        result.Should().Be(json);
    }

    [Fact]
    public void SummarizeRunView_CancelledRun_ShowsCancelledSymbol() {
        var json = """{"status":"completed","conclusion":"cancelled","display_title":"Cancelled run","id":555}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().StartWith("⊘");
    }

    [Fact]
    public void SummarizeRunView_FallsBackToName_WhenNoDisplayTitle() {
        var json = """{"name":"Workflow X","id":1,"status":"completed","conclusion":"success"}""";

        var result = GitHubRunViewSummarizer.SummarizeRunView(json);

        result.Should().Contain("Workflow X");
    }
}

/// <summary>
/// GitHubRunFormatHelper 单元测试
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunFormatHelperTests {
    [Theory]
    [InlineData("completed", "success", "✓")]
    [InlineData("completed", "failure", "✗")]
    [InlineData("completed", "cancelled", "⊘")]
    [InlineData("completed", "timed_out", "⏱")]
    [InlineData("completed", "skipped", "→")]
    [InlineData("in_progress", null, "*")]
    [InlineData("queued", null, "…")]
    [InlineData("completed", "neutral", "-")]
    [InlineData(null, null, "-")]
    public void GetStatusSymbol_MapsCorrectly(string? status, string? conclusion, string expected) {
        GitHubRunFormatHelper.GetStatusSymbol(status, conclusion).Should().Be(expected);
    }

    [Fact]
    public void FormatElapsed_ValidTimes_ReturnsFormatted() {
        var result = GitHubRunFormatHelper.FormatElapsed("2026-10-08T00:00:00Z", "2026-10-08T00:05:30Z");
        result.Should().Be("5m30s");
    }

    [Fact]
    public void FormatElapsed_UnderMinute_ReturnsSeconds() {
        var result = GitHubRunFormatHelper.FormatElapsed("2026-10-08T00:00:00Z", "2026-10-08T00:00:45Z");
        result.Should().Be("45s");
    }

    [Fact]
    public void FormatElapsed_OverHour_ReturnsHoursAndMinutes() {
        var result = GitHubRunFormatHelper.FormatElapsed("2026-10-08T00:00:00Z", "2026-10-08T01:30:00Z");
        result.Should().Be("1h30m");
    }

    [Fact]
    public void FormatElapsed_NullInput_ReturnsNull() {
        GitHubRunFormatHelper.FormatElapsed(null, "2026-10-08T00:00:00Z").Should().BeNull();
        GitHubRunFormatHelper.FormatElapsed("2026-10-08T00:00:00Z", null).Should().BeNull();
    }

    [Fact]
    public void FormatElapsed_InvalidInput_ReturnsNull() {
        GitHubRunFormatHelper.FormatElapsed("not-a-date", "also-not-a-date").Should().BeNull();
    }
}
