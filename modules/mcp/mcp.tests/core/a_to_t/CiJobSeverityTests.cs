namespace Mcp.Tests;

/// <summary>
/// CiJobSeverity 状态机测试 — 超时取消判定(isTimeoutCancelled)
/// <para>背景: GitHub API 中超时 job(超过 timeout-minutes 被强制取消)的 conclusion=cancelled,
/// 与手动取消相同。唯一区分判据是 check-run annotations 带 "exceeded the maximum execution time"。
/// 探测层拿到该判据后,通过 isTimeoutCancelled 参数把 Cancelled 升级为 TimedOut。</para>
/// </summary>
public sealed class CiJobSeverityTests {
    [Fact]
    public void FromConclusion_CancelledWithTimeoutFlag_MapsToTimedOut() {
        var severity = CiJobSeverityExtensions.FromConclusion("cancelled", "completed", isTimeoutCancelled: true);
        severity.Should().Be(CiJobSeverity.TimedOut, "超时取消(依据 check-run annotation)应映射为 TimedOut 而非 Cancelled");
    }

    [Fact]
    public void FromConclusion_CancelledWithoutTimeoutFlag_MapsToCancelled() {
        var severity = CiJobSeverityExtensions.FromConclusion("cancelled", "completed", isTimeoutCancelled: false);
        severity.Should().Be(CiJobSeverity.Cancelled, "无超时注解的 cancelled 保持 Cancelled(手动取消)");
    }

    [Fact]
    public void FromConclusion_CancelledDefaultParam_StaysCancelled_BackCompat() {
        var severity = CiJobSeverityExtensions.FromConclusion("cancelled", "completed");
        severity.Should().Be(CiJobSeverity.Cancelled, "默认参数向后兼容: 未传 isTimeoutCancelled 时保持原行为");
    }

    [Fact]
    public void FromConclusion_TimeoutFlagOnlyAppliesToCancelled() {
        var severity = CiJobSeverityExtensions.FromConclusion("failure", "completed", isTimeoutCancelled: true);
        severity.Should().Be(CiJobSeverity.Failure, "isTimeoutCancelled 仅对 cancelled 生效,不影响其他结论");
    }

    [Fact]
    public void TimedOut_IsAnomaly_AndSortsBeforeCancelled() {
        CiJobSeverity.TimedOut.IsAnomaly().Should().BeTrue("超时属于异常,需完整展开");
        CiJobSeverityExtensions.SortKey(CiJobSeverity.TimedOut).Should()
            .BeLessThan(CiJobSeverityExtensions.SortKey(CiJobSeverity.Cancelled), "超时比取消更严重,排序在前(优先冒泡)");
    }

    [Fact]
    public void ToConclusionDisplay_TimeoutCancelled_ShowsBothConclusionAndReason() {
        var severity = CiJobSeverityExtensions.FromConclusion("cancelled", "completed", isTimeoutCancelled: true);
        var display = severity.ToConclusionDisplay("cancelled");
        display.Should().Contain("cancelled", "保留原始 API 结论");
        display.Should().Contain("超时", "标注超时原因,让 AI 不把超时误读为手动取消");
    }

    [Fact]
    public void ToConclusionDisplay_ManualCancelled_ShowsPlainCancelled() {
        var severity = CiJobSeverityExtensions.FromConclusion("cancelled", "completed", isTimeoutCancelled: false);
        severity.ToConclusionDisplay("cancelled").Should().Be("cancelled", "手动取消不标注超时");
    }
}
