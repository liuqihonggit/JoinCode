// 测试使用真实文件系统创建临时工作目录
#pragma warning disable JCC9001, JCC9002
namespace Core.Tests.LLM;

public sealed class ThinkingStoreTests
{
    private static ThinkingStore CreateStore()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"thinking_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        var fileOp = new InMemoryFileOperationService();
        return new ThinkingStore(Options.Create(new MemdirOptions { StoragePath = tempPath }), fileOp, new PhysicalFileSystem());
    }

    [Fact]
    public async Task StoreAsync_ShouldAddEntry()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking content", "model-a", CancellationToken.None).ConfigureAwait(true);

        var entries = await store.GetRecentAsync("session1", 10, CancellationToken.None).ConfigureAwait(true);

        entries.Should().HaveCount(1);
        entries[0].Content.Should().Be("thinking content");
        entries[0].ModelId.Should().Be("model-a");
        entries[0].SessionId.Should().Be("session1");
    }

    [Fact]
    public async Task StoreAsync_EmptyContent_ShouldNotAddEntry()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "", "model-a", CancellationToken.None).ConfigureAwait(true);

        var entries = await store.GetRecentAsync("session1", 10, CancellationToken.None).ConfigureAwait(true);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_MultipleEntries_ShouldStoreAll()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking 1", "model-a", CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session1", "thinking 2", "model-b", CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session1", "thinking 3", "model-a", CancellationToken.None).ConfigureAwait(true);

        var entries = await store.GetRecentAsync("session1", 10, CancellationToken.None).ConfigureAwait(true);

        entries.Should().HaveCount(3);
        entries[0].Content.Should().Be("thinking 1");
        entries[2].Content.Should().Be("thinking 3");
    }

    [Fact]
    public async Task GetRecentAsync_ShouldReturnLastNEntries()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking 1", null, CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session1", "thinking 2", null, CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session1", "thinking 3", null, CancellationToken.None).ConfigureAwait(true);

        var entries = await store.GetRecentAsync("session1", 2, CancellationToken.None).ConfigureAwait(true);

        entries.Should().HaveCount(2);
        entries[0].Content.Should().Be("thinking 2");
        entries[1].Content.Should().Be("thinking 3");
    }

    [Fact]
    public async Task GetRecentAsync_UnknownSession_ShouldReturnEmpty()
    {
        var store = CreateStore();

        var entries = await store.GetRecentAsync("unknown", 10, CancellationToken.None).ConfigureAwait(true);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_ShouldReturnLastEntry()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking 1", null, CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session1", "thinking 2", "model-b", CancellationToken.None).ConfigureAwait(true);

        var latest = await store.GetLatestAsync("session1", CancellationToken.None).ConfigureAwait(true);

        latest.Should().NotBeNull();
        latest!.Content.Should().Be("thinking 2");
        latest.ModelId.Should().Be("model-b");
    }

    [Fact]
    public async Task GetLatestAsync_EmptySession_ShouldReturnNull()
    {
        var store = CreateStore();

        var latest = await store.GetLatestAsync("empty", CancellationToken.None).ConfigureAwait(true);

        latest.Should().BeNull();
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveEntries()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking content", null, CancellationToken.None).ConfigureAwait(true);

        await store.ClearAsync("session1", CancellationToken.None).ConfigureAwait(true);

        var entries = await store.GetRecentAsync("session1", 10, CancellationToken.None).ConfigureAwait(true);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_DifferentSessions_ShouldIsolate()
    {
        var store = CreateStore();
        await store.StoreAsync("session1", "thinking for s1", null, CancellationToken.None).ConfigureAwait(true);
        await store.StoreAsync("session2", "thinking for s2", null, CancellationToken.None).ConfigureAwait(true);

        var s1Entries = await store.GetRecentAsync("session1", 10, CancellationToken.None).ConfigureAwait(true);
        var s2Entries = await store.GetRecentAsync("session2", 10, CancellationToken.None).ConfigureAwait(true);

        s1Entries.Should().HaveCount(1);
        s1Entries[0].Content.Should().Be("thinking for s1");
        s2Entries.Should().HaveCount(1);
        s2Entries[0].Content.Should().Be("thinking for s2");
    }

    [Fact]
    public async Task StoreAsync_NullSessionId_ShouldThrow()
    {
        var store = CreateStore();

        var act = () => store.StoreAsync(null!, "content", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }
}
