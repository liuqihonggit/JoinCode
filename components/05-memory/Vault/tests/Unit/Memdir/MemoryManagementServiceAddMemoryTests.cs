
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
}
