namespace Mcp.Tests;

public sealed partial class GitHubToolHandlersTests {
    /// <summary>优化D1: 默认模式(无 expand 无 log)run 失败时,首次调用就置顶失败 job 列表+"尚未拉取"提示</summary>
    [Fact]
    public async Task RunView_Default_FailureRun_ShowsFailedJobsAndNotFetchedHint() {
        var runDetail = """{"id":42,"run_number":755,"status":"completed","conclusion":"failure","display_title":"CI","event":"push","head_branch":"main","head_sha":"abc1234","html_url":"https://github.com/o/r/actions/runs/42","created_at":"2026-10-08T10:00:00Z","updated_at":"2026-10-08T10:05:30Z"}""";
        var jobsJson = """{"jobs":[{"id":101,"name":"unit-tests","status":"completed","conclusion":"failure"},{"id":102,"name":"integration","status":"completed","conclusion":"failure"},{"id":103,"name":"lint","status":"completed","conclusion":"success"}]}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;

        text.Should().Contain("failure", "应显示 run 失败状态");
        text.Should().Contain("unit-tests", "应列出失败 job 名称");
        text.Should().Contain("integration", "应列出失败 job 名称");
        text.Should().Contain("101", "应列出失败 job ID");
        text.Should().Contain("尚未拉取", "应提示日志尚未拉取");
        text.Should().Contain("expand=failed", "应诱导用 expand=failed 拉取日志");

        var lintIdx = text.IndexOf("lint", StringComparison.Ordinal);
        lintIdx.Should().Be(-1, "成功 job 不应在默认模式列出(保持简洁)");
    }

    /// <summary>优化D1: 默认模式 run 成功时,不附加 job 列表(保持简洁)</summary>
    [Fact]
    public async Task RunView_Default_SuccessRun_DoesNotShowJobList() {
        var runDetail = """{"id":42,"run_number":755,"status":"completed","conclusion":"success","display_title":"CI","event":"push","head_branch":"main","head_sha":"abc1234","html_url":"https://github.com/o/r/actions/runs/42","created_at":"2026-10-08T10:00:00Z","updated_at":"2026-10-08T10:05:30Z"}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;

        text.Should().Contain("success");
        text.Should().NotContain("尚未拉取", "成功 run 不需要拉取日志提示");
        text.Should().NotContain("expand=failed", "成功 run 不需要失败日志提示");
    }

    /// <summary>优化D1: 默认模式 run 进行中时,附加 job 状态概览+"尚未拉取"提示</summary>
    [Fact]
    public async Task RunView_Default_InProgressRun_ShowsJobStatusAndHint() {
        var runDetail = """{"id":42,"run_number":755,"status":"in_progress","conclusion":null,"display_title":"CI","event":"push","head_branch":"main","head_sha":"abc1234","html_url":"https://github.com/o/r/actions/runs/42","created_at":"2026-10-08T10:00:00Z","updated_at":"2026-10-08T10:05:30Z"}""";
        var jobsJson = """{"jobs":[{"id":101,"name":"unit-tests","status":"completed","conclusion":"failure"},{"id":102,"name":"integration","status":"in_progress","conclusion":null}]}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = jobsJson });

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;

        text.Should().Contain("in_progress");
        text.Should().Contain("unit-tests", "应列出已失败 job");
        text.Should().Contain("尚未拉取", "应提示日志尚未拉取");
    }

    /// <summary>优化D1: 默认模式 verbosity=2(完整JSON)时不附加 job 列表(保持原始 JSON)</summary>
    [Fact]
    public async Task RunView_Default_Verbosity2_FailureRun_DoesNotAppendJobs() {
        var runDetail = """{"id":42,"run_number":755,"status":"completed","conclusion":"failure","display_title":"CI"}""";

        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = runDetail });

        var result = await _handler.GhRunViewAsync("42", repo: "owner/repo", verbosity: 2);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;

        text.Should().NotContain("尚未拉取", "verbosity=2 完整 JSON 模式不附加提示");
    }
}
