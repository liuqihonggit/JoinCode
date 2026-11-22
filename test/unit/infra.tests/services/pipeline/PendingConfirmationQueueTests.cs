namespace Infrastructure.Pipeline;

/// <summary>
/// 后台待确认队列测试 — 登记/快照/结算/释放语义；不依赖 UI 宿主。
/// </summary>
public sealed class PendingConfirmationQueueTests {
    [Fact]
    public async Task Enqueue_Snapshot_ExposesItemWithoutRemoving() {
        await using var queue = new PendingConfirmationQueue();
        var item = queue.Enqueue("agent-1", "Bash", "需要确认", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.HasPending.Should().BeTrue();
        queue.Snapshot().Should().ContainSingle(candidate => candidate.Id == item.Id);

        queue.Snapshot().Should().ContainSingle(candidate => candidate.Id == item.Id,
            "快照只读，供前台多次展示，不得把票据移除");
    }

    [Fact]
    public async Task TryResolve_CompletesDecisionTask() {
        await using var queue = new PendingConfirmationQueue();
        var item = queue.Enqueue(null, "FileWrite", "cwd 外写入", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.TryResolve(item.Id, PendingConfirmationDecision.Approve).Should().BeTrue();

        (await item.Decision).Should().Be(PendingConfirmationDecision.Approve);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task TryResolve_UnknownOrDuplicate_ReturnsFalse() {
        await using var queue = new PendingConfirmationQueue();
        var item = queue.Enqueue(null, "Bash", "确认", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.TryResolve("missing", PendingConfirmationDecision.Deny).Should().BeFalse();
        queue.TryResolve(item.Id, PendingConfirmationDecision.Deny).Should().BeTrue();
        queue.TryResolve(item.Id, PendingConfirmationDecision.Approve).Should().BeFalse("票据只能结算一次");
    }

    [Fact]
    public async Task Dispose_DefersPendingTickets() {
        var queue = new PendingConfirmationQueue();
        var item = queue.Enqueue(null, "Bash", "确认", new Dictionary<string, System.Text.Json.JsonElement>());

        await queue.DisposeAsync();

        (await item.Decision).Should().Be(PendingConfirmationDecision.Defer,
            "释放时不能让后台等待任务永远悬挂");
    }

    [Fact]
    public async Task Snapshot_OrdersByCreatedAtAscending() {
        await using var queue = new PendingConfirmationQueue();
        var first = queue.Enqueue("agent-1", "Bash", "一", new Dictionary<string, System.Text.Json.JsonElement>());
        var second = queue.Enqueue("agent-2", "FileWrite", "二", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.Snapshot().Select(item => item.Id).Should().Equal(first.Id, second.Id);
    }
}
