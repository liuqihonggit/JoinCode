namespace Mcp.Tests;

/// <summary>
/// GitHubRunLogFilterRunner 拆分出的 internal 子方法确定性测试 —
/// ProcessLogLine / DeduplicateFailures / FormatFailuresRustStyle (纯计算,无 IO/无异步/无时序)
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunLogFilterRunnerTests {

    [Fact]
    public void ProcessLogLine_NormalFailedMarker_EntersInFailedTest() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = null;
        var state = GitHubRunLogFilterRunner.LogParseState.Normal;

        GitHubRunLogFilterRunner.ProcessLogLine("  Failed MyTest [FAIL]", 5, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InFailedTest);
        failures.Should().HaveCount(1);
        failures[0].StartLine.Should().Be(5);
        failures[0].TestLine.Should().Be("  Failed MyTest [FAIL]");
        current.Should().NotBeNull();
    }

    [Fact]
    public void ProcessLogLine_NormalXUnitFailMarker_EntersInFailedTest() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = null;
        var state = GitHubRunLogFilterRunner.LogParseState.Normal;

        GitHubRunLogFilterRunner.ProcessLogLine("[xUnit.net 00:00:00.81] MyTest [FAIL]", 3, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InFailedTest);
        failures.Should().HaveCount(1);
        failures[0].StartLine.Should().Be(3);
    }

    [Fact]
    public void ProcessLogLine_NormalErrorMarker_AddsErrorMarkerAndStaysNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = null;
        var state = GitHubRunLogFilterRunner.LogParseState.Normal;

        GitHubRunLogFilterRunner.ProcessLogLine("##[error] something broke", 7, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        failures.Should().HaveCount(1);
        failures[0].IsErrorMarker.Should().BeTrue();
        failures[0].StartLine.Should().Be(7);
    }

    [Fact]
    public void ProcessLogLine_NormalPlainLine_NoChange() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = null;
        var state = GitHubRunLogFilterRunner.LogParseState.Normal;

        GitHubRunLogFilterRunner.ProcessLogLine("just a log line", 1, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        failures.Should().BeEmpty();
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InFailedTestErrorMessage_EntersInErrorMessage() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InFailedTest;

        GitHubRunLogFilterRunner.ProcessLogLine("  Error Message:", 2, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InErrorMessage);
    }

    [Fact]
    public void ProcessLogLine_InFailedTestStackTrace_EntersInStackTrace() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InFailedTest;

        GitHubRunLogFilterRunner.ProcessLogLine("  Stack Trace:", 2, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InStackTrace);
    }

    [Fact]
    public void ProcessLogLine_InFailedTestPassedLine_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InFailedTest;

        GitHubRunLogFilterRunner.ProcessLogLine("  Passed OtherTest [1 ms]", 2, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InFailedTestFailLine_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InFailedTest;

        GitHubRunLogFilterRunner.ProcessLogLine("  Failed NextTest [FAIL]", 2, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InFailedTestPassMarker_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InFailedTest;

        GitHubRunLogFilterRunner.ProcessLogLine("[xUnit.net] OtherTest [PASS]", 2, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InErrorMessageContent_AppendsToErrorMessageLines() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InErrorMessage;

        GitHubRunLogFilterRunner.ProcessLogLine("  Expected: 1 but was: 2", 3, ref state, ref current, failures);

        current!.ErrorMessageLines.Should().Contain("Expected: 1 but was: 2");
        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InErrorMessage);
    }

    [Fact]
    public void ProcessLogLine_InErrorMessageStackTrace_EntersInStackTrace() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InErrorMessage;

        GitHubRunLogFilterRunner.ProcessLogLine("  Stack Trace:", 4, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InStackTrace);
    }

    [Fact]
    public void ProcessLogLine_InErrorMessagePassedLine_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InErrorMessage;

        GitHubRunLogFilterRunner.ProcessLogLine("  Passed X [1 ms]", 5, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InStackTraceContent_AppendsToStackTraceLines() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InStackTrace;

        GitHubRunLogFilterRunner.ProcessLogLine("  at Foo.Bar() in line 10", 5, ref state, ref current, failures);

        current!.StackTraceLines.Should().Contain("  at Foo.Bar() in line 10");
        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.InStackTrace);
    }

    [Fact]
    public void ProcessLogLine_InStackTraceEndOfTrace_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InStackTrace;

        GitHubRunLogFilterRunner.ProcessLogLine("--- End of stack trace ---", 6, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void ProcessLogLine_InStackTracePassedLine_ReturnsToNormal() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo>();
        GitHubRunLogFilterRunner.TestFailureInfo? current = new() { StartLine = 1, TestLine = "  Failed T [FAIL]" };
        failures.Add(current);
        var state = GitHubRunLogFilterRunner.LogParseState.InStackTrace;

        GitHubRunLogFilterRunner.ProcessLogLine("  Passed X [1 ms]", 7, ref state, ref current, failures);

        state.Should().Be(GitHubRunLogFilterRunner.LogParseState.Normal);
        current.Should().BeNull();
    }

    [Fact]
    public void DeduplicateFailures_Empty_ReturnsEmpty() {
        var result = GitHubRunLogFilterRunner.DeduplicateFailures([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void DeduplicateFailures_AllErrorMarkers_KeepsAll() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo> {
            new() { StartLine = 1, TestLine = "##[error] a", IsErrorMarker = true },
            new() { StartLine = 2, TestLine = "##[error] b", IsErrorMarker = true }
        };

        var result = GitHubRunLogFilterRunner.DeduplicateFailures(failures);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateFailures_SameTestName_KeepsMoreErrorMessage() {
        var less = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 1, TestLine = "  Failed MyTest [FAIL]" };
        var more = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 5, TestLine = "[xUnit.net] MyTest [FAIL]" };
        more.ErrorMessageLines.Add("detailed error");

        var result = GitHubRunLogFilterRunner.DeduplicateFailures([less, more]);

        result.Should().HaveCount(1);
        result[0].StartLine.Should().Be(5);
        result[0].ErrorMessageLines.Should().HaveCount(1);
    }

    [Fact]
    public void DeduplicateFailures_SameTestName_KeepsFirstWhenEqualErrorMessage() {
        var first = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 1, TestLine = "  Failed MyTest [FAIL]" };
        first.ErrorMessageLines.Add("err");
        var second = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 5, TestLine = "[xUnit.net] MyTest [FAIL]" };
        second.ErrorMessageLines.Add("err");

        var result = GitHubRunLogFilterRunner.DeduplicateFailures([first, second]);

        result.Should().HaveCount(1);
        result[0].StartLine.Should().Be(1);
    }

    [Fact]
    public void DeduplicateFailures_DifferentNames_KeepsAll() {
        var a = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 1, TestLine = "  Failed TestA [FAIL]" };
        var b = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 2, TestLine = "  Failed TestB [FAIL]" };

        var result = GitHubRunLogFilterRunner.DeduplicateFailures([a, b]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateFailures_ErrorMarkerAndFailure_KeepsBoth() {
        var marker = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 1, TestLine = "##[error] x", IsErrorMarker = true };
        var failure = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 2, TestLine = "  Failed T [FAIL]" };

        var result = GitHubRunLogFilterRunner.DeduplicateFailures([marker, failure]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void FormatFailuresRustStyle_SingleFailure_ContainsLineMarker() {
        var f = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 10, TestLine = "  Failed T [FAIL]" };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle([f], 10, 0, "run123");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("--> line 10");
        result.GetFirstText().Should().Contain("run123");
    }

    [Fact]
    public void FormatFailuresRustStyle_MaxLinesTruncatesAndAddsContinueHint() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo> {
            new() { StartLine = 1, TestLine = "  Failed A [FAIL]" },
            new() { StartLine = 2, TestLine = "  Failed B [FAIL]" },
            new() { StartLine = 3, TestLine = "  Failed C [FAIL]" }
        };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle(failures, 2, 0, "r");
        var text = result.GetFirstText()!;

        text.Should().Contain("--> line 1");
        text.Should().Contain("--> line 2");
        text.Should().NotContain("--> line 3");
        text.Should().Contain("续读");
        text.Should().Contain("skip_lines=2");
    }

    [Fact]
    public void FormatFailuresRustStyle_SkipLinesSkipsFirst() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo> {
            new() { StartLine = 1, TestLine = "  Failed A [FAIL]" },
            new() { StartLine = 2, TestLine = "  Failed B [FAIL]" }
        };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle(failures, 10, 1, "r");
        var text = result.GetFirstText()!;

        text.Should().NotContain("--> line 1");
        text.Should().Contain("--> line 2");
        text.Should().Contain("跳过前 1");
    }

    [Fact]
    public void FormatFailuresRustStyle_AllShown_NoContinueHint() {
        var failures = new List<GitHubRunLogFilterRunner.TestFailureInfo> {
            new() { StartLine = 1, TestLine = "  Failed A [FAIL]" },
            new() { StartLine = 2, TestLine = "  Failed B [FAIL]" }
        };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle(failures, 10, 0, "r");
        var text = result.GetFirstText()!;

        text.Should().Contain("--> line 1");
        text.Should().Contain("--> line 2");
        text.Should().NotContain("续读");
    }

    [Fact]
    public void FormatFailuresRustStyle_PrefixContainsRunIdAndCount() {
        var f = new GitHubRunLogFilterRunner.TestFailureInfo { StartLine = 1, TestLine = "  Failed T [FAIL]" };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle([f], 10, 0, "run99");

        result.GetFirstText().Should().Contain("run99");
        result.GetFirstText().Should().Contain("1 个");
    }

    [Fact]
    public void FormatFailuresRustStyle_ErrorMarkerStripsErrorPrefix() {
        var f = new GitHubRunLogFilterRunner.TestFailureInfo {
            StartLine = 5,
            TestLine = "##[error] build failed",
            IsErrorMarker = true
        };

        var result = GitHubRunLogFilterRunner.FormatFailuresRustStyle([f], 10, 0, "r");
        var text = result.GetFirstText()!;

        text.Should().Contain("--> line 5");
        text.Should().Contain("build failed");
        text.Should().NotContain("##[error] build failed");
    }

    // ===== BuildZeroMatchHint: 0 行匹配精准提示（缺陷5）=====

    [Fact]
    public void BuildZeroMatchHint_FailedOnly_SuggestsAllPassedAndExpandJobs() {
        var hint = GitHubRunLogFilterRunner.BuildZeroMatchHint(failedOnly: true, scope: "失败步骤", filterLevel: null, markers: null);
        hint.Should().Contain("未匹配到任何失败步骤行");
        hint.Should().Contain("可能原因");
        hint.Should().Contain("通过");
        hint.Should().Contain("--expand jobs");
    }

    [Fact]
    public void BuildZeroMatchHint_WithMarkers_SuggestsFilterAll() {
        var hint = GitHubRunLogFilterRunner.BuildZeroMatchHint(failedOnly: false, scope: "日志", filterLevel: GitHubLogFilter.Error, markers: FrozenSet<string>.Empty);
        hint.Should().Contain("未匹配到任何日志行");
        hint.Should().Contain("--filter all");
        hint.Should().Contain("可能原因");
    }

    [Fact]
    public void BuildZeroMatchHint_NoMarkers_SuggestsExpandSteps() {
        var hint = GitHubRunLogFilterRunner.BuildZeroMatchHint(failedOnly: false, scope: "日志", filterLevel: null, markers: null);
        hint.Should().Contain("--expand steps");
    }

    // ===== D2: 可能性名单表格式(每行=可能原因+调查命令) =====

    [Fact]
    public void BuildZeroMatchHint_D2_FailedOnly_HasStructuredTableWithArrowCommands() {
        var hint = GitHubRunLogFilterRunner.BuildZeroMatchHint(failedOnly: true, scope: "失败步骤", filterLevel: null, markers: null);
        hint.Should().Contain("→", "每行应有 → 管道符标注调查命令");
        hint.Should().Contain("gh run view", "应包含完整 gh 命令");
        hint.Should().Contain("1)", "应有编号");
        hint.Should().Contain("2)", "应有多个编号");
    }

    [Fact]
    public void BuildZeroMatchHint_D2_WithMarkers_HasFilterAllAndWarningSuggestions() {
        var hint = GitHubRunLogFilterRunner.BuildZeroMatchHint(failedOnly: false, scope: "日志", filterLevel: GitHubLogFilter.Error, markers: FrozenSet<string>.Empty);
        hint.Should().Contain("→");
        hint.Should().Contain("filter all", "应提示放宽过滤");
        hint.Should().Contain("gh run view");
    }
}
