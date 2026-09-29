namespace Mcp.Tests;

/// <summary>
/// GitHubRunLogFilterRunner.FilterFailedTestsAsync 编排逻辑确定性测试 —
/// 用 FakeGitHubApiClient 消除 HTTP IO,验证"拉取→状态机推进→去重→格式化"主流程。
/// 子方法 ProcessLogLine/DeduplicateFailures/FormatFailuresRustStyle 的纯计算已由
/// GitHubRunLogFilterRunnerTests 覆盖,此处聚焦主方法编排。
/// </summary>
public sealed class GitHubRunLogFilterRunnerFilterTests {

    [Fact]
    public async Task FilterFailedTestsAsync_WithJobId_FiltersFailuresFromJobLogs() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] {
                "  Failed MyTest [FAIL]",
                "  Error Message:",
                "  Expected: 1 but was: 2",
                "  Stack Trace:",
                "  at Foo.Bar() in line 10",
                "  Passed OtherTest [1 ms]"
            }
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "123", "123", 10, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("--> line 1");
        text.Should().Contain("MyTest");
        text.Should().Contain("Expected: 1 but was: 2");
    }

    [Fact]
    public async Task FilterFailedTestsAsync_WithJobId_NoFailures_ReturnsEmptyMessage() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] { "just a log line", "another line" }
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "123", "123", 10, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未检测到测试失败行");
    }

    [Fact]
    public async Task FilterFailedTestsAsync_NoJobId_FiltersFromFailedJobs() {
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"jobs":[{"id":42,"conclusion":"failure"}]}"""
        });
        api.NextLogLines = new[] {
            "  Failed JobTest [FAIL]",
            "  Error Message:",
            "  boom",
            "  Stack Trace:",
            "  at Baz() in line 5"
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "999", null, 10, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("--> line 1");
        text.Should().Contain("JobTest");
    }

    [Fact]
    public async Task FilterFailedTestsAsync_NoJobId_NoFailedJobs_ReturnsEmptyMessage() {
        var api = new FakeGitHubApiClient();
        api.EnqueueResponse(new GitHubApiResponse {
            Success = true,
            StatusCode = 200,
            Body = """{"jobs":[{"id":42,"conclusion":"success"}]}"""
        });
        api.NextLogLines = Array.Empty<string>();
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "999", null, 10, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("未检测到测试失败行");
    }

    [Fact]
    public async Task FilterFailedTestsAsync_DuplicateTestName_DeduplicatesToOne() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] {
                "  Failed SameTest [FAIL]",
                "  Error Message:",
                "  first error",
                "  Stack Trace:",
                "  at A() in line 1",
                "  Passed Reset [1 ms]",
                "[xUnit.net] SameTest [FAIL]",
                "  Error Message:",
                "  second error",
                "  more detail",
                "  Stack Trace:",
                "  at B() in line 2"
            }
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "123", "123", 10, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("1 个");
        text.Should().Contain("SameTest");
        text.Should().Contain("more detail");
    }

    [Fact]
    public async Task FilterFailedTestsAsync_MaxLinesTruncates_AddsContinueHint() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] {
                "  Failed TestA [FAIL]",
                "  Passed Reset1 [1 ms]",
                "  Failed TestB [FAIL]",
                "  Passed Reset2 [1 ms]",
                "  Failed TestC [FAIL]"
            }
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.FilterFailedTestsAsync("owner", "repo", "123", "123", 2, 0, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("--> line 1");
        text.Should().Contain("--> line 3");
        text.Should().NotContain("--> line 5");
        text.Should().Contain("续读");
    }
}
