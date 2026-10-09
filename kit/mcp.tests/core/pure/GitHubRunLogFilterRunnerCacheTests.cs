// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Mcp.Tests;

/// <summary>
/// GitHubRunLogFilterRunner LSM 缓存命中测试 — 验证 GetOrFetchSummaryAsync/GetOrFetchSectionAsync
/// 首次调用下载+写缓存,二次调用从 LSM 缓存读(不调 API)。
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunLogFilterRunnerCacheTests {

    private static readonly string[] SampleLogLines = [
        "##[group]Run dotnet test",
        "##[endgroup]",
        "##[error] Test failed",
        "Test output line 1",
        "Test output line 2",
    ];

    [Fact]
    public async Task GetOrFetchSummaryAsync_CacheMiss_ThenHit_SecondCallFromCache() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var summary1 = await runner.GetOrFetchSummaryAsync("owner", "repo", "123", "456", false, CancellationToken.None);
        summary1.Should().NotBeNull();
        summary1!.StepLineCounts.Should().NotBeEmpty();

        api.NextLogLines = Array.Empty<string>();
        var summary2 = await runner.GetOrFetchSummaryAsync("owner", "repo", "123", "456", false, CancellationToken.None);
        summary2.Should().NotBeNull();
        summary2!.StepLineCounts.Should().BeEquivalentTo(summary1.StepLineCounts);
    }

    [Fact]
    public async Task GetOrFetchSummaryAsync_WantRefresh_SkipsCacheRead() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var summary1 = await runner.GetOrFetchSummaryAsync("owner", "repo", "123", "456", false, CancellationToken.None);
        summary1!.StepLineCounts.Should().NotBeEmpty();

        api.NextLogLines = new[] { "##[group]Run dotnet build", "##[endgroup]", "##[error] Build failed" };
        var summary2 = await runner.GetOrFetchSummaryAsync("owner", "repo", "123", "456", true, CancellationToken.None);
        summary2!.StepLineCounts.Should().ContainKey("dotnet build");
        summary2.StepLineCounts.Should().NotContainKey("dotnet test");
    }

    [Fact]
    public async Task GetOrFetchSectionAsync_ReturnsSectionLines() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var lines = await runner.GetOrFetchSectionAsync("owner", "repo", "123", "456", "dotnet test", "error", false, CancellationToken.None);
        lines.Should().NotBeNull();
        lines!.Should().ContainSingle(l => l.Contains("##[error]"));
    }

    [Fact]
    public async Task GetOrFetchSectionAsync_CacheHit_SecondCallFromCache() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var lines1 = await runner.GetOrFetchSectionAsync("owner", "repo", "123", "456", "dotnet test", "error", false, CancellationToken.None);
        lines1.Should().NotBeEmpty();

        api.NextLogLines = Array.Empty<string>();
        var lines2 = await runner.GetOrFetchSectionAsync("owner", "repo", "123", "456", "dotnet test", "error", false, CancellationToken.None);
        lines2.Should().BeEquivalentTo(lines1);
    }

    [Fact]
    public async Task GetOrFetchSectionAsync_NonExistentSection_ReturnsNull() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var lines = await runner.GetOrFetchSectionAsync("owner", "repo", "123", "456", "nonexistent", "error", false, CancellationToken.None);
        lines.Should().BeNull();
    }

    [Fact]
    public async Task GetOrFetchSummaryAsync_NoJobId_UsesRunLogs() {
        var api = new FakeGitHubApiClient { NextLogLines = SampleLogLines };
        await using var kv = new InMemoryKvStore();
        var runner = new GitHubRunLogFilterRunner(api, kv);

        var summary = await runner.GetOrFetchSummaryAsync("owner", "repo", "123", null, false, CancellationToken.None);
        summary.Should().NotBeNull();
        summary!.StepLineCounts.Should().NotBeEmpty();
    }
}
