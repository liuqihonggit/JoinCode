// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Mcp.Tests;

/// <summary>
/// 超时 job 探测与冒泡测试 — cancelled + check-run annotation "exceeded the maximum execution time" → TimedOut
/// <para>真实数据依据(2026-10-20 run 39320384025 实测): 超时 job 的 API conclusion=cancelled,
/// 与手动取消无区别;check-run annotations 带 [failure] "The job has exceeded the maximum execution time of 35m0s"。</para>
/// <para>本工具的历史盲区: 把超时 job 显示为 🚫 cancelled,AI 误判为人为取消而漏修超时问题。</para>
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    private const string TimeoutRunDetail = """
        {"id":42,"run_number":102,"status":"completed","conclusion":"cancelled","display_title":"Mutation Testing","event":"schedule","head_branch":"w2","head_sha":"abc1234","html_url":"https://github.com/o/r/actions/runs/42","created_at":"2026-10-19T20:34:06Z","updated_at":"2026-10-19T21:33:06Z"}
        """;

    private const string JobsWithCancelled = """
        {"jobs":[{"id":101,"name":"Stryker - CodeIndex","status":"completed","conclusion":"cancelled","check_run_url":"https://api.github.com/repos/owner/repo/check-runs/555"},{"id":102,"name":"Stryker - Brain-CtxAdmin","status":"completed","conclusion":"success"}]}
        """;

    private const string TimeoutAnnotations = """
        [{"annotation_level":"failure","message":"The job has exceeded the maximum execution time of 35m0s"},{"annotation_level":"failure","message":"The operation was canceled."}]
        """;

    /// <summary>默认视图: 超时 cancelled job 应显示"超时"分组(⏱)+超时诱导文案,而非误导性的"取消"分组</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RunView_Default_TimeoutCancelledJob_ShowsTimeoutGroupAndHint() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = JobsWithCancelled });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutAnnotations });

        var result = await _handler.GhRunViewAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("超时", "超时 job 应分组为超时,不能误导为手动取消");
        text.Should().Contain("Stryker - CodeIndex", "应列出超时 job 名称");
        text.Should().NotContain("取消 Job", "超时 job 不应留在取消分组(避免 AI 误判为人为取消)");
        text.Should().Contain("timeout-minutes", "应诱导排查超时原因(挂死 vs 纯慢)");
    }

    /// <summary>默认视图: cancelled job 无超时注解时保持"取消"分组(不误判)</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RunView_Default_ManualCancelledJob_StillShowsCancelledGroup() {
        var emptyAnnotations = "[]";
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = JobsWithCancelled });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = emptyAnnotations });

        var result = await _handler.GhRunViewAsync("42", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("取消", "无超时注解的 cancelled 是手动取消,保持取消分组");
        text.Should().NotContain("超时 Job", "无超时注解不得误判为超时");
    }

    /// <summary>expand=jobs: 超时 job 行尾应标注超时(cancelled→超时)而非裸 cancelled</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RunView_ExpandJobs_TimeoutCancelledJob_MarkedAsTimeout() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = JobsWithCancelled });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutAnnotations });

        var result = await _handler.GhRunViewAsync("42", expand: "jobs", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("Stryker - CodeIndex");
        var line = text.Split('\n').FirstOrDefault(l => l.Contains("Stryker - CodeIndex"));
        line.Should().NotBeNull();
        line.Should().Contain("超时", "job 行应标注超时(如 [cancelled→超时]),让 AI 一眼区分超时与手动取消");
    }

    /// <summary>expand=failed: 超时 job 应进失败列表且标记为超时(而非 🚫 cancelled)</summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task RunView_ExpandFailed_TimeoutCancelledJob_ListedAsTimeout() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = JobsWithCancelled });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutAnnotations });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = TimeoutRunDetail });

        var result = await _handler.GhRunViewAsync("42", expand: "failed", common: new GitHubCommonOptions { Repo = "owner/repo" });

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText()!;
        text.Should().Contain("Stryker - CodeIndex", "超时 job 属于失败 job,应进 expand=failed 列表");
        text.Should().Contain("超时", "应标注超时原因");
    }
}
