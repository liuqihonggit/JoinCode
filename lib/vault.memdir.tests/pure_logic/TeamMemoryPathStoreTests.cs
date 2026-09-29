
namespace Core.Tests.Memdir;

/// <summary>
/// TeamMemoryPathStore 确定性测试 — null IFileSystem 跳过磁盘加载,Mock IPersistencePipeline 验证持久化入队。
/// 覆盖 Add/Get/Remove 的内存 CRUD 与持久化触发逻辑,不依赖时序。
/// </summary>
public sealed class TeamMemoryPathStoreTests {
    // === 纯内存 CRUD（null pipeline + null fs）===

    [Fact]
    public async Task AddTeamMemoryPathCoreAsync_NewPath_AddsToStore() {
        var store = new TeamMemoryPathStore(persistencePipeline: null, fs: null, logger: null);
        await store.AddTeamMemoryPathCoreAsync("team1", "/mem/path1", isShared: true, allowedAgents: new() { "agentA" }, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().ContainSingle();
        all[0].TeamId.Should().Be("team1");
        all[0].Path.Should().Be("/mem/path1");
        all[0].IsShared.Should().BeTrue();
        all[0].AllowedAgents.Should().ContainSingle().Which.Should().Be("agentA");
    }

    [Fact]
    public async Task AddTeamMemoryPathCoreAsync_ExistingSamePath_ReplacesEntry() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("team1", "/p", false, new() { "a" }, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("team1", "/p", true, new() { "b" }, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().ContainSingle();
        all[0].IsShared.Should().BeTrue();
        all[0].AllowedAgents.Should().ContainSingle().Which.Should().Be("b");
    }

    [Fact]
    public async Task AddTeamMemoryPathCoreAsync_NullAllowedAgents_UsesEmptyList() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t", "/p", false, allowedAgents: null, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all[0].AllowedAgents.Should().BeEmpty();
    }

    [Fact]
    public async Task AddTeamMemoryPathCoreAsync_DistinctTeams_KeepsAllEntries() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t2", "/p2", false, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p3", true, null, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetTeamMemoryPathsCoreAsync_NullTeamId_ReturnsAll() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t2", "/p2", true, null, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTeamMemoryPathsCoreAsync_EmptyTeamId_ReturnsAll() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);

        var all = await store.GetTeamMemoryPathsCoreAsync("", CancellationToken.None).ConfigureAwait(true);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTeamMemoryPathsCoreAsync_ByTeamId_FiltersByTeam() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t2", "/p2", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p3", true, null, CancellationToken.None).ConfigureAwait(true);

        var t1 = await store.GetTeamMemoryPathsCoreAsync("t1", CancellationToken.None).ConfigureAwait(true);
        t1.Should().HaveCount(2);
        t1.Should().OnlyContain(p => p.TeamId == "t1");
    }

    [Fact]
    public async Task GetTeamMemoryPathsCore_SyncVersion_ReturnsFilteredWithoutLoad() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t2", "/p2", true, null, CancellationToken.None).ConfigureAwait(true);

        // 同步版本不触发加载,但前面 Add 已加载过,内存中有数据
        var t1 = store.GetTeamMemoryPathsCore("t1");
        t1.Should().ContainSingle();
        t1[0].Path.Should().Be("/p1");
    }

    [Fact]
    public async Task RemoveTeamMemoryPathCoreAsync_Existing_RemovesAndReturnsTrue() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);

        var removed = await store.RemoveTeamMemoryPathCoreAsync("t1", "/p1", CancellationToken.None).ConfigureAwait(true);
        removed.Should().BeTrue();

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveTeamMemoryPathCoreAsync_NonExisting_ReturnsFalseAndNoThrow() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);

        var removed = await store.RemoveTeamMemoryPathCoreAsync("t1", "/nonexistent", CancellationToken.None).ConfigureAwait(true);
        removed.Should().BeFalse();

        var all = await store.GetTeamMemoryPathsCoreAsync(null, CancellationToken.None).ConfigureAwait(true);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveTeamMemoryPathCoreAsync_DifferentTeamId_DoesNotRemove() {
        var store = new TeamMemoryPathStore(null, null, null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);

        var removed = await store.RemoveTeamMemoryPathCoreAsync("t2", "/p1", CancellationToken.None).ConfigureAwait(true);
        removed.Should().BeFalse();
    }

    // === 持久化触发（Mock IPersistencePipeline）===

    [Fact]
    public async Task AddTeamMemoryPathCoreAsync_WithPipeline_EnqueuesPersistRequestAndCompletes() {
        PersistRequest? captured = null;
        var pipelineMock = new Mock<IPersistencePipeline>();
        pipelineMock.Setup(x => x.EnqueueAsync(It.IsAny<PersistRequest>(), It.IsAny<CancellationToken>()))
                    .Callback((PersistRequest req, CancellationToken _) => {
                        captured = req;
                        req.Completion?.TrySetResult();
                    })
                    .Returns(ValueTask.CompletedTask);

        var store = new TeamMemoryPathStore(pipelineMock.Object, fs: null, logger: null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, new() { "a" }, CancellationToken.None).ConfigureAwait(true);

        pipelineMock.Verify(x => x.EnqueueAsync(It.IsAny<PersistRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        captured!.Category.Should().Be("memory");
        captured.FileName.Should().Be("team-paths.json");
        captured.Content.Should().NotBeNullOrEmpty();
        // 内容应可反序列化为 TeamMemoryPath 列表
        var roundtrip = RelaxedJsonSerializer.Deserialize<List<TeamMemoryPath>>(captured.Content, MemdirJsonContext.Default);
        roundtrip.Should().ContainSingle();
        roundtrip![0].TeamId.Should().Be("t1");
        roundtrip[0].Path.Should().Be("/p1");
    }

    [Fact]
    public async Task RemoveTeamMemoryPathCoreAsync_WithPipeline_EnqueuesPersistRequest() {
        PersistRequest? captured = null;
        var pipelineMock = new Mock<IPersistencePipeline>();
        pipelineMock.Setup(x => x.EnqueueAsync(It.IsAny<PersistRequest>(), It.IsAny<CancellationToken>()))
                    .Callback((PersistRequest req, CancellationToken _) => {
                        captured = req;
                        req.Completion?.TrySetResult();
                    })
                    .Returns(ValueTask.CompletedTask);

        var store = new TeamMemoryPathStore(pipelineMock.Object, fs: null, logger: null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        captured = null;

        var removed = await store.RemoveTeamMemoryPathCoreAsync("t1", "/p1", CancellationToken.None).ConfigureAwait(true);
        removed.Should().BeTrue();

        pipelineMock.Verify(x => x.EnqueueAsync(It.IsAny<PersistRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        captured.Should().NotBeNull();
        // 移除后持久化的内容应为空数组
        var roundtrip = RelaxedJsonSerializer.Deserialize<List<TeamMemoryPath>>(captured!.Content, MemdirJsonContext.Default);
        roundtrip.Should().BeEmpty();
    }

    [Fact]
    public async Task AddThenRemove_WithPipeline_FinalPersistedStateIsEmpty() {
        var requests = new List<PersistRequest>();
        var pipelineMock = new Mock<IPersistencePipeline>();
        pipelineMock.Setup(x => x.EnqueueAsync(It.IsAny<PersistRequest>(), It.IsAny<CancellationToken>()))
                    .Callback((PersistRequest req, CancellationToken _) => {
                        requests.Add(req);
                        req.Completion?.TrySetResult();
                    })
                    .Returns(ValueTask.CompletedTask);

        var store = new TeamMemoryPathStore(pipelineMock.Object, fs: null, logger: null);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p1", true, null, CancellationToken.None).ConfigureAwait(true);
        await store.AddTeamMemoryPathCoreAsync("t1", "/p2", false, null, CancellationToken.None).ConfigureAwait(true);
        await store.RemoveTeamMemoryPathCoreAsync("t1", "/p1", CancellationToken.None).ConfigureAwait(true);

        requests.Should().HaveCount(3);
        var last = RelaxedJsonSerializer.Deserialize<List<TeamMemoryPath>>(requests[^1].Content, MemdirJsonContext.Default);
        last.Should().ContainSingle();
        last![0].Path.Should().Be("/p2");
    }
}
