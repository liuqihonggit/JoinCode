namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 待确认收件箱测试：队列→DTO 映射、批准/拒绝路由、批量结算、引擎未就绪 fail-closed，
/// 以及 index.html 注入幂等（不破坏第三方资产、重复打开不重复注入）。
/// </summary>
public sealed class DshPendingConfirmationsTests {
    /// <summary>只读队列桩 — 票据由测试构造，不引入 Infrastructure 实现依赖。</summary>
    private sealed class TestQueue(params PendingConfirmationItem[] items) : IPendingConfirmationQueue {
        public bool HasPending => items.Length > 0;

        public PendingConfirmationItem Enqueue(string? agentId, string toolName, string reason,
            IReadOnlyDictionary<string, JsonElement> arguments) => throw new NotSupportedException();

        public IReadOnlyList<PendingConfirmationItem> Snapshot() => items;

        public bool TryResolve(string id, PendingConfirmationDecision decision) => throw new NotSupportedException();
    }

    /// <summary>结算器桩 — 记录调用并按预设返回，不触达真实权限管理器/通知队列。</summary>
    private sealed class TestResolver(IReadOnlyList<PendingConfirmationItem> items) : IPendingConfirmationResolver {
        internal List<(string Id, PendingConfirmationDecision Decision)> Calls { get; } = [];
        internal List<PendingConfirmationDecision> BatchCalls { get; } = [];
        internal bool Result { get; init; } = true;
        internal int BatchResult { get; init; }

        public IReadOnlyList<PendingConfirmationItem> ListPending() => items;

        public bool Resolve(string id, PendingConfirmationDecision decision) {
            Calls.Add((id, decision));
            return Result;
        }

        public int ResolveAll(PendingConfirmationDecision decision) {
            BatchCalls.Add(decision);
            return BatchResult;
        }
    }

    private static PendingConfirmationItem Item(string id, string tool, string reason, DateTime createdAt) => new() {
        Id = id,
        AgentId = "agent-bg",
        ToolName = tool,
        Reason = reason,
        Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
            ["command"] = JsonSerializer.SerializeToElement("npm test"),
        },
        CreatedAtUtc = createdAt,
        Decision = Task.FromResult(PendingConfirmationDecision.Defer),
    };

    [Fact]
    public void List_WhenEngineNotAttached_ReturnsUnavailableWithGuidance() {
        var broker = new DshPendingConfirmationBroker();

        var result = broker.List();

        result.Available.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.Message.Should().Contain("引擎仍在组装");
    }

    [Fact]
    public void Resolve_WhenResolverNotAttached_FailsClosed() {
        var broker = new DshPendingConfirmationBroker();

        var result = broker.Resolve("ticket-1", "approve");

        result.Resolved.Should().BeFalse("引擎未就绪不能假装已处理");
        result.Message.Should().Contain("暂不可用");
    }

    [Fact]
    public void List_MapsTicketToDtoWithUnixMilliseconds() {
        var created = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        var broker = new DshPendingConfirmationBroker();
        broker.Attach(
            new TestQueue(Item("ticket-1", "Bash", "需要确认", created)),
            new TestResolver([]));

        var result = broker.List();

        result.Available.Should().BeTrue();
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be("ticket-1");
        result.Items[0].ToolName.Should().Be("Bash");
        result.Items[0].AgentId.Should().Be("agent-bg");
        result.Items[0].CreatedAt.Should().Be(new DateTimeOffset(created, TimeSpan.Zero).ToUnixTimeMilliseconds());
        result.Items[0].Arguments.Should().ContainKey("command");
    }

    [Fact]
    public void Resolve_MapsApproveAndDenyAndReturnsRemaining() {
        var ticket = Item("ticket-1", "Bash", "需要确认", DateTime.UtcNow);
        var resolver = new TestResolver([ticket]);
        var broker = new DshPendingConfirmationBroker();
        broker.Attach(new TestQueue(ticket), resolver);

        broker.Resolve("ticket-1", "approve").Resolved.Should().BeTrue();
        resolver.Calls.Should().ContainSingle(call => call.Decision == PendingConfirmationDecision.Approve);

        broker.Resolve("ticket-2", "deny").Resolved.Should().BeTrue();
        resolver.Calls.Should().Contain(call =>
            call.Id == "ticket-2" && call.Decision == PendingConfirmationDecision.Deny);
    }

    [Fact]
    public void Resolve_RejectsUnknownDecisionAndMissingTicket() {
        var resolver = new TestResolver([]) { Result = false };
        var broker = new DshPendingConfirmationBroker();
        broker.Attach(new TestQueue(), resolver);

        broker.Resolve("ticket-1", "defer").Message.Should().Contain("只接受 approve 或 deny");
        broker.Resolve("ticket-1", "approve").Message.Should().Contain("不存在、已过期或已被处理");
    }

    [Fact]
    public void ResolveAll_ReturnsBatchCountAndRemaining() {
        var broker = new DshPendingConfirmationBroker();
        var resolver = new TestResolver([]) { BatchResult = 2 };
        broker.Attach(new TestQueue(), resolver);

        var result = broker.ResolveAll("deny");

        result.Resolved.Should().BeTrue();
        result.Message.Should().Contain("2");
        resolver.BatchCalls.Should().ContainSingle().Which.Should().Be(PendingConfirmationDecision.Deny);
    }

    [Fact]
    public void InjectPendingConfirmationUi_InsertsBeforeBodyCloseAndIsIdempotent() {
        const string html = "<html><body><div id=\"app\"></div></body></html>";

        var bytes = DshWebHost.InjectPendingConfirmationUi(html);
        var injected = Encoding.UTF8.GetString(bytes);

        injected.Should().Contain(DshPendingConfirmationUi.ScriptTag);
        injected.Should().Contain("</script></body>");
        var second = Encoding.UTF8.GetString(DshWebHost.InjectPendingConfirmationUi(injected));
        second.Should().Be(injected, "已注入标记时不得重复插入脚本");
    }
}
