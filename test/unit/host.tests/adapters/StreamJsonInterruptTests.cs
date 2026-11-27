namespace Host.Tests.Adapters;

/// <summary>
/// TASK142-B — SDK 流式输入的 interrupt 句柄（CAS 快照，不用显式锁）。
/// <para>真机已证明 control_request 会被解析并回 control_response；"打断在途回合"在本地
/// 缺少可控的慢 provider，无法稳定复现 HTTP 在途场景，所以这里锁住决定成败的竞态：
/// 读循环与回合收尾并发时 CTS 的登记/清除/取消/释放。</para>
/// </summary>
public sealed class StreamJsonInterruptTests {
    [Fact]
    public void CancelActive_CancelsOnlyTheRegisteredTurn() {
        using var holder = new NonInteractiveModeRunner.TurnCancellation();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        holder.SetActive(first);
        holder.CancelActive();

        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Token.IsCancellationRequested.Should().BeFalse();

        holder.SetActive(second);
        first.Cancel();
        second.Token.IsCancellationRequested.Should().BeFalse("上一回合的 CTS 不能再影响新回合");
    }

    [Fact]
    public void ClearActive_DoesNotRemoveANewerTurn() {
        using var holder = new NonInteractiveModeRunner.TurnCancellation();
        using var old = new CancellationTokenSource();
        using var current = new CancellationTokenSource();

        holder.SetActive(old);
        holder.SetActive(current);
        holder.ClearActive(old);

        holder.CancelActive();
        current.Token.IsCancellationRequested.Should().BeTrue("迟到的旧回合收尾不能把新回合摘掉");
    }

    [Fact]
    public void CancelActive_AfterTurnDispose_IsIgnoredWithNoThrow() {
        using var holder = new NonInteractiveModeRunner.TurnCancellation();
        var finished = new CancellationTokenSource();

        holder.SetActive(finished);
        finished.Dispose(); // 模拟"回合已收尾并释放 CTS，读循环仍持有旧快照"

        var act = () => holder.CancelActive();
        act.Should().NotThrow("读循环可能拿着旧快照，命中已释放的 CTS 属于正常竞态");
    }

    [Fact]
    public void Dispose_IsIdempotent_AndDisposesRegisteredTurn() {
        var holder = new NonInteractiveModeRunner.TurnCancellation();
        var cts = new CancellationTokenSource();
        holder.SetActive(cts);

        holder.Dispose();
        holder.Dispose();

        var act = () => cts.Token.Register(() => { });
        act.Should().Throw<ObjectDisposedException>("仍登记的 CTS 应由持有者释放");
    }
}
