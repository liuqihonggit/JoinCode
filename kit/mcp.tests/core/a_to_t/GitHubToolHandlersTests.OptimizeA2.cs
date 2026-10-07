namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    /// <summary>优化A2: expand=jobs 汇总前置+全量失败+success 折叠到5(AI 首屏定位问题降 token)</summary>
    [Fact]
    public async Task RunView_ExpandJobs_SummaryFirst_AllFailuresShown_SuccessFolded() {
        var jobs = new List<string>();
        for (var i = 1; i <= 20; i++)
            jobs.Add("{\"id\":" + i + ",\"name\":\"success-" + i.ToString("D2") + "\",\"status\":\"completed\",\"conclusion\":\"success\"}");
        for (var i = 21; i <= 30; i++)
            jobs.Add("{\"id\":" + i + ",\"name\":\"failure-" + i.ToString("D2") + "\",\"status\":\"completed\",\"conclusion\":\"failure\"}");
        var jobsJson = "{\"jobs\":[" + string.Join(",", jobs) + "]}";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":42}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });

        var result = await _handler.GhRunViewAsync("42", expand: "jobs", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;

        var summaryIdx = text.IndexOf("汇总", StringComparison.Ordinal);
        var firstFailureIdx = text.IndexOf("failure-21", StringComparison.Ordinal);
        var lastFailureIdx = text.IndexOf("failure-30", StringComparison.Ordinal);
        var firstSuccessIdx = text.IndexOf("success-01", StringComparison.Ordinal);
        var fifthSuccessIdx = text.IndexOf("success-05", StringComparison.Ordinal);
        var sixthSuccessIdx = text.IndexOf("success-06", StringComparison.Ordinal);

        summaryIdx.Should().BeGreaterThanOrEqualTo(0, "应有汇总行");
        firstFailureIdx.Should().BeGreaterThan(summaryIdx, "汇总应在失败 job 前");
        lastFailureIdx.Should().BeGreaterThan(0, "全部 10 个失败 job 都应显示");
        firstFailureIdx.Should().BeLessThan(firstSuccessIdx, "失败 job 应在 success 前");
        fifthSuccessIdx.Should().BeGreaterThan(0, "前 5 个 success 应显示");
        sixthSuccessIdx.Should().Be(-1, "第 6 个 success 应被折叠");
        text.Should().Contain("另有 15 个 success", "应提示折叠了 15 个 success");
    }
}
