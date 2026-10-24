// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// subagentCatalog 投影测试（TASK058 B8）：前端子代理面板读 projections.values.subagentCatalog
/// （undefined → loading 态永久挂起，空数组 → ready 无子代理）。目录条目从 journal 的
/// tool-workflow/agent-start 事件回放（实例目录：含已完成条目，重启后可还原）。
/// </summary>
public sealed class DshSubagentCatalogTests {
    /// <summary>建带 journal 的服务容器</summary>
    private static DshHostServices NewServices() => new(new InMemoryFileSystem());

    /// <summary>建带两条消息的源会话（真实转录）</summary>
    private static async Task<string> CreateSessionAsync(DshHostServices services) {
        services.Sessions.AttachTranscripts(new State.TranscriptService(new InMemoryFileSystem(), "/test/sessions"));
        var created = await services.Sessions.CreateAsync(null, @"D:\proj\demo", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        return (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
    }

    [Fact]
    public async Task Projections_EmptySession_CatalogIsReadyStateEmptyArray() {
        var services = NewServices();
        var sessionId = await CreateSessionAsync(services);

        var projection = await services.Sessions.ProjectionsAsync(sessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("投影为 null");
        var values = projection.Values;
        // 前端契约：undefined → "loading"（面板挂起）；[] → "ready"（无子代理）。必须输出空数组。
        values.SubagentCatalog.Should().NotBeNull("undefined 会让前端子代理面板永久 loading").And.BeEmpty();
    }

    [Fact]
    public async Task Projections_AgentStartEvents_CatalogContainsEntries() {
        var services = NewServices();
        var sessionId = await CreateSessionAsync(services);
        services.Streams.Observe(sessionId, -1, 0);
        await services.Streams.PublishAsync(sessionId, "tool-workflow/run-start", new DshWorkflowRunStartData { RunId = "r1", Name = "subagents" });
        await services.Streams.PublishAsync(sessionId, "tool-workflow/agent-start", new DshWorkflowAgentStartData {
            RunId = "r1", Seq = 1, Label = "explore", ChildId = "child-1",
        });
        await services.Streams.PublishAsync(sessionId, "tool-workflow/agent-start", new DshWorkflowAgentStartData {
            RunId = "r1", Seq = 2, Label = "coder", ChildId = "child-2",
        });

        var projection = await services.Sessions.ProjectionsAsync(sessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("投影为 null");
        var catalog = projection.Values.SubagentCatalog.Should().NotBeNull().And.Subject.ToList();
        catalog.Count.Should().Be(2);
        catalog[0].Id.Should().Be("child-1");
        catalog[0].Label.Should().Be("explore");
        catalog[0].Mode.Should().Be("one-shot");
        catalog[0].CreatedAt.Should().BeGreaterThan(0, "createdAt 取事件时间戳（Unix 毫秒）");
        catalog[1].Id.Should().Be("child-2");
    }

    [Fact]
    public async Task Projections_AgentEnd_KeepsEntryAsInstanceCatalog() {
        var services = NewServices();
        var sessionId = await CreateSessionAsync(services);
        services.Streams.Observe(sessionId, -1, 0);
        await services.Streams.PublishAsync(sessionId, "tool-workflow/agent-start", new DshWorkflowAgentStartData {
            RunId = "r1", Seq = 1, Label = "explore", ChildId = "child-1",
        });
        await services.Streams.PublishAsync(sessionId, "tool-workflow/agent-end", new DshWorkflowAgentEndData {
            RunId = "r1", Seq = 1, Outcome = "completed",
        });

        var projection = await services.Sessions.ProjectionsAsync(sessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("投影为 null");

        // catalog 是实例目录（含已完成），前端 activity 另查运行态
        projection.Values.SubagentCatalog.Should().ContainSingle().Which.Id.Should().Be("child-1");
    }
}
