
namespace Core.Tests.Memdir;

public class MemoryManagementServiceAddMemoryTests : IDisposable
{
    private readonly string _tempStoragePath;
    private readonly MemoryStore _store;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly MemoryManagementService _sut;

    public MemoryManagementServiceAddMemoryTests()
    {
        _tempStoragePath = "/test/memdir/add-memory-test.json";
        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _store = new MemoryStore(Options.Create(new MemdirOptions { StoragePath = _tempStoragePath }), _fileOperationServiceMock.Object, NullLogger<MemoryStore>.Instance);
        _sut = new MemoryManagementService(_store, logger: NullLogger<MemoryManagementService>.Instance);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task AddMemoryAsync_ShouldPersistToStore()
    {
        var content = "test memory from AddMemoryAsync";

        var memoryId = await _sut.AddMemoryAsync(content).ConfigureAwait(true);

        memoryId.Should().NotBeNullOrEmpty();

        var scanResult = await _sut.ScanMemoriesAsync(content, limit: 1).ConfigureAwait(true);
        scanResult.RelevantMemories.Should().ContainSingle();
        scanResult.RelevantMemories[0].Memory.Content.Should().Be(content);
    }

    [Fact]
    public async Task AddMemoryAsync_WithTypeAndTags_ShouldPersistCorrectly()
    {
        var content = "typed memory with tags";
        var tags = new List<string> { "test", "unit" };

        var memoryId = await _sut.AddMemoryAsync(content, type: MemoryType.Feedback, tags: tags).ConfigureAwait(true);

        memoryId.Should().NotBeNullOrEmpty();

        var scanResult = await _sut.ScanMemoriesAsync(content, limit: 1).ConfigureAwait(true);
        scanResult.RelevantMemories.Should().ContainSingle();
        var memory = scanResult.RelevantMemories[0].Memory;
        memory.Content.Should().Be(content);
        memory.Type.Should().Be(MemoryType.Feedback);
        memory.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public async Task AddMemoryAsync_WithTitleAndSource_ShouldPersistCorrectly()
    {
        var content = "memory with metadata";
        var title = "Test Title";
        var source = "unit-test";

        var memoryId = await _sut.AddMemoryAsync(content, title: title, source: source).ConfigureAwait(true);

        memoryId.Should().NotBeNullOrEmpty();

        var scanResult = await _sut.ScanMemoriesAsync(content, limit: 1).ConfigureAwait(true);
        scanResult.RelevantMemories.Should().ContainSingle();
        var memory = scanResult.RelevantMemories[0].Memory;
        memory.Title.Should().Be(title);
        memory.Source.Should().Be(source);
    }

    [Fact]
    public async Task AddMemoryAsync_WithEmptyContent_ShouldThrow()
    {
        var act = async () => await _sut.AddMemoryAsync("").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ArchiveMemoryAsync_Existing_ShouldArchiveInStore()
    {
        var memoryId = await _sut.AddMemoryAsync("Memory to archive via service").ConfigureAwait(true);

        var result = await _sut.ArchiveMemoryAsync(memoryId).ConfigureAwait(true);

        result.Should().BeTrue();
        var memory = _store.GetMemory(memoryId);
        memory!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreMemoryAsync_Archived_ShouldRestoreInStore()
    {
        var memoryId = await _sut.AddMemoryAsync("Memory to restore via service").ConfigureAwait(true);
        await _sut.ArchiveMemoryAsync(memoryId).ConfigureAwait(true);

        var result = await _sut.RestoreMemoryAsync(memoryId).ConfigureAwait(true);

        result.Should().BeTrue();
        var memory = _store.GetMemory(memoryId);
        memory!.IsArchived.Should().BeFalse();
        memory.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveMemoryAsync_NonExistent_ShouldReturnFalse()
    {
        var result = await _sut.ArchiveMemoryAsync("nonexistent_id").ConfigureAwait(true);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreMemoryAsync_NonExistent_ShouldReturnFalse()
    {
        var result = await _sut.RestoreMemoryAsync("nonexistent_id").ConfigureAwait(true);
        result.Should().BeFalse();
    }
}
