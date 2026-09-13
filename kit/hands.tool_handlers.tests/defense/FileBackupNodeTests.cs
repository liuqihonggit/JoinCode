namespace Core.Tests;

public class FileBackupNodeTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task BackupAsync_NullService_SkipsSilently()
    {
        var node = new FileBackupNode(_fs);
        var filePath = CreateFile("test content");

        await node.BackupAsync(filePath, CancellationToken.None);
    }

    [Fact]
    public async Task BackupAsync_FileNotExists_SkipsSilently()
    {
        var history = new Mock<IFileHistoryService>();
        var node = new FileBackupNode(_fs, history.Object);

        await node.BackupAsync("/nonexistent/path.txt", CancellationToken.None);

        history.Verify(h => h.BackupBeforeWriteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BackupAsync_FileExists_CallsBackup()
    {
        var history = new Mock<IFileHistoryService>();
        history.Setup(h => h.BackupBeforeWriteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.FromResult<string?>(null))
               .Verifiable();
        var node = new FileBackupNode(_fs, history.Object);
        var filePath = CreateFile("test content");

        await node.BackupAsync(filePath, CancellationToken.None);

        history.Verify();
    }

    private string CreateFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"backup_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content);
        return path;
    }
}
