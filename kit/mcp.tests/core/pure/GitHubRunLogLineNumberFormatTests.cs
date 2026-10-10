// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Mcp.Tests;

/// <summary>
/// gh 日志行号前缀格式测试 — 验证 GitHubRunLogFilterRunner 输出的行号格式
/// 统一到 LineNumberFormatter（紧凑 tab 或箭头 →），对齐 TS addLineNumbers。
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class GitHubRunLogLineNumberFormatTests {

    [Fact]
    public async Task StreamAndFilterAsync_CompactLinePrefix_UsesTabSeparator() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] { "line one", "line two" }
        };
        var runner = new GitHubRunLogFilterRunner(api, compactLinePrefix: true);

        var result = await runner.StreamAndFilterAsync(
            "owner", "repo", "123", "123", failedOnly: false,
            "test", markers: null, filterLevel: null,
            maxLines: 10, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("1\tline one");
        text.Should().Contain("2\tline two");
    }

    [Fact]
    public async Task StreamAndFilterAsync_WideLinePrefix_UsesArrowSeparator() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] { "line one", "line two" }
        };
        var runner = new GitHubRunLogFilterRunner(api, compactLinePrefix: false);

        var result = await runner.StreamAndFilterAsync(
            "owner", "repo", "123", "123", failedOnly: false,
            "test", markers: null, filterLevel: null,
            maxLines: 10, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("     1\u2192line one");
        text.Should().Contain("     2\u2192line two");
    }

    [Fact]
    public async Task StreamAndFilterAsync_DefaultCompactLinePrefix_UsesTabSeparator() {
        var api = new FakeGitHubApiClient {
            NextLogLines = new[] { "content" }
        };
        var runner = new GitHubRunLogFilterRunner(api);

        var result = await runner.StreamAndFilterAsync(
            "owner", "repo", "123", "123", failedOnly: false,
            "test", markers: null, filterLevel: null,
            maxLines: 10, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("1\tcontent");
        text.Should().NotContain("1: content");
    }
}
