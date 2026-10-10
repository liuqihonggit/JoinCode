#pragma warning disable JCC11003
namespace Mcp.Tests;

/// <summary>
/// pr checks 警告档测试 — GAP-038-06: stale/action_required 归入 warning 档
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    /// <summary>GAP-038-06: stale/action_required 归入警告档,排序 fail→warning→pass</summary>
    [Fact]
    public async Task PrChecks_StaleAndActionRequired_CountedAsWarning() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"head":{"sha":"abc"}}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"check_runs":[{"name":"pass1","conclusion":"success"},{"name":"stale-check","conclusion":"stale"},{"name":"action-check","conclusion":"action_required"},{"name":"fail1","conclusion":"failure"}]}""" });

        var result = await _handler.GhPrChecksAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });
        var text = result.GetFirstText()!;
        text.Should().Contain("2 警告");
        text.Should().Contain("stale-check\twarning");
        text.Should().Contain("action-check\twarning");
        var failIdx = text.IndexOf("fail1", StringComparison.Ordinal);
        var warnIdx = text.IndexOf("stale-check", StringComparison.Ordinal);
        var passIdx = text.IndexOf("pass1", StringComparison.Ordinal);
        failIdx.Should().BeLessThan(warnIdx, "失败应在警告前");
        warnIdx.Should().BeLessThan(passIdx, "警告应在通过前");
    }
}
