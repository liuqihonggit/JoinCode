namespace Core.Tests.LLM;

public sealed class FileOperationTrackerTests {
    [Fact]
    public async Task Track_ReadOperation_ShouldRecord() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Read);

        var entries = tracker.GetAllEntries().ToList();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Read);
    }

    [Fact]
    public async Task Track_WriteOperation_ShouldRecord() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Write);

        var entries = tracker.GetAllEntries().ToList();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Write);
    }

    [Fact]
    public async Task Track_EditOperation_ShouldRecord() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Edit);

        var entries = tracker.GetAllEntries().ToList();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Edit);
    }

    [Fact]
    public async Task Track_MultipleOperationsOnSameFile_ShouldRecordAll() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Read);
        tracker.Track("/path/to/file.cs", FileOperationType.Edit);

        var entries = tracker.GetAllEntries();
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void Track_NullFilePath_ShouldThrow() {
        var tracker = new FileOperationTracker();

        var act = () => tracker.Track(null!, FileOperationType.Read);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetOperatedFilePaths_ShouldReturnDistinctPaths() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/a.cs", FileOperationType.Read);
        tracker.Track("/path/to/a.cs", FileOperationType.Edit);
        tracker.Track("/path/to/b.cs", FileOperationType.Read);

        var paths = tracker.GetOperatedFilePaths();
        paths.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOperatedFilePaths_ShouldBeSorted() {
        await using var tracker = new FileOperationTracker();

        tracker.Track("/path/to/z.cs", FileOperationType.Read);
        tracker.Track("/path/to/a.cs", FileOperationType.Read);

        var paths = tracker.GetOperatedFilePaths().ToList();
        paths[0].Should().Contain("a.cs");
        paths[1].Should().Contain("z.cs");
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllEntries() {
        await using var tracker = new FileOperationTracker();
        tracker.Track("/path/to/file.cs", FileOperationType.Read);

        tracker.Clear();

        tracker.GetAllEntries().Should().BeEmpty();
        tracker.GetOperatedFilePaths().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllEntries_EmptyTracker_ShouldReturnEmpty() {
        await using var tracker = new FileOperationTracker();

        tracker.GetAllEntries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetOperatedFilePaths_EmptyTracker_ShouldReturnEmpty() {
        await using var tracker = new FileOperationTracker();

        tracker.GetOperatedFilePaths().Should().BeEmpty();
    }
}