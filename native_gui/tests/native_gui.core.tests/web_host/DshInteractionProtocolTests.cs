namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// TASK066 C4 交互协议测试：会话搜索、settings 整段替换/顶层补丁、代理预设、
/// 工作区路径打开方式与消息反馈（含乐观并发、错误联合体）。
/// </summary>
public sealed class DshInteractionProtocolTests {
    private const string SessionsDir = "/test/sessions";

    /// <summary>建一个带真实转录存储的宿主服务与分发器</summary>
    private static async Task<(DshHostServices Services, DshRpcDispatcher Dispatcher, State.TranscriptService Transcripts)> CreateHostAsync() {
        var fs = new InMemoryFileSystem();
        var transcripts = new State.TranscriptService(fs, SessionsDir);
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json", "/test/dsh_journal");
        services.Sessions.AttachTranscripts(transcripts);
        await services.Settings.InitializeAsync(CancellationToken.None);
        await services.Feedback.InitializeAsync(CancellationToken.None);
        return (services, new DshRpcDispatcher(services), transcripts);
    }

    /// <summary>按 DSH 远程契约构造 request 信封参数</summary>
    private static JsonElement Envelope<T>(T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<DshRequestEnvelope<T>> typeInfo)
        where T : new() =>
        JsonSerializer.SerializeToElement(new DshRequestEnvelope<T> { Request = payload }, typeInfo);

    [Fact]
    public async Task Dispatcher_SessionSearch_FindsLiteralAndCapsAtTwenty() {
        var (_, dispatcher, transcripts) = await CreateHostAsync();
        for (var index = 0; index < 21; index++) {
            await transcripts.AppendEntryAsync($"s{index}", new TranscriptEntry {
                Role = "user",
                Content = $"请帮我排查缓存问题 #{index}",
                Timestamp = DateTime.UtcNow.AddSeconds(index),
            });
        }
        await transcripts.AppendEntryAsync("s0", new TranscriptEntry { Role = "assistant", Content = "缓存命中率偏低" });

        var result = await dispatcher.HandleAsync("session/search", Envelope(
            new DshSessionSearchArgs { Query = "缓存" },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionSearchArgs));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("items").GetArrayLength().Should().Be(20);
        value.GetProperty("hasMore").GetBoolean().Should().BeTrue("第 21 条命中只用于置位 hasMore");
        value.GetProperty("items")[0].GetProperty("snippet").GetString().Should().Contain("缓存");
    }

    [Fact]
    public async Task Dispatcher_SessionSearch_BlankQueryGuides() {
        var (_, dispatcher, _) = await CreateHostAsync();
        var result = await dispatcher.HandleAsync("session/search", Envelope(
            new DshSessionSearchArgs { Query = "   " },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionSearchArgs));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("gateway/bad-request");
        result.Error?.Message.Should().Contain("query");
    }

    [Fact]
    public async Task SessionSearch_SnippetIsWindowedAndTruncated() {
        var fs = new InMemoryFileSystem();
        await using var transcripts = new State.TranscriptService(fs, SessionsDir);
        var store = new DshSessionStore();
        store.AttachTranscripts(transcripts);
        var prefix = new string('前', 200);
        var suffix = new string('后', 200);
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry {
            Role = "assistant",
            Content = $"{prefix}关键词{suffix}",
        });

        var (items, hasMore) = await store.SearchAsync("关键词", CancellationToken.None);

        hasMore.Should().BeFalse();
        var snippet = items.Single().Snippet;
        snippet.Should().StartWith("…").And.EndWith("…");
        snippet.Should().Contain("关键词");
        snippet.Length.Should().BeLessThanOrEqualTo(242, "240 字符窗口 + 前后省略号各 1 字符");
    }

    [Fact]
    public void SettingsReplace_ReplacesWholeNamespace() {
        var store = new DshSettingsStore(new InMemoryFileSystem());
        _ = store.Mutate(JsonSerializer.SerializeToElement(
            new DshSettingsMutateArgs {
                Ns = "demo",
                Ops = [new DshSettingsOp { Op = "set", Path = ["old"], Value = JsonDocument.Parse("1").RootElement.Clone() }],
            },
            DshJsonContext.Default.DshSettingsMutateArgs));

        var result = store.Replace(JsonSerializer.SerializeToElement(
            new DshSettingsReplaceArgs { Ns = "demo", Section = JsonDocument.Parse("""{"name":"新值"}""").RootElement.Clone() },
            DshJsonContext.Default.DshSettingsReplaceArgs));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault().GetProperty("value");
        value.GetProperty("name").GetString().Should().Be("新值");
        value.TryGetProperty("old", out _).Should().BeFalse("replace 是整段替换，不保留旧字段");
        result.Value.GetValueOrDefault().GetProperty("revision").GetInt32().Should().Be(2);
    }

    [Fact]
    public void SettingsUpdate_MergesTopLevelFieldsAndDeletesNull() {
        var store = new DshSettingsStore(new InMemoryFileSystem());
        _ = store.Replace(JsonSerializer.SerializeToElement(
            new DshSettingsReplaceArgs {
                Ns = "demo",
                Section = JsonDocument.Parse("""{"keep":"a","drop":1,"nested":{"x":1,"y":2}}""").RootElement.Clone(),
            },
            DshJsonContext.Default.DshSettingsReplaceArgs));

        var result = store.Update(JsonSerializer.SerializeToElement(
            new DshSettingsUpdateArgs {
                Ns = "demo",
                Patch = JsonDocument.Parse("""{"drop":null,"nested":{"x":9},"added":true}""").RootElement.Clone(),
                ExpectedRevision = 1,
            },
            DshJsonContext.Default.DshSettingsUpdateArgs));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault().GetProperty("value");
        value.GetProperty("keep").GetString().Should().Be("a");
        value.TryGetProperty("drop", out _).Should().BeFalse("null 表示删除字段");
        value.GetProperty("nested").GetProperty("x").GetInt32().Should().Be(9);
        value.GetProperty("nested").TryGetProperty("y", out _).Should().BeFalse("顶层字段补丁整段替换该字段");
        value.GetProperty("added").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void SettingsUpdate_StaleRevisionFailsWithConflict() {
        var store = new DshSettingsStore(new InMemoryFileSystem());
        _ = store.Replace(JsonSerializer.SerializeToElement(
            new DshSettingsReplaceArgs { Ns = "demo", Section = JsonDocument.Parse("""{"x":1}""").RootElement.Clone() },
            DshJsonContext.Default.DshSettingsReplaceArgs));

        var result = store.Update(JsonSerializer.SerializeToElement(
            new DshSettingsUpdateArgs {
                Ns = "demo",
                Patch = JsonDocument.Parse("""{"x":2}""").RootElement.Clone(),
                ExpectedRevision = 0,
            },
            DshJsonContext.Default.DshSettingsUpdateArgs));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/conflict");
    }

    [Fact]
    public async Task Dispatcher_AgentPresets_ListReadSelect() {
        var (_, dispatcher, _) = await CreateHostAsync();

        var list = await dispatcher.HandleAsync("agentPresets/list", default);
        list.Ok.Should().BeTrue(list.Error?.Message);
        var preset = list.Value.GetValueOrDefault().GetProperty("presets")[0];
        preset.GetProperty("id").GetString().Should().Be("standard");
        preset.GetProperty("isDefault").GetBoolean().Should().BeTrue();

        var read = await dispatcher.HandleAsync("agentPresets/read", JsonSerializer.SerializeToElement("standard", DshJsonContext.Default.String));
        read.Ok.Should().BeTrue(read.Error?.Message);
        read.Value.GetValueOrDefault().GetProperty("agentPreset").GetString().Should().Be("standard");
        read.Value.GetValueOrDefault().GetProperty("content").GetString().Should().NotBeNullOrWhiteSpace();

        var select = await dispatcher.HandleAsync("agentPresets/select", JsonSerializer.SerializeToElement(
            new DshAgentPresetSelectArgs { AgentId = "s1", AgentPreset = "standard" },
            DshJsonContext.Default.DshAgentPresetSelectArgs));
        select.Ok.Should().BeTrue(select.Error?.Message);
        select.Value.GetValueOrDefault().GetString().Should().Be("standard");

        var missing = await dispatcher.HandleAsync("agentPresets/read", JsonSerializer.SerializeToElement("unknown", DshJsonContext.Default.String));
        missing.Ok.Should().BeFalse();
        missing.Error?.Code.Should().Be("agentPreset/not-found");
    }

    [Fact]
    public async Task Dispatcher_WorkspacePathApplications_OnlyOffersExplorerForExistingPath() {
        var (services, dispatcher, _) = await CreateHostAsync();
        var directory = Path.GetFullPath("/demo/ws-1");
        services.FileSystem.CreateDirectory(directory);

        var found = await dispatcher.HandleAsync("session/workspacePathApplications", Envelope(
            new DshWorkspacePathAppsArgs { Path = directory },
            DshJsonContext.Default.DshRequestEnvelopeDshWorkspacePathAppsArgs));
        found.Ok.Should().BeTrue(found.Error?.Message);
        var app = found.Value.GetValueOrDefault()[0];
        app.GetProperty("id").GetString().Should().Be("explorer");
        app.GetProperty("default").GetBoolean().Should().BeTrue();
        app.GetProperty("icon").ValueKind.Should().Be(JsonValueKind.Null, "协议要求 icon 字段存在，无图标为 null");

        var missing = await dispatcher.HandleAsync("session/workspacePathApplications", Envelope(
            new DshWorkspacePathAppsArgs { Path = Path.Combine(directory, "nope") },
            DshJsonContext.Default.DshRequestEnvelopeDshWorkspacePathAppsArgs));
        missing.Ok.Should().BeTrue(missing.Error?.Message);
        missing.Value.GetValueOrDefault().GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_OpenWorkspacePath_RejectsInvalidInputWithGuidance() {
        var (services, dispatcher, _) = await CreateHostAsync();
        var directory = Path.GetFullPath("/demo/ws-2");
        services.FileSystem.CreateDirectory(directory);

        var relative = await dispatcher.HandleAsync("session/openWorkspacePath", Envelope(
            new DshOpenWorkspacePathArgs { Path = "relative/path" },
            DshJsonContext.Default.DshRequestEnvelopeDshOpenWorkspacePathArgs));
        relative.Ok.Should().BeFalse();
        relative.Error?.Code.Should().Be("gateway/bad-request");
        relative.Error?.Message.Should().Contain("绝对路径");

        var badAction = await dispatcher.HandleAsync("session/openWorkspacePath", Envelope(
            new DshOpenWorkspacePathArgs { Path = directory, Action = "download" },
            DshJsonContext.Default.DshRequestEnvelopeDshOpenWorkspacePathArgs));
        badAction.Ok.Should().BeFalse();
        badAction.Error?.Message.Should().Contain("reveal");

        var badApp = await dispatcher.HandleAsync("session/openWorkspacePath", Envelope(
            new DshOpenWorkspacePathArgs { Path = directory, Application = "powershell" },
            DshJsonContext.Default.DshRequestEnvelopeDshOpenWorkspacePathArgs));
        badApp.Ok.Should().BeFalse();
        badApp.Error?.Message.Should().Contain("explorer");
    }

    [Fact]
    public async Task Dispatcher_MessageFeedback_PutListDelete_RoundTrip() {
        var (_, dispatcher, transcripts) = await CreateHostAsync();
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "你好" });
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "assistant", Content = "答复" });
        var assistantId = AssistantMessageId(await transcripts.LoadTranscriptAsync("s1"));

        var put = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs {
                SessionId = "s1",
                MessageId = assistantId,
                Rating = "positive",
                Note = "回答准确",
                Category = "task-result",
                IfVersion = null,
            },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        put.Ok.Should().BeTrue(put.Error?.Message);
        var saved = put.Value.GetValueOrDefault();
        saved.GetProperty("ok").GetBoolean().Should().BeTrue();
        var version = saved.GetProperty("value").GetProperty("version").GetString();
        version.Should().Be("1");

        var list = await dispatcher.HandleAsync("messageFeedback/list", Envelope(
            new DshMessageFeedbackListRequest { SessionId = "s1" },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackListRequest));
        list.Ok.Should().BeTrue(list.Error?.Message);
        list.Value.GetValueOrDefault().GetProperty("value").GetProperty("items").GetArrayLength().Should().Be(1);

        var delete = await dispatcher.HandleAsync("messageFeedback/delete", Envelope(
            new DshMessageFeedbackDeleteArgs { SessionId = "s1", MessageId = assistantId, IfVersion = version },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackDeleteArgs));
        delete.Ok.Should().BeTrue(delete.Error?.Message);
        delete.Value.GetValueOrDefault().GetProperty("value").GetProperty("absent").GetBoolean().Should().BeTrue();

        var after = await dispatcher.HandleAsync("messageFeedback/list", Envelope(
            new DshMessageFeedbackListRequest { SessionId = "s1" },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackListRequest));
        after.Value.GetValueOrDefault().GetProperty("value").GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_MessageFeedback_VersionConflictReturnsCurrent() {
        var (_, dispatcher, transcripts) = await CreateHostAsync();
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "assistant", Content = "答复" });
        var assistantId = AssistantMessageId(await transcripts.LoadTranscriptAsync("s1"));

        var first = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs { SessionId = "s1", MessageId = assistantId, Rating = "positive", IfVersion = null },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        first.Ok.Should().BeTrue(first.Error?.Message);

        var stale = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs { SessionId = "s1", MessageId = assistantId, Rating = "negative", IfVersion = "999" },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        stale.Ok.Should().BeTrue(stale.Error?.Message);
        var value = stale.Value.GetValueOrDefault();
        value.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = value.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("version-conflict");
        error.GetProperty("current").GetProperty("rating").GetString().Should().Be("positive");
        error.GetProperty("current").GetProperty("version").GetString().Should().Be("1");
    }

    [Fact]
    public async Task Dispatcher_MessageFeedback_RejectsMissingTargetAndBadNote() {
        var (_, dispatcher, transcripts) = await CreateHostAsync();
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "你好" });

        var missingTarget = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs { SessionId = "s1", MessageId = "s1:0", Rating = "positive", IfVersion = null },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        missingTarget.Ok.Should().BeTrue(missingTarget.Error?.Message);
        missingTarget.Value.GetValueOrDefault().GetProperty("error").GetProperty("code").GetString().Should().Be("target-not-found");

        var noSession = await dispatcher.HandleAsync("messageFeedback/list", Envelope(
            new DshMessageFeedbackListRequest { SessionId = "missing" },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackListRequest));
        noSession.Ok.Should().BeTrue(noSession.Error?.Message);
        noSession.Value.GetValueOrDefault().GetProperty("error").GetProperty("code").GetString().Should().Be("session-not-found");

        var blankNote = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs { SessionId = "s1", MessageId = "s1:0", Rating = "negative", Note = "   ", IfVersion = null },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        blankNote.Value.GetValueOrDefault().GetProperty("error").GetProperty("code").GetString().Should().Be("note-blank");

        var largeNote = await dispatcher.HandleAsync("messageFeedback/put", Envelope(
            new DshMessageFeedbackPutArgs {
                SessionId = "s1",
                MessageId = "s1:0",
                Rating = "negative",
                Note = new string('长', 1400),
                IfVersion = null,
            },
            DshJsonContext.Default.DshRequestEnvelopeDshMessageFeedbackPutArgs));
        var largeError = largeNote.Value.GetValueOrDefault().GetProperty("error");
        largeError.GetProperty("code").GetString().Should().Be("note-too-large");
        largeError.GetProperty("maxBytes").GetInt32().Should().Be(4096);
    }

    [Fact]
    public async Task FeedbackStore_PersistsAcrossInstances() {
        var fs = new InMemoryFileSystem();
        var first = new DshMessageFeedbackStore(fs, "/test/feedback.json");
        var saved = first.Put("s1", "m1", "positive", "不错", null, null).Saved;
        saved.Should().NotBeNull();

        var second = new DshMessageFeedbackStore(fs, "/test/feedback.json");
        await second.InitializeAsync(CancellationToken.None);
        var loaded = second.Get("s1", "m1")
            ?? throw new InvalidOperationException("反馈未从磁盘恢复");
        loaded.Rating.Should().Be("positive");
        loaded.Note.Should().Be("不错");
        loaded.Version.Should().Be("1");
    }

    /// <summary>从转录投影里取助手消息 ID（与前端消息 id 同源）</summary>
    private static string AssistantMessageId(IReadOnlyList<TranscriptEntry> entries) {
        var id = DshTranscriptProjector.Project("s1", entries)
            .Single(record => record.Event.Type == "assistant/message")
            .Event.Data.GetProperty("message").GetProperty("id").GetString();
        return id ?? throw new InvalidOperationException("投影未生成 assistant/message.id");
    }
}
