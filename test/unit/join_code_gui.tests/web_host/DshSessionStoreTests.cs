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
        item.Projections.Values.SessionListMetadata.LastPromptAt.Should().BeNull();
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
    public async Task Dispatcher_SessionCancel_Accepts() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new InMemoryFileSystem(), "/test/dsh-workspaces.json"));
        var result = await dispatcher.HandleAsync("session/cancel", default);
        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("accepted").GetBoolean().Should().BeTrue();
    }

    /// <summary>按 DSH 远程契约形状构造 session/rename 参数（具名参数在 request 下）</summary>
    private static JsonElement RenameArgs(string sessionId, string title) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionRenameArgs> { Request = new DshSessionRenameArgs { SessionId = sessionId, Title = title } },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionRenameArgs);
}
