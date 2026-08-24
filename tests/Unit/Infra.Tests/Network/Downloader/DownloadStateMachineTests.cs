namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// DownloadStateMachine 单元测试 — 验证所有合法转换、非法转换抛 [DOWN001]、终态不可转换、线程安全
/// <para>操作遍历用 Enum.GetValues&lt;DownloadOperation&gt;(),不硬编码字符串</para>
/// </summary>
public sealed class DownloadStateMachineTests
{
    private static readonly DownloadOperation[] AllOperations =
        Enum.GetValues<DownloadOperation>();

    // === 合法转换 ===

    [Fact]
    public void TryStart_FromIdle_ToDownloading()
    {
        var sm = new DownloadStateMachine();
        var t = sm.TryStart();
        t.Success.Should().BeTrue();
        t.PreviousState.Should().Be(DownloadState.Idle);
        t.NewState.Should().Be(DownloadState.Downloading);
        sm.State.Should().Be(DownloadState.Downloading);
    }

    [Fact]
    public void TryPause_FromDownloading_ToPaused()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        var t = sm.TryPause();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Paused);
        sm.State.Should().Be(DownloadState.Paused);
    }

    [Fact]
    public void TryResume_FromPaused_ToDownloading()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryPause();
        var t = sm.TryResume();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Downloading);
    }

    [Fact]
    public void TryEnterMerging_FromDownloading_ToMerging()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        var t = sm.TryEnterMerging();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Merging);
    }

    [Fact]
    public void TryComplete_FromMerging_ToCompleted()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryEnterMerging();
        var t = sm.TryComplete();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Completed);
    }

    [Fact]
    public void TryCancel_FromIdle_ToCancelled()
    {
        var sm = new DownloadStateMachine();
        var t = sm.TryCancel();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Cancelled);
    }

    [Fact]
    public void TryCancel_FromDownloading_ToCancelled()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        var t = sm.TryCancel();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Cancelled);
    }

    [Fact]
    public void TryCancel_FromPaused_ToCancelled()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryPause();
        var t = sm.TryCancel();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Cancelled);
    }

    [Fact]
    public void TryCancel_FromMerging_ToCancelled()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryEnterMerging();
        var t = sm.TryCancel();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Cancelled);
    }

    [Fact]
    public void TryFail_FromDownloading_ToFailed()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        var t = sm.TryFail();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Failed);
    }

    [Fact]
    public void TryFail_FromPaused_ToFailed()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryPause();
        var t = sm.TryFail();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Failed);
    }

    [Fact]
    public void TryFail_FromMerging_ToFailed()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryEnterMerging();
        var t = sm.TryFail();
        t.Success.Should().BeTrue();
        t.NewState.Should().Be(DownloadState.Failed);
    }

    // === 非法转换:从 Idle(合法操作仅 Start/Cancel) ===

    [Theory]
    [InlineData(DownloadOperation.Pause)]
    [InlineData(DownloadOperation.Resume)]
    [InlineData(DownloadOperation.EnterMerging)]
    [InlineData(DownloadOperation.Complete)]
    [InlineData(DownloadOperation.Fail)]
    public void IllegalTransitions_FromIdle_Fail(DownloadOperation op)
    {
        var sm = new DownloadStateMachine();
        var t = sm.TryTransition(op);
        t.Success.Should().BeFalse();
        t.Error.Should().Contain("[DOWN001]");
        sm.State.Should().Be(DownloadState.Idle);
    }

    // === 非法转换:从 Downloading(合法操作仅 Pause/EnterMerging/Cancel/Fail) ===

    [Theory]
    [InlineData(DownloadOperation.Start)]
    [InlineData(DownloadOperation.Resume)]
    [InlineData(DownloadOperation.Complete)]
    public void IllegalTransitions_FromDownloading_Fail(DownloadOperation op)
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        var t = sm.TryTransition(op);
        t.Success.Should().BeFalse();
        t.Error.Should().Contain("[DOWN001]");
        sm.State.Should().Be(DownloadState.Downloading);
    }

    // === 非法转换:从 Paused(合法操作仅 Resume/Cancel/Fail) ===

    [Theory]
    [InlineData(DownloadOperation.Start)]
    [InlineData(DownloadOperation.Pause)]
    [InlineData(DownloadOperation.EnterMerging)]
    [InlineData(DownloadOperation.Complete)]
    public void IllegalTransitions_FromPaused_Fail(DownloadOperation op)
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryPause();
        var t = sm.TryTransition(op);
        t.Success.Should().BeFalse();
        t.Error.Should().Contain("[DOWN001]");
        sm.State.Should().Be(DownloadState.Paused);
    }

    // === 非法转换:从 Merging(合法操作仅 Complete/Cancel/Fail) ===

    [Theory]
    [InlineData(DownloadOperation.Start)]
    [InlineData(DownloadOperation.Pause)]
    [InlineData(DownloadOperation.Resume)]
    [InlineData(DownloadOperation.EnterMerging)]
    public void IllegalTransitions_FromMerging_Fail(DownloadOperation op)
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryEnterMerging();
        var t = sm.TryTransition(op);
        t.Success.Should().BeFalse();
        t.Error.Should().Contain("[DOWN001]");
        sm.State.Should().Be(DownloadState.Merging);
    }

    // === 终态不可转换:遍历所有操作,全部失败 ===

    [Fact]
    public void TerminalState_Completed_NoTransition()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryEnterMerging();
        sm.TryComplete();
        AssertAllOpsFail(sm, DownloadState.Completed);
    }

    [Fact]
    public void TerminalState_Cancelled_NoTransition()
    {
        var sm = new DownloadStateMachine();
        sm.TryCancel();
        AssertAllOpsFail(sm, DownloadState.Cancelled);
    }

    [Fact]
    public void TerminalState_Failed_NoTransition()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();
        sm.TryFail();
        AssertAllOpsFail(sm, DownloadState.Failed);
    }

    // === 线程安全:并发 Pause + Cancel,最终必为 Cancelled 终态 ===

    [Fact]
    public async Task ConcurrentPauseAndCancel_EndsInCancelled()
    {
        var sm = new DownloadStateMachine();
        sm.TryStart();

        var pauseTask = Task.Run(() => sm.TryPause());
        var cancelTask = Task.Run(() => sm.TryCancel());
        var results = await Task.WhenAll(pauseTask, cancelTask).ConfigureAwait(true);

        results.Should().Contain(r => r.Success, "至少一个操作应成功");
        sm.State.Should().Be(DownloadState.Cancelled,
            "Cancel 总能到达终态:若 Cancel 先执行→Cancelled;若 Pause 先执行→Paused,Cancel 仍可从 Paused→Cancelled");
    }

    // === 辅助 ===

    private static void AssertAllOpsFail(DownloadStateMachine sm, DownloadState expected)
    {
        foreach (var op in AllOperations)
        {
            var t = sm.TryTransition(op);
            t.Success.Should().BeFalse($"{expected} 终态不应响应 {op}");
            sm.State.Should().Be(expected, $"{op} 不应改变终态 {expected}");
        }
    }
}
