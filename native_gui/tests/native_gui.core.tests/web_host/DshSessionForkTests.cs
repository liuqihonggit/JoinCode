// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 会话分叉测试（TASK058 B7）：session/fork 复制源会话（meta + 转录 + journal 事件）到新会话。
/// atSeq 截断属深化项（v1 全量复制，文档标注）。
/// </summary>
public sealed class DshSessionForkTests {
    /// <summary>构造带两条消息的源会话（真实 State.TranscriptService + 内存文件系统）+ 挂接好的服务容器</summary>
    private static async Task<(DshHostServices Services, State.TranscriptService Transcripts, string SourceId)> CreateSourceAsync() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, @"D:\proj\demo", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sourceId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        await transcripts.AppendEntriesAsync(sourceId, [
            new TranscriptEntry { Role = "user", Content = "第一问" },
            new TranscriptEntry { Role = "assistant", Content = "第一答" },
        ], CancellationToken.None);
        return (services, transcripts, sourceId);
    }

    /// <summary>构造 fork 请求（具名参数 request 信封）</summary>
    private static JsonElement ForkArgs(string sessionId, long? atSeq = null) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionForkArgs> {
                Request = new() { SessionId = sessionId, AtSeq = atSeq },
            }, DshJsonContext.Default.DshRequestEnvelopeDshSessionForkArgs);

    [Fact]
    public async Task Fork_UnknownSession_ReturnsGuidanceError() {
        var services = new DshHostServices(new InMemoryFileSystem());
        services.Sessions.AttachTranscripts(new State.TranscriptService(new InMemoryFileSystem(), "/test/sessions"));
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/fork", ForkArgs("no-such-session"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/unknown");
    }

    [Fact]
    public async Task Fork_CopiesHistoryToNewSessionId() {
        var (services, transcripts, sourceId) = await CreateSourceAsync();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/fork", ForkArgs(sourceId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var newId = result.Value.GetValueOrDefault().GetProperty("sessionId").GetString()
            ?? throw new Xunit.Sdk.XunitException("fork 未返回新会话 ID");
        newId.Should().NotBe(sourceId);
        // 新会话历史完整（两条消息）
        var entries = await transcripts.LoadTranscriptAsync(newId, CancellationToken.None);
        entries.Count.Should().Be(2);
        entries[0].Content.Should().Be("第一问");
        // 源会话不受影响
        (await transcripts.LoadTranscriptAsync(sourceId, CancellationToken.None)).Count.Should().Be(2);
    }

    [Fact]
    public async Task Fork_WithoutMetaFile_FallsBackToTranscriptOnlySession() {
        // 真机验收发现：历史会话只有 transcript.jsonl 无 meta.json（旧宿主/#436 迁移遗留），
        // GetSessionInfoAsync 返回 null → fork 误报 session/unknown。修复后应容错分叉。
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, @"D:\proj\demo", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sourceId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        await transcripts.AppendEntriesAsync(sourceId, [
            new TranscriptEntry { Role = "user", Content = "历史问" },
        ], CancellationToken.None);
        fs.DeleteFile($"/test/sessions/{sourceId}/meta.json");

        var dispatcher = new DshRpcDispatcher(services);
        var result = await dispatcher.HandleAsync("session/fork", ForkArgs(sourceId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        if (result.Ok) {
            var newId = result.Value.GetValueOrDefault().GetProperty("sessionId").GetString()
                ?? throw new Xunit.Sdk.XunitException("fork 未返回新会话 ID");
            (await transcripts.LoadTranscriptAsync(newId, CancellationToken.None)).Count.Should().Be(1);
        }
    }

    [Fact]
    public async Task Fork_CopiesJournalEvents() {
        var (services, _, sourceId) = await CreateSourceAsync();
        services.Streams.Observe(sourceId, -1, 0);
        await services.Streams.PublishAsync(sourceId, "turn/start", new DshTurnStartData { Turn = 1 });
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/fork", ForkArgs(sourceId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var newId = result.Value.GetValueOrDefault().GetProperty("sessionId").GetString()
            ?? throw new Xunit.Sdk.XunitException("fork 未返回新会话 ID");
        var events = await services.Journal.LoadAsync(newId, CancellationToken.None);
        events.Should().NotBeEmpty("journal 事件随分叉复制（打开新会话历史可还原）");
        events.All(e => e.Seq >= 0).Should().BeTrue("seq 保持源值复制");
    }
}
