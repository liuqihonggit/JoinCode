namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// session/updateQueue 队列编辑测试（TASK058 P2-B3）：mode=queue 排队项的 remove/edit 随机访问；
/// steer 返回 session/steer-unavailable（引擎无轮内注入点，诚实引导）；操作不存在项返回 queue-item-not-found。
/// </summary>
public sealed class DshUpdateQueueTests {
    private static JsonElement QueueArgs(string sessionId, string itemId, string kind, string? text = null) =>
        text is null
            ? JsonSerializer.SerializeToElement(new {
                sessionId, itemId,
                action = new { kind },
            })
            : JsonSerializer.SerializeToElement(new {
                sessionId, itemId,
                action = new { kind, content = new[] { new { type = "text", text } } },
            });

    [Fact]
    public async Task UpdateQueue_Remove_DeletesQueuedItem() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        // 第一条占住轮（引擎 busy — TurnGate 挂起 StreamAsync 不返回）
        var first = await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问"));
        first.Ok.Should().BeTrue(first.Error?.Message);
        // 第二/三条排队
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第二问", queue: true))).Ok.Should().BeTrue();
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第三问", queue: true))).Ok.Should().BeTrue();
        var items = dispatcher.ListQueue(sessionId);
        items.Should().HaveCount(2);
        var victim = items[0].ItemId;

        var result = await dispatcher.HandleAsync("session/updateQueue", QueueArgs(sessionId, victim, "remove"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        dispatcher.ListQueue(sessionId).Should().ContainSingle().Which.Text.Should().Be("第三问");
        } finally { hold.TrySetCanceled(); }
    }

    [Fact]
    public async Task UpdateQueue_Edit_RewritesQueuedText() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问"))).Ok.Should().BeTrue();
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "旧文本", queue: true))).Ok.Should().BeTrue();
        var itemId = dispatcher.ListQueue(sessionId).Single().ItemId;

        var result = await dispatcher.HandleAsync("session/updateQueue", QueueArgs(sessionId, itemId, "edit", "新文本"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        dispatcher.ListQueue(sessionId).Single().Text.Should().Be("新文本");
        } finally { hold.TrySetCanceled(); }
    }

    [Fact]
    public async Task UpdateQueue_Steer_ReturnsStableUnavailable() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问"))).Ok.Should().BeTrue();
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "排队问", queue: true))).Ok.Should().BeTrue();
        var itemId = dispatcher.ListQueue(sessionId).Single().ItemId;

        var result = await dispatcher.HandleAsync("session/updateQueue", QueueArgs(sessionId, itemId, "steer"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/steer-unavailable");
        result.Error?.Message.Should().Contain("排队", "引导用户该消息将在下一轮执行");
        } finally { hold.TrySetCanceled(); }
    }

    [Fact]
    public async Task UpdateQueue_UnknownItem_ReturnsNotFound() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问"))).Ok.Should().BeTrue();

        var result = await dispatcher.HandleAsync("session/updateQueue", QueueArgs(sessionId, "no-such-item", "remove"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/queue-item-not-found");
        } finally { hold.TrySetCanceled(); }
    }

    private static JsonElement PromptArgs(string sessionId, string text, bool queue = false) =>
        JsonSerializer.SerializeToElement(new {
            request = new {
                requestId = Guid.NewGuid().ToString("N"),
                sessionId,
                mode = queue ? "queue" : "send",
                content = new[] { new { type = "text", text } },
            },
        });
}
