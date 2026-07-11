namespace Core.Tests.LLM;

public sealed class FileOperationTrackerTests
{
    [Fact]
    public void Track_ReadOperation_ShouldRecord()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Read);

        var entries = tracker.GetAllEntries();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Read);
    }

    [Fact]
    public void Track_WriteOperation_ShouldRecord()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Write);

        var entries = tracker.GetAllEntries();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Write);
    }

    [Fact]
    public void Track_EditOperation_ShouldRecord()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Edit);

        var entries = tracker.GetAllEntries();
        entries.Should().HaveCount(1);
        entries[0].OperationType.Should().Be(FileOperationType.Edit);
    }

    [Fact]
    public void Track_MultipleOperationsOnSameFile_ShouldRecordAll()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/file.cs", FileOperationType.Read);
        tracker.Track("/path/to/file.cs", FileOperationType.Edit);

        var entries = tracker.GetAllEntries();
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void Track_NullFilePath_ShouldThrow()
    {
        var tracker = new FileOperationTracker();

        var act = () => tracker.Track(null!, FileOperationType.Read);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetOperatedFilePaths_ShouldReturnDistinctPaths()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/a.cs", FileOperationType.Read);
        tracker.Track("/path/to/a.cs", FileOperationType.Edit);
        tracker.Track("/path/to/b.cs", FileOperationType.Read);

        var paths = tracker.GetOperatedFilePaths();
        paths.Should().HaveCount(2);
    }

    [Fact]
    public void GetOperatedFilePaths_ShouldBeSorted()
    {
        var tracker = new FileOperationTracker();

        tracker.Track("/path/to/z.cs", FileOperationType.Read);
        tracker.Track("/path/to/a.cs", FileOperationType.Read);

        var paths = tracker.GetOperatedFilePaths();
        paths[0].Should().Contain("a.cs");
        paths[1].Should().Contain("z.cs");
    }

    [Fact]
    public void Clear_ShouldRemoveAllEntries()
    {
        var tracker = new FileOperationTracker();
        tracker.Track("/path/to/file.cs", FileOperationType.Read);

        tracker.Clear();

        tracker.GetAllEntries().Should().BeEmpty();
        tracker.GetOperatedFilePaths().Should().BeEmpty();
    }

    [Fact]
    public void GetAllEntries_EmptyTracker_ShouldReturnEmpty()
    {
        var tracker = new FileOperationTracker();

        tracker.GetAllEntries().Should().BeEmpty();
    }

    [Fact]
    public void GetOperatedFilePaths_EmptyTracker_ShouldReturnEmpty()
    {
        var tracker = new FileOperationTracker();

        tracker.GetOperatedFilePaths().Should().BeEmpty();
    }
}
