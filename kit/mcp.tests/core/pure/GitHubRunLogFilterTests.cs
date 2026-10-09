namespace Mcp.Tests;

/// <summary>
/// GitHubRunLogFilter 单元测试 — 验证 GitHub Run 日志过滤(标记过滤/分页截断/栈帧检测/优先级排序)
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunLogFilterTests {
    [Fact]
    public void GetFilterMarkers_Error_ContainsOnlyError() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.Error);
        markers.Should().Contain("##[error]");
        markers.Should().NotContain("##[warning]");
        markers.Should().NotContain("##[command]");
    }

    [Fact]
    public void GetFilterMarkers_Warning_ContainsOnlyWarning() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.Warning);
        markers.Should().Contain("##[warning]");
        markers.Should().NotContain("##[error]");
        markers.Should().NotContain("##[command]");
    }

    [Fact]
    public void GetFilterMarkers_Info_ContainsAllThree() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.Info);
        markers.Should().Contain("##[error]");
        markers.Should().Contain("##[warning]");
        markers.Should().Contain("##[command]");
    }

    [Fact]
    public void GetFilterMarkers_All_ContainsAllMarkers() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.All);
        markers.Should().Contain("##[error]");
        markers.Should().Contain("##[warning]");
        markers.Should().Contain("##[command]");
        markers.Should().Contain("[FAIL]");
        markers.Should().Contain("Exception:");
    }

    [Fact]
    public void GetFilterMarkers_Failed_ContainsFailMarkers() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.Failed);
        markers.Should().Contain("[FAIL]");
        markers.Should().Contain("  Failed ");
        markers.Should().NotContain("##[error]");
    }

    [Fact]
    public void GetFilterMarkers_Combined_ErrorAndFailed() {
        var markers = GitHubRunLogFilter.GetFilterMarkers(GitHubLogFilter.Error | GitHubLogFilter.Failed);
        markers.Should().Contain("##[error]");
        markers.Should().Contain("[FAIL]");
    }

    [Theory]
    [InlineData("error", GitHubLogFilter.Error)]
    [InlineData("warning", GitHubLogFilter.Warning)]
    [InlineData("info", GitHubLogFilter.Info)]
    [InlineData("all", GitHubLogFilter.All)]
    [InlineData("failed", GitHubLogFilter.Failed)]
    [InlineData("exception", GitHubLogFilter.Exception)]
    public void TryParseLogFilter_ValidStrings_ReturnsTrue(string input, GitHubLogFilter expected) {
        var ok = GitHubRunLogFilter.TryParseLogFilter(input, out var result);
        ok.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("error,failed", GitHubLogFilter.Error | GitHubLogFilter.Failed)]
    [InlineData("error,exception", GitHubLogFilter.Error | GitHubLogFilter.Exception)]
    [InlineData("error,warning,failed", GitHubLogFilter.Error | GitHubLogFilter.Warning | GitHubLogFilter.Failed)]
    public void TryParseLogFilter_Combinated_ReturnsBitwiseOr(string input, GitHubLogFilter expected) {
        var ok = GitHubRunLogFilter.TryParseLogFilter(input, out var result);
        ok.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyz")]
    public void TryParseLogFilter_InvalidStrings_ReturnsFalse(string? input) {
        var ok = GitHubRunLogFilter.TryParseLogFilter(input, out var result);
        ok.Should().BeFalse();
        result.Should().Be(GitHubLogFilter.None);
    }

    [Fact]
    public void TryParseLogFilter_CaseInsensitive_ParsesSuccessfully() {
        // FromValue 大小写不敏感: "ERROR" 匹配 [EnumValue("error")]
        var ok = GitHubRunLogFilter.TryParseLogFilter("ERROR", out var result);
        ok.Should().BeTrue();
        result.Should().Be(GitHubLogFilter.Error);
    }

    [Fact]
    public void HasNoStackTrace_NoExitCode_ReturnsFalse() {
        GitHubRunLogFilter.HasNoStackTrace("just some log line").Should().BeFalse();
    }

    [Fact]
    public void HasNoStackTrace_ExitCodeOnly_NoStack_ReturnsTrue() {
        GitHubRunLogFilter.HasNoStackTrace("Process completed with exit code 1.").Should().BeTrue();
    }

    [Fact]
    public void HasNoStackTrace_WithException_ReturnsFalse() {
        GitHubRunLogFilter.HasNoStackTrace("Process completed with exit code 1. Exception: boom").Should().BeFalse();
    }

    [Fact]
    public void HasNoStackTrace_WithStackTrace_ReturnsFalse() {
        GitHubRunLogFilter.HasNoStackTrace("Process completed with exit code 1. StackTrace: at Foo()").Should().BeFalse();
    }

    [Fact]
    public void HasNoStackTrace_WithAtFrame_ReturnsFalse() {
        GitHubRunLogFilter.HasNoStackTrace("Process completed with exit code 1.\n  at Foo.Bar()").Should().BeFalse();
    }

    [Fact]
    public void HasNoStackTrace_WithLowerCaseStackTrace_ReturnsFalse() {
        GitHubRunLogFilter.HasNoStackTrace("Process completed with exit code 1. see stack trace below").Should().BeFalse();
    }

    [Theory]
    [InlineData("error", 0)]
    [InlineData("warning", 1)]
    [InlineData("command", 2)]
    [InlineData("group", 3)]
    [InlineData("normal", 4)]
    [InlineData("unknown", 5)]
    public void SectionOrder_ReturnsExpectedPriority(string type, int expected) {
        GitHubRunLogFilter.SectionOrder(type).Should().Be(expected);
    }

    [Fact]
    public void SkipAndTruncate_EmptyLines_ReturnsEmpty() {
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate([], 10, 0);
        text.Should().BeEmpty();
        hasMore.Should().BeFalse();
    }

    [Fact]
    public void SkipAndTruncate_SkipAllLines_ReturnsSkipMessage() {
        var lines = new List<string> { "a", "b", "c" };
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate(lines, 10, 3);
        text.Should().Contain("已跳过全部");
        text.Should().Contain("skip_lines=3");
        hasMore.Should().BeFalse();
    }

    [Fact]
    public void SkipAndTruncate_SkipBeyondCount_ReturnsSkipMessage() {
        var lines = new List<string> { "a", "b" };
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate(lines, 10, 5);
        text.Should().Contain("已跳过全部");
        hasMore.Should().BeFalse();
    }

    [Fact]
    public void SkipAndTruncate_TakeAll_NoMore() {
        var lines = new List<string> { "a", "b", "c" };
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate(lines, 10, 0);
        text.Should().Contain("a");
        text.Should().Contain("b");
        text.Should().Contain("c");
        hasMore.Should().BeFalse();
    }

    [Fact]
    public void SkipAndTruncate_TakePartial_HasMoreWithContinueHint() {
        var lines = new List<string> { "a", "b", "c", "d" };
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate(lines, 2, 0);
        text.Should().Contain("a");
        text.Should().Contain("b");
        text.Should().NotContain("c\nc");
        hasMore.Should().BeTrue();
        text.Should().Contain("skip_lines=2");
        text.Should().Contain("续读");
    }

    [Fact]
    public void SkipAndTruncate_WithSkip_TakesFromOffset() {
        var lines = new List<string> { "a", "b", "c", "d", "e" };
        var (text, hasMore) = GitHubRunLogFilter.SkipAndTruncate(lines, 2, 1);
        text.Should().Contain("b");
        text.Should().Contain("c");
        hasMore.Should().BeTrue();
    }

    [Fact]
    public void SkipAndTruncate_AddsLineNumberPrefix() {
        var lines = new List<string> { "first", "second", "third" };
        var (text, _) = GitHubRunLogFilter.SkipAndTruncate(lines, 10, 0);
        text.Should().Contain("1\tfirst");
        text.Should().Contain("2\tsecond");
        text.Should().Contain("3\tthird");
    }

    [Fact]
    public void SkipAndTruncate_WithSkip_LineNumbersAreGlobal() {
        var lines = new List<string> { "a", "b", "c", "d", "e" };
        var (text, _) = GitHubRunLogFilter.SkipAndTruncate(lines, 2, 2);
        text.Should().Contain("3\tc");
        text.Should().Contain("4\td");
    }

    [Fact]
    public void ParseJobIds_NullOrEmpty_ReturnsEmpty() {
        GitHubRunLogFilter.ParseJobIds(null).Should().BeEmpty();
        GitHubRunLogFilter.ParseJobIds("").Should().BeEmpty();
        GitHubRunLogFilter.ParseJobIds("   ").Should().BeEmpty();
    }

    [Fact]
    public void ParseJobIds_SingleId_ReturnsOne() {
        GitHubRunLogFilter.ParseJobIds("123").Should().Equal(123L);
    }

    [Fact]
    public void ParseJobIds_MultipleIds_ReturnsAll() {
        GitHubRunLogFilter.ParseJobIds("123,456,789").Should().Equal(123L, 456L, 789L);
    }

    [Fact]
    public void ParseJobIds_WithSpaces_TrimsAndParses() {
        GitHubRunLogFilter.ParseJobIds(" 123 , 456 ").Should().Equal(123L, 456L);
    }

    [Fact]
    public void ParseJobIds_WithInvalidValues_SkipsInvalid() {
        GitHubRunLogFilter.ParseJobIds("123,abc,456").Should().Equal(123L, 456L);
    }

    [Fact]
    public void ParseJobIds_AllInvalid_ReturnsEmpty() {
        GitHubRunLogFilter.ParseJobIds("abc,xyz").Should().BeEmpty();
    }
}
