namespace Core.Tests;

public class FileStateGuardNodeTests {
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void HasBeenRead_NullCache_ReturnsTrue() {
        var node = new FileStateGuardNode(_fs);

        var result = node.HasBeenRead("/test.txt");

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileNotExists_ReturnsTrue() {
        var cache = new Mock<IFileStateCache>();
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead("/nonexistent/file.txt");

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileExistsInCache_ReturnsTrue() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.HasBeenRead(filePath)).Returns(true);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.True(result);
    }

    [Fact]
    public void HasBeenRead_FileExistsNotInCache_ReturnsFalse() {
        var filePath = CreateFile("test");
        var cache = new Mock<IFileStateCache>();
        cache.Setup(c => c.HasBeenRead(filePath)).Returns(false);
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = node.HasBeenRead(filePath);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckStaleWriteAsync_NullCache_ReturnsNull() {
        var node = new FileStateGuardNode(_fs);

        var result = await node.CheckStaleWriteAsync("/test.txt", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckStaleWriteAsync_FileNotExists_ReturnsNull() {
        var cache = new Mock<IFileStateCache>();
        var node = new FileStateGuardNode(_fs, cache.Object);

        var result = await node.CheckStaleWriteAsync("/nonexistent/file.txt", CancellationToken.None);

        Assert.Null(result);
    }

    private string CreateFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"state_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content);
        return path;
    }
}