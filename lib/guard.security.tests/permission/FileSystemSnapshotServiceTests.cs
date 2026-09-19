namespace Guard.Security.Tests;

/// <summary>
/// FileSystemSnapshotService 文件系统快照对比测试
/// </summary>
public class FileSystemSnapshotServiceTests {
    private static FileSystemSnapshotService CreateService()
        => new(new InMemoryFileSystem(), NullLogger<FileSystemSnapshotService>.Instance);

    [Fact]
    public async Task CaptureAsync_NonExistent_Directory_Returns_Empty_Snapshot() {
        var service = CreateService();
        var snapshot = await service.CaptureAsync("/nonexistent");

        snapshot.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_Existing_Directory_Captures_Files() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/file1.txt", "content1");
        fs.WriteAllText("/test/dir/file2.txt", "content2");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var snapshot = await service.CaptureAsync("/test/dir");

        snapshot.Entries.Should().HaveCount(2);
        snapshot.Entries.Should().ContainKey("file1.txt");
        snapshot.Entries.Should().ContainKey("file2.txt");
    }

    [Fact]
    public async Task Compare_Detects_Created_Files() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/existing.txt", "old");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var before = await service.CaptureAsync("/test/dir");

        fs.WriteAllText("/test/dir/new.txt", "new content");

        var after = await service.CaptureAsync("/test/dir");
        var changes = service.Compare(before, after);

        changes.Should().ContainSingle(c => c.Path == "new.txt" && c.ChangeType == FileChangeType.Created);
    }

    [Fact]
    public async Task Compare_Detects_Modified_Files() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/file.txt", "original content");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var before = await service.CaptureAsync("/test/dir");

        fs.WriteAllText("/test/dir/file.txt", "modified content that is longer");

        var after = await service.CaptureAsync("/test/dir");
        var changes = service.Compare(before, after);

        changes.Should().ContainSingle(c => c.Path == "file.txt" && c.ChangeType == FileChangeType.Modified);
    }

    [Fact]
    public async Task Compare_Detects_Deleted_Files() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/keep.txt", "keep");
        fs.WriteAllText("/test/dir/delete.txt", "delete me");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var before = await service.CaptureAsync("/test/dir");

        fs.DeleteFile("/test/dir/delete.txt");

        var after = await service.CaptureAsync("/test/dir");
        var changes = service.Compare(before, after);

        changes.Should().ContainSingle(c => c.Path == "delete.txt" && c.ChangeType == FileChangeType.Deleted);
    }

    [Fact]
    public async Task Compare_No_Changes_Returns_Empty() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/file.txt", "unchanged");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var before = await service.CaptureAsync("/test/dir");
        var after = await service.CaptureAsync("/test/dir");

        service.Compare(before, after).Should().BeEmpty();
    }

    [Fact]
    public async Task Compare_Multiple_Changes_Detected() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/test/dir");
        fs.WriteAllText("/test/dir/keep.txt", "keep");
        fs.WriteAllText("/test/dir/modify.txt", "original");
        fs.WriteAllText("/test/dir/delete.txt", "to delete");

        var service = new FileSystemSnapshotService(fs, NullLogger<FileSystemSnapshotService>.Instance);
        var before = await service.CaptureAsync("/test/dir");

        fs.WriteAllText("/test/dir/modify.txt", "modified");
        fs.WriteAllText("/test/dir/create.txt", "new");
        fs.DeleteFile("/test/dir/delete.txt");

        var after = await service.CaptureAsync("/test/dir");
        var changes = service.Compare(before, after);

        changes.Should().HaveCount(3);
        changes.Should().Contain(c => c.Path == "create.txt" && c.ChangeType == FileChangeType.Created);
        changes.Should().Contain(c => c.Path == "modify.txt" && c.ChangeType == FileChangeType.Modified);
        changes.Should().Contain(c => c.Path == "delete.txt" && c.ChangeType == FileChangeType.Deleted);
    }
}