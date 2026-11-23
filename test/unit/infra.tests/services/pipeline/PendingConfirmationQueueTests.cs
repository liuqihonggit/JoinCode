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
    public async Task TryResolve_DeferCompletesWaitButKeepsTicketVisible() {
        await using var queue = new PendingConfirmationQueue();
        var item = queue.Enqueue(null, "Bash", "后台让出", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.TryResolve(item.Id, PendingConfirmationDecision.Defer).Should().BeTrue();

        (await item.Decision).Should().Be(PendingConfirmationDecision.Defer);
        queue.Snapshot().Should().ContainSingle(candidate => candidate.Id == item.Id,
            "Defer 只让出等待，前台仍要能看到待确认项");
        queue.TryResolve(item.Id, PendingConfirmationDecision.Approve).Should().BeTrue("前台确认后应结算并移除");
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

    [Fact]
    public async Task Enqueue_RedactsSensitiveArgumentsAndTruncatesText() {
        await using var queue = new PendingConfirmationQueue();
        var longText = new string('x', 700);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pending-confirmation", "a.txt");
        var item = queue.Enqueue(null, "Bash", "确认", new Dictionary<string, System.Text.Json.JsonElement> {
            ["api_key"] = System.Text.Json.JsonSerializer.SerializeToElement("sk-live-123456"),
            ["command"] = System.Text.Json.JsonSerializer.SerializeToElement("curl -H 'Authorization: Bearer abc' --token=topsecret https://example.test"),
            ["long_text"] = System.Text.Json.JsonSerializer.SerializeToElement(longText),
            ["path"] = System.Text.Json.JsonSerializer.SerializeToElement(path),
        });

        item.Arguments.Should().ContainKey("api_key");
        item.Arguments["api_key"].GetString().Should().Be("[REDACTED]");
        var command = item.Arguments["command"].GetString();
        command.Should().NotContain("abc").And.NotContain("topsecret");
        command.Should().Contain("[REDACTED]");
        item.Arguments["long_text"].GetString().Should().Contain("[truncated");
        item.Arguments["path"].GetString().Should().Be(path);
    }

    [Fact]
    public async Task Snapshot_DropsExpiredTicketsAndCompletesWaiters() {
        await using var queue = new PendingConfirmationQueue(TimeSpan.FromMilliseconds(40), 10);
        var item = queue.Enqueue(null, "Bash", "确认", new Dictionary<string, System.Text.Json.JsonElement>());

        await Task.Delay(80);
        queue.Snapshot().Should().BeEmpty("过期票据不应继续展示给前台");
        (await item.Decision).Should().Be(PendingConfirmationDecision.Defer, "清理时必须先完成等待，避免后台悬挂");
        queue.TryResolve(item.Id, PendingConfirmationDecision.Approve).Should().BeFalse();
    }

    [Fact]
    public async Task Enqueue_EvictsOldestWhenCapacityReached() {
        await using var queue = new PendingConfirmationQueue(TimeSpan.FromMinutes(5), 2);
        var first = queue.Enqueue(null, "Bash", "一", new Dictionary<string, System.Text.Json.JsonElement>());
        var second = queue.Enqueue(null, "FileWrite", "二", new Dictionary<string, System.Text.Json.JsonElement>());
        var third = queue.Enqueue(null, "WebFetch", "三", new Dictionary<string, System.Text.Json.JsonElement>());

        queue.Snapshot().Select(item => item.Id).Should().Equal(second.Id, third.Id);
        (await first.Decision).Should().Be(PendingConfirmationDecision.Defer, "被容量淘汰的票据也要完成等待");
    }
}
