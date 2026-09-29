
namespace Core.Tests.Memdir;

public class MemoryManagementServiceCleanupGuardTests : IDisposable {
    private readonly string _tempStoragePath;
    private readonly MemoryStore _store;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly MemoryManagementService _sut;
    private bool _disposed;

    public MemoryManagementServiceCleanupGuardTests() {
        _tempStoragePath = "/test/memdir/cleanup-guard-test.json";
        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _store = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = _tempStoragePath }),
            _fileOperationServiceMock.Object,
            NullLogger<MemoryStore>.Instance);
        _sut = new MemoryManagementService(_store, logger: NullLogger<MemoryManagementService>.Instance);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
    }

    [Fact]
    public async Task CleanupOldMemoriesAsync_WhenArchiveDaysGreaterThanDeleteDays_ShouldThrow() {
        var act = async () => await _sut.CleanupOldMemoriesAsync(
            archiveAfterDays: 200,
            deleteAfterDays: 100).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*归档天数*不能大于删除天数*").ConfigureAwait(true);
    }

    [Fact]
    public async Task CleanupOldMemoriesAsync_WhenArchiveDaysEqualsDeleteDays_ShouldNotThrow() {
        var act = async () => await _sut.CleanupOldMemoriesAsync(
            archiveAfterDays: 150,
            deleteAfterDays: 150).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CleanupOldMemoriesAsync_WhenArchiveDaysLessThanDeleteDays_ShouldNotThrow() {
        var act = async () => await _sut.CleanupOldMemoriesAsync(
            archiveAfterDays: 90,
            deleteAfterDays: 180).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CleanupOldMemoriesAsync_WhenDefaultsUsed_ShouldNotThrow() {
        var act = async () => await _sut.CleanupOldMemoriesAsync().ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }
}
