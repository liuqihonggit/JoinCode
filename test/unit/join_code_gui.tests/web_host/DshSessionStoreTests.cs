namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 会话投影测试（ADR 0143 M2-b）：引擎会话存储（真实 <see cref="State.TranscriptService"/> + 内存文件系统）
/// 投影成 DSH 会话列表的形状，含标题投影、空白标记、工作目录与重命名落库；以及分发器路由的校验与引导错误。
/// </summary>
public sealed class DshSessionStoreTests {
    private const string SessionsDir = "/test/sessions";

    /// <summary>建一个接了真实引擎会话存储的注册表</summary>
    private static (DshSessionStore Store, State.TranscriptService Transcripts) CreateStore() {
        var fs = new InMemoryFileSystem();
        var transcripts = new State.TranscriptService(fs, SessionsDir);
        var store = new DshSessionStore();
        store.AttachTranscripts(transcripts);
        return (store, transcripts);
    }

    [Fact]
    public async Task List_WithoutEngine_ReturnsEmpty() {
        var store = new DshSessionStore();
        (await store.ListAsync(50, CancellationToken.None)).Should().BeEmpty();
        store.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_WithoutEngine_ReturnsNull() {
        var store = new DshSessionStore();
        (await store.RenameAsync("s1", "标题", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task List_ProjectsEngineSession_IntoDshShape() {
        var (store, transcripts) = CreateStore();
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry {
            Role = "user",
            Content = "写个斐波那契",
            Timestamp = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
        });
        await transcripts.SaveSessionInfoAsync("s1", new SessionInfo {
            Id = "s1",
            ProjectPath = @"C:\demo\proj-a",
            Vendor = "deepseek",
            ModelId = "deepseek-chat",
        });

        var items = await store.ListAsync(50, CancellationToken.None);

        items.Should().ContainSingle();
        var item = items[0];
        item.SessionId.Should().Be("s1");
        item.Blank.Should().BeFalse();
        item.AgentAvailable.Should().BeTrue();
        item.Running.Should().BeFalse();
        item.Cwd.Should().Be(@"C:\demo\proj-a");
        item.UpdatedAt.Should().BeGreaterThan(0);
        item.Projections.Kind.Should().Be("sequenced");
        item.Projections.Values.Title.Should().BeNull();
        item.Projections.Values.AgentPreset.Should().Be("standard");
        item.Projections.Values.SessionListMetadata.Should().NotBeNull();
        item.Projections.Values.SessionListMetadata.Blank.Should().BeFalse();
        item.Projections.Values.SessionListMetadata.LastPromptAt.Should().Be(
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
        item.Projections.Values.ModelSelection.Should().NotBeNull();
        item.Projections.Values.ModelSelection.LastUsed.Should().NotBeNull();
        item.Projections.Values.ModelSelection.LastUsed.Provider.Should().Be("deepseek");
        item.Projections.Values.ModelSelection.LastUsed.Model.Should().Be("deepseek-chat");
        item.Projections.Values.ModelSelection.Next.Should().BeNull();
    }

    [Fact]
    public async Task Rename_WritesCustomTitle_VisibleInList() {
        var (store, transcripts) = CreateStore();
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "hi" });

        var renamed = await store.RenameAsync("s1", "斐波那契任务", CancellationToken.None);

        renamed.Should().NotBeNull();
        renamed.Value.Title.Should().Be("斐波那契任务");
        var titles = await store.ListTitlesAsync(50, CancellationToken.None);
        titles.Should().ContainSingle();
        titles[0].Title.Should().Be("斐波那契任务");
    }

    [Fact]
    public async Task Rename_UnknownSession_ReturnsNull() {
        var (store, _) = CreateStore();
        (await store.RenameAsync("missing", "x", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Dispatcher_SessionList_ReturnsEngineItems() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "hi" });
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/list", default);

        result.Ok.Should().BeTrue(result.Error?.Message);
        var items = result.Value.GetValueOrDefault().GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("sessionId").GetString().Should().Be("s1");
        items[0].GetProperty("projections").GetProperty("kind").GetString().Should().Be("sequenced");
    }

    [Fact]
    public async Task Dispatcher_SessionRename_ValidatesAndGuides() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "hi" });
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);

        var missing = await dispatcher.HandleAsync("session/rename", RenameArgs("", "标题"));
        missing.Ok.Should().BeFalse();
        missing.Error?.Code.Should().Be("session/rejected");

        var noTitle = await dispatcher.HandleAsync("session/rename", RenameArgs("s1", ""));
        noTitle.Ok.Should().BeFalse();
        noTitle.Error?.Code.Should().Be("session/rejected");

        var notFound = await dispatcher.HandleAsync("session/rename", RenameArgs("nope", "标题"));
        notFound.Ok.Should().BeFalse();
        notFound.Error?.Code.Should().Be("session/not-found");

        var renamed = await dispatcher.HandleAsync("session/rename", RenameArgs("s1", "新标题"));
        renamed.Ok.Should().BeTrue(renamed.Error?.Message);
        renamed.Value.GetValueOrDefault().GetProperty("title").GetString().Should().Be("新标题");
    }

    [Fact]
    public async Task Journal_SurvivesRestart_AndPageEndsAtRequestedCursor() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.EnsureSessionAsync("s1", new SessionInfo { Id = "s1", ProjectPath = "/ws" });
        var first = new DshHostServices(fs, "/test/dsh-workspaces.json", "/test/dsh_journal");
        first.Sessions.AttachTranscripts(transcripts);

        await first.Streams.PublishAsync("s1", "turn/start", new DshTurnStartData { Turn = 1 });
        await first.Streams.PublishAsync("s1", "step/start", new DshStepData { Turn = 1, Step = 1 });
        await first.Streams.PublishAsync("s1", "tool/call", new DshToolCallData { Turn = 1, Step = 1, CallId = "c1", Name = "StubTool", Arguments = "{}" });
        await first.Streams.PublishAsync("s1", "turn/end", new DshTurnEndData { Turn = 1, Reason = new DshTurnEndReason { Kind = "completed" } });

        var second = new DshHostServices(fs, "/test/dsh-workspaces.json", "/test/dsh_journal");
        second.Sessions.AttachTranscripts(transcripts);
        var snapshot = await second.Sessions.SnapshotAsync("s1", CancellationToken.None, second.Streams);

        var restored = snapshot ?? throw new InvalidOperationException("重启后必须能打开会话");
        restored.Cursor.Should().Be(3, "事件序号必须跨进程续写，不能回落到转录条目数");
        restored.Records.Should().HaveCount(4);
        restored.Records[2].Event.Type.Should().Be("tool/call", "工具事件不落引擎转录，必须由宿主日志保留");

        var dispatcher = new DshRpcDispatcher(second);
        var page = await dispatcher.HandleAsync("session/page", PageArgs("s1", 3, maxMessages: 2));
        page.Ok.Should().BeTrue(page.Error?.Message);
        var records = page.Value.GetValueOrDefault().GetProperty("records");
        records.GetArrayLength().Should().Be(2);
        records[1].GetProperty("event").GetProperty("seq").GetInt64().Should().Be(3, "翻页必须恰好结束在请求光标");
        page.Value.GetValueOrDefault().GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Dispatcher_MessageFeedbackList_ReturnsEmptySuccess() {
        var services = new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json");
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("messageFeedback/list", Args(new DshMessageFeedbackListRequest { SessionId = "s1" }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("ok").GetBoolean().Should().BeTrue("Remote 契约自带成功包装");
        value.GetProperty("value").GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_SessionPage_ReturnsOlderRecords() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "第一条" });
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "assistant", Content = "第一答" });
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "第二条" });
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "assistant", Content = "第二答" });
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);

        var snapshot = await services.Sessions.SnapshotAsync("s1", CancellationToken.None);
        var cursor = snapshot?.Cursor ?? throw new InvalidOperationException("会话快照必须存在");

        var page = await dispatcher.HandleAsync("session/page", PageArgs("s1", cursor, beforeSeq: cursor, maxMessages: 2));
        page.Ok.Should().BeTrue(page.Error?.Message);
        var records = page.Value.GetValueOrDefault().GetProperty("records");
        records.GetArrayLength().Should().Be(2);
        records[0].GetProperty("event").GetProperty("seq").GetInt64().Should().Be(cursor - 1, "页尾必须恰好结束在 throughSeq");
        records[1].GetProperty("event").GetProperty("seq").GetInt64().Should().Be(cursor);
        page.Value.GetValueOrDefault().GetProperty("hasMore").GetBoolean().Should().BeTrue("更早还有记录");

        var missing = await dispatcher.HandleAsync("session/page", PageArgs("nope", cursor));
        missing.Ok.Should().BeFalse();
        missing.Error?.Code.Should().Be("session/not-found");
    }

    [Fact]
    public async Task Dispatcher_SessionCancel_AcceptsKnownSession() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json"));
        var result = await dispatcher.HandleAsync("session/cancel", Args(new DshSessionCancelArgs { SessionId = "s1" }));
        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("accepted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Dispatcher_SessionCancel_MissingSessionId_Guides() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json"));
        var result = await dispatcher.HandleAsync("session/cancel", default);
        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/rejected");
        result.Error?.Message.Should().Contain("sessionId");
    }

    [Fact]
    public async Task Projections_WithoutEngine_ReturnsNull() {
        var store = new DshSessionStore();
        (await store.ProjectionsAsync("s1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Projections_UnknownSession_ReturnsNull() {
        var (store, _) = CreateStore();
        (await store.ProjectionsAsync("missing", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Projections_BlankSession_ReportedBlank() {
        var (store, transcripts) = CreateStore();
        await transcripts.EnsureSessionAsync("fresh", new SessionInfo {
            Id = "fresh",
            ProjectPath = @"C:\demo\fresh",
            Vendor = "deepseek",
            ModelId = "deepseek-chat",
        });

        var projections = await store.ProjectionsAsync("fresh", CancellationToken.None)
            ?? throw new InvalidOperationException("ProjectionsAsync 未返回会话投影");

        projections.AsOfSeq.Should().Be(0);
        projections.Values.Title.Should().BeNull();
        projections.Values.SessionListMetadata.Should().NotBeNull();
        projections.Values.SessionListMetadata.Blank.Should().BeTrue();
        projections.Values.ModelSelection.Should().NotBeNull();
        projections.Values.ModelSelection.Next.Should().BeNull();
    }

    [Fact]
    public async Task Create_EnsuresSessionEntityOnDisk() {
        var (store, transcripts) = CreateStore();

        var created = await store.CreateAsync(null, @"C:\demo\proj-b", "deepseek", "deepseek-chat", CancellationToken.None)
            ?? throw new InvalidOperationException("CreateAsync 未返回会话");

        created.SessionId.Should().NotBeNullOrWhiteSpace();
        created.AgentPreset.Should().Be("standard");
        (await transcripts.TranscriptExistsAsync(created.SessionId)).Should().BeTrue();
        var info = await transcripts.GetSessionInfoAsync(created.SessionId)
            ?? throw new InvalidOperationException("CreateAsync 未写出 meta.json");
        info.ProjectPath.Should().Be(@"C:\demo\proj-b");
        info.Vendor.Should().Be("deepseek");
        info.ModelId.Should().Be("deepseek-chat");
        var items = await store.ListAsync(50, CancellationToken.None);
        items.Should().ContainSingle();
        items[0].SessionId.Should().Be(created.SessionId);
        items[0].Blank.Should().BeTrue();
        items[0].Cwd.Should().Be(@"C:\demo\proj-b");
    }

    [Fact]
    public async Task Create_WithRequestedSessionId_KeepsExistingTranscript() {
        var (store, transcripts) = CreateStore();
        await transcripts.AppendEntryAsync("kept", new TranscriptEntry { Role = "user", Content = "已有内容" });

        var created = await store.CreateAsync("kept", @"C:\demo\proj-c", "deepseek", "deepseek-chat", CancellationToken.None)
            ?? throw new InvalidOperationException("CreateAsync 未返回会话");

        created.SessionId.Should().Be("kept");
        var entries = await transcripts.LoadTranscriptAsync("kept");
        entries.Should().ContainSingle();
        var items = await store.ListAsync(50, CancellationToken.None);
        items.Should().ContainSingle();
        items[0].Blank.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_WithoutEngine_ReturnsNull() {
        var store = new DshSessionStore();
        (await store.SnapshotAsync("s1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_UnknownSession_ReturnsNull() {
        var (store, _) = CreateStore();
        (await store.SnapshotAsync("missing", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_BlankSession_OpensEmptyJournalWithProjections() {
        var (store, transcripts) = CreateStore();
        await transcripts.EnsureSessionAsync("blank-1", new SessionInfo {
            Id = "blank-1",
            ProjectPath = @"C:\demo\proj-e",
            CreatedAt = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
        });

        var snapshot = await store.SnapshotAsync("blank-1", CancellationToken.None)
            ?? throw new InvalidOperationException("SnapshotAsync 未返回快照");

        snapshot.Type.Should().Be("snapshot");
        snapshot.Header.Id.Should().Be("blank-1");
        snapshot.Header.Cwd.Should().Be(@"C:\demo\proj-e");
        snapshot.Header.CreatedAt.Should().BeGreaterThan(0);
        snapshot.Header.AgentPreset.Should().Be("standard");
        snapshot.Header.IsSeeded.Should().BeTrue();
        snapshot.Cursor.Should().Be(-1);
        snapshot.Records.Should().BeEmpty();
        snapshot.HasMore.Should().BeFalse();
        snapshot.Projections.AsOfSeq.Should().Be(0);
        snapshot.Projections.Values.SessionListMetadata.Should().NotBeNull();
        snapshot.Projections.Values.SessionListMetadata.Blank.Should().BeTrue();
        snapshot.AssistantStream.Revision.Should().Be(0);
    }

    [Fact]
    public async Task Snapshot_WithMessages_ReportsSeqAndTitle() {
        var (store, transcripts) = CreateStore();
        var promptAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        await transcripts.AppendEntryAsync("s2", new TranscriptEntry { Role = "user", Content = "hi", Timestamp = promptAt });
        await transcripts.AppendEntryAsync("s2", new TranscriptEntry { Role = "assistant", Content = "yo", Timestamp = promptAt.AddSeconds(2) });
        await transcripts.SaveCustomTitleAsync("s2", "聊两句");

        var snapshot = await store.SnapshotAsync("s2", CancellationToken.None)
            ?? throw new InvalidOperationException("SnapshotAsync 未返回快照");

        snapshot.Projections.AsOfSeq.Should().Be(3);
        snapshot.Projections.Values.Title.Should().Be("聊两句");
        snapshot.Projections.Values.SessionListMetadata.Should().NotBeNull();
        snapshot.Projections.Values.SessionListMetadata.Blank.Should().BeFalse();
        snapshot.Projections.Values.SessionListMetadata.LastPromptAt.Should().Be(
            new DateTimeOffset(promptAt).ToUnixTimeMilliseconds());
        snapshot.Records.Select(record => record.Event.Type).Should().Equal(
            "turn/start", "step/start", "user/message", "assistant/message", "step/end", "turn/end");
        snapshot.Cursor.Should().Be(snapshot.Records[^1].Event.Seq);
        snapshot.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task List_WithUserMessage_ReportsLastPromptAt() {
        var (store, transcripts) = CreateStore();
        var promptAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        await transcripts.AppendEntryAsync("s3", new TranscriptEntry { Role = "user", Content = "hi", Timestamp = promptAt });

        var items = await store.ListAsync(10, CancellationToken.None);

        var item = items.Single(entry => entry.SessionId == "s3");
        item.Blank.Should().BeFalse();
        var metadata = item.Projections.Values.SessionListMetadata
            ?? throw new InvalidOperationException("列表条目缺少 sessionListMetadata 投影");
        metadata.LastPromptAt.Should().Be(new DateTimeOffset(promptAt).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void FollowSessionId_DirectSessionAddress_ReturnsId() {
        DshWebHost.FollowSessionId(FollowOpen("session", "s1")).Should().Be("s1");
    }

    [Fact]
    public void FollowSessionId_SubagentAddress_ReturnsNull() {
        DshWebHost.FollowSessionId(FollowOpen("subagent", "child")).Should().BeNull();
    }

    [Fact]
    public void FollowSessionId_MissingPayload_ReturnsNull() {
        DshWebHost.FollowSessionId(new DshStreamOpen { Type = "open", StreamId = "x", Endpoint = "session/follow" }).Should().BeNull();
    }

    [Fact]
    public async Task Dispatcher_SessionCreate_EnsuresEngineSession() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/create", CreateArgs(@"C:\demo\proj-d"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var sessionId = result.Value.GetValueOrDefault().GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("session/create 未返回 sessionId");
        sessionId.Should().NotBeNullOrWhiteSpace();
        (await transcripts.TranscriptExistsAsync(sessionId)).Should().BeTrue();
        var info = await transcripts.GetSessionInfoAsync(sessionId)
            ?? throw new InvalidOperationException("session/create 未写出 meta.json");
        info.ProjectPath.Should().Be(@"C:\demo\proj-d");
    }

    [Fact]
    public async Task Dispatcher_SessionProjections_ReturnsValuesOrNull() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        await transcripts.EnsureSessionAsync("blank-1", new SessionInfo { Id = "blank-1", ProjectPath = @"C:\demo\p" });
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);

        var found = await dispatcher.HandleAsync("session/projections", ProjectionsArgs("blank-1"));
        found.Ok.Should().BeTrue(found.Error?.Message);
        found.Value.GetValueOrDefault().GetProperty("asOfSeq").GetInt64().Should().Be(0);
        found.Value.GetValueOrDefault().GetProperty("values").GetProperty("sessionListMetadata").GetProperty("blank").GetBoolean().Should().BeTrue();

        var missing = await dispatcher.HandleAsync("session/projections", ProjectionsArgs("nope"));
        missing.Ok.Should().BeTrue(missing.Error?.Message);
        missing.Value.GetValueOrDefault().ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Dispatcher_SessionCreate_WithWorkspaceId_UsesWorkspacePathAndAttaches() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\demo\ws-1");
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json");
        services.Sessions.AttachTranscripts(transcripts);
        var dispatcher = new DshRpcDispatcher(services);
        var (workspace, _) = await services.Workspaces.CreateAsync(@"C:\demo\ws-1");

        var result = await dispatcher.HandleAsync("session/create", WorkspaceCreateArgs(workspace.WorkspaceId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var sessionId = result.Value.GetValueOrDefault().GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("session/create 未返回 sessionId");
        var info = await transcripts.GetSessionInfoAsync(sessionId)
            ?? throw new InvalidOperationException("session/create 未写出 meta.json");
        info.ProjectPath.Should().Be(@"C:\demo\ws-1");
        var reloaded = await services.Workspaces.FindAsync(workspace.WorkspaceId)
            ?? throw new InvalidOperationException("工作区注册表丢失");
        reloaded.SessionIds.Should().Contain(sessionId);
    }

    [Fact]
    public async Task Dispatcher_SessionCreate_WithUnknownWorkspaceId_Guides() {
        var services = new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json");
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/create", WorkspaceCreateArgs("missing"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/rejected");
        result.Error?.Message.Should().Contain("workspace/create");
    }

    /// <summary>按 DSH 远程契约形状构造 session/follow 的流打开帧</summary>
    private static DshStreamOpen FollowOpen(string kind, string sessionId) =>
        new() {
            Type = "open",
            StreamId = "stream-1",
            Endpoint = "session/follow",
            Payload = new DshRpcRequestPayload {
                Args = JsonSerializer.SerializeToElement(
                    new DshRequestEnvelope<DshSessionFollowArgs> {
                        Request = new DshSessionFollowArgs {
                            Address = new DshSessionAddress { Kind = kind, SessionId = sessionId },
                            AssistantStream = true,
                        },
                    },
                    DshJsonContext.Default.DshRequestEnvelopeDshSessionFollowArgs),
            },
        };

    /// <summary>按 DSH 远程契约形状构造 session/create 参数（具名参数在 request 下）</summary>
    private static JsonElement CreateArgs(string cwd) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionCreateArgs> { Request = new DshSessionCreateArgs { Cwd = cwd } },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionCreateArgs);

    /// <summary>按 workspaceId 构造 session/create 参数</summary>
    private static JsonElement WorkspaceCreateArgs(string workspaceId) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionCreateArgs> { Request = new DshSessionCreateArgs { WorkspaceId = workspaceId } },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionCreateArgs);

    /// <summary>按 DSH 远程契约形状构造 session/page 参数（具名参数在 request 下）</summary>
    private static JsonElement PageArgs(string sessionId, long throughSeq, long? beforeSeq = null, int? maxMessages = null) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionPageArgs> {
                Request = new DshSessionPageArgs {
                    Address = new DshSessionAddress { Kind = "session", SessionId = sessionId },
                    ThroughSeq = throughSeq,
                    BeforeSeq = beforeSeq,
                    MaxMessages = maxMessages,
                },
            },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionPageArgs);

    /// <summary>按 DSH 远程契约形状构造 session/projections 参数</summary>
    private static JsonElement ProjectionsArgs(string sessionId) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionProjectionsArgs> { Request = new DshSessionProjectionsArgs { SessionId = sessionId } },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionProjectionsArgs);

    /// <summary>按 DSH 远程契约形状构造 session/rename 参数（具名参数在 request 下）</summary>
    private static JsonElement RenameArgs(string sessionId, string title) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionRenameArgs> { Request = new DshSessionRenameArgs { SessionId = sessionId, Title = title } },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionRenameArgs);

    /// <summary>按 DSH 远程契约形状构造 session/cancel 参数（具名参数在 request 下）</summary>
    private static JsonElement Args(DshMessageFeedbackListRequest request) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshMessageFeedbackListRequest> { Request = request },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackListRequest);

    private static JsonElement Args(DshSessionCancelArgs request) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionCancelArgs> { Request = request },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionCancelArgs);
}
