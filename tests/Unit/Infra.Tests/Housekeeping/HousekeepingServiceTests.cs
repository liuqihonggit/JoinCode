namespace Infra.Tests.Housekeeping;


public sealed class HousekeepingServiceTests
{
    private readonly TestInMemFs _fs = new();
    private readonly FakeClockService _clock = new();

    private static readonly string JccDir = WorkflowConstants.Paths.JccDirectory;
    private static readonly string SessionsDir = Path.Combine(JccDir, AppDataConstants.SessionsFolderName);
    private static readonly string FileHistoryDir = Path.Combine(JccDir, AppDataConstants.FileHistoryFolderName);
    private static readonly string SessionEnvDir = Path.Combine(JccDir, "session-env");
    private static readonly string DebugDir = Path.Combine(JccDir, "debug");
    private static readonly string ErrorsDir = Path.Combine(JccDir, "errors");

    private readonly Mock<IPlanModeManager> _planModeManager = new();
    private readonly Mock<IAgentWorktreeService> _worktreeService = new();

    private HousekeepingService CreateSut()
        => new(_fs, _clock, _planModeManager.Object, _worktreeService.Object, null, NullLogger<HousekeepingService>.Instance);

    [Fact]
    public void CleanupOldSessionFiles_WithNoSessionsDir_ShouldReturnZero()
    {
        var sut = CreateSut();
        sut.CleanupOldSessionFiles().Should().Be(0);
    }

    [Fact]
    public void CleanupOldSessionFiles_ShouldDeleteOldJsonlFiles()
    {
        _fs.CreateDirectory(SessionsDir);
        var oldFile = Path.Combine(SessionsDir, "old-session.json");
        var newFile = Path.Combine(SessionsDir, "new-session.json");

        _fs.WriteAllText(oldFile, "old");
        _fs.WriteAllText(newFile, "new");

        _fs.SetLastWriteTimeUtc(oldFile, _clock.GetUtcNow().AddDays(-31));
        _fs.SetLastWriteTimeUtc(newFile, _clock.GetUtcNow().AddDays(-1));

        var sut = CreateSut();
        var result = sut.CleanupOldSessionFiles(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.FileExists(oldFile).Should().BeFalse();
        _fs.FileExists(newFile).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldSessionFiles_ShouldDeleteOldCastFiles()
    {
        _fs.CreateDirectory(SessionsDir);
        var castFile = Path.Combine(SessionsDir, "old-session.cast");
        _fs.WriteAllText(castFile, "cast-content");
        _fs.SetLastWriteTimeUtc(castFile, _clock.GetUtcNow().AddDays(-31));

        var sut = CreateSut();
        var result = sut.CleanupOldSessionFiles(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.FileExists(castFile).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldSessionFiles_WithAllRecentFiles_ShouldDeleteNothing()
    {
        _fs.CreateDirectory(SessionsDir);
        var recentFile = Path.Combine(SessionsDir, "recent.json");
        _fs.WriteAllText(recentFile, "recent");
        _fs.SetLastWriteTimeUtc(recentFile, _clock.GetUtcNow().AddDays(-5));

        var sut = CreateSut();
        sut.CleanupOldSessionFiles(maxAgeDays: 30).Should().Be(0);
        _fs.FileExists(recentFile).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldFileHistoryBackups_ShouldDeleteOldDirectories()
    {
        _fs.CreateDirectory(FileHistoryDir);
        var oldDir = Path.Combine(FileHistoryDir, "old-backup");
        var newDir = Path.Combine(FileHistoryDir, "new-backup");
        _fs.CreateDirectory(oldDir);
        _fs.CreateDirectory(newDir);

        _fs.SetDirectoryLastWriteTimeUtc(oldDir, _clock.GetUtcNow().AddDays(-31));
        _fs.SetDirectoryLastWriteTimeUtc(newDir, _clock.GetUtcNow().AddDays(-1));

        var sut = CreateSut();
        var result = sut.CleanupOldFileHistoryBackups(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.DirectoryExists(oldDir).Should().BeFalse();
        _fs.DirectoryExists(newDir).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldSessionEnvDirs_ShouldDeleteOldDirectories()
    {
        _fs.CreateDirectory(SessionEnvDir);
        var oldDir = Path.Combine(SessionEnvDir, "old-env");
        _fs.CreateDirectory(oldDir);
        _fs.SetDirectoryLastWriteTimeUtc(oldDir, _clock.GetUtcNow().AddDays(-31));

        var sut = CreateSut();
        var result = sut.CleanupOldSessionEnvDirs(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.DirectoryExists(oldDir).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldDebugLogs_ShouldDeleteOldTxtFiles()
    {
        _fs.CreateDirectory(DebugDir);
        var oldLog = Path.Combine(DebugDir, "old-log.txt");
        var newLog = Path.Combine(DebugDir, "new-log.txt");
        _fs.WriteAllText(oldLog, "old");
        _fs.WriteAllText(newLog, "new");

        _fs.SetLastWriteTimeUtc(oldLog, _clock.GetUtcNow().AddDays(-31));
        _fs.SetLastWriteTimeUtc(newLog, _clock.GetUtcNow().AddDays(-1));

        var sut = CreateSut();
        var result = sut.CleanupOldDebugLogs(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.FileExists(oldLog).Should().BeFalse();
        _fs.FileExists(newLog).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldMessageFiles_ShouldDeleteOldErrorFiles()
    {
        _fs.CreateDirectory(ErrorsDir);
        var oldError = Path.Combine(ErrorsDir, "old-error.log");
        _fs.WriteAllText(oldError, "error");
        _fs.SetLastWriteTimeUtc(oldError, _clock.GetUtcNow().AddDays(-31));

        var sut = CreateSut();
        var result = sut.CleanupOldMessageFiles(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.FileExists(oldError).Should().BeFalse();
    }

    [Fact]
    public async Task RunAllCleanupAsync_ShouldAggregateAllResults()
    {
        _fs.CreateDirectory(SessionsDir);
        var oldFile = Path.Combine(SessionsDir, "old.json");
        _fs.WriteAllText(oldFile, "old");
        _fs.SetLastWriteTimeUtc(oldFile, _clock.GetUtcNow().AddDays(-31));

        _fs.CreateDirectory(FileHistoryDir);
        var oldDir = Path.Combine(FileHistoryDir, "old-backup");
        _fs.CreateDirectory(oldDir);
        _fs.SetDirectoryLastWriteTimeUtc(oldDir, _clock.GetUtcNow().AddDays(-31));

        var sut = CreateSut();
        var result = await sut.RunAllCleanupAsync();

        result.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void CleanupOldFileHistoryBackups_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldFileHistoryBackups().Should().Be(0);
    }

    [Fact]
    public void CleanupOldSessionEnvDirs_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldSessionEnvDirs().Should().Be(0);
    }

    [Fact]
    public void CleanupOldDebugLogs_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldDebugLogs().Should().Be(0);
    }

    [Fact]
    public void CleanupOldMessageFiles_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldMessageFiles().Should().Be(0);
    }

    [Fact]
    public void CleanupOldImageCaches_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldImageCaches("session-1").Should().Be(0);
    }

    [Fact]
    public void CleanupOldImageCaches_ShouldDeleteNonCurrentSessionDirs()
    {
        var imageCacheDir = Path.Combine(JccDir, "image-cache");
        _fs.CreateDirectory(imageCacheDir);
        var oldSessionDir = Path.Combine(imageCacheDir, "old-session");
        var currentSessionDir = Path.Combine(imageCacheDir, "current-session");
        _fs.CreateDirectory(oldSessionDir);
        _fs.CreateDirectory(currentSessionDir);
        _fs.WriteAllText(Path.Combine(oldSessionDir, "image.png"), "img");
        _fs.WriteAllText(Path.Combine(currentSessionDir, "image.png"), "img");

        var sut = CreateSut();
        var result = sut.CleanupOldImageCaches("current-session");

        result.Should().Be(1);
        _fs.DirectoryExists(oldSessionDir).Should().BeFalse();
        _fs.DirectoryExists(currentSessionDir).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldImageCaches_WithEmptySessionId_ShouldDeleteAllDirs()
    {
        var imageCacheDir = Path.Combine(JccDir, "image-cache");
        _fs.CreateDirectory(imageCacheDir);
        var dir1 = Path.Combine(imageCacheDir, "session-1");
        var dir2 = Path.Combine(imageCacheDir, "session-2");
        _fs.CreateDirectory(dir1);
        _fs.CreateDirectory(dir2);

        var sut = CreateSut();
        var result = sut.CleanupOldImageCaches("");

        result.Should().Be(2);
        _fs.DirectoryExists(dir1).Should().BeFalse();
        _fs.DirectoryExists(dir2).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldImageCaches_ShouldRemoveEmptyBaseDir()
    {
        var imageCacheDir = Path.Combine(JccDir, "image-cache");
        _fs.CreateDirectory(imageCacheDir);
        var oldDir = Path.Combine(imageCacheDir, "old-session");
        _fs.CreateDirectory(oldDir);

        var sut = CreateSut();
        sut.CleanupOldImageCaches("");

        _fs.DirectoryExists(imageCacheDir).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldPastes_WithNoDir_ShouldReturnZero()
    {
        CreateSut().CleanupOldPastes().Should().Be(0);
    }

    [Fact]
    public void CleanupOldPastes_ShouldDeleteOldTxtFiles()
    {
        var pasteCacheDir = Path.Combine(JccDir, "paste-cache");
        _fs.CreateDirectory(pasteCacheDir);
        var oldPaste = Path.Combine(pasteCacheDir, "abc123.txt");
        var newPaste = Path.Combine(pasteCacheDir, "def456.txt");
        _fs.WriteAllText(oldPaste, "old paste content");
        _fs.WriteAllText(newPaste, "new paste content");

        _fs.SetLastWriteTimeUtc(oldPaste, _clock.GetUtcNow().AddDays(-31));
        _fs.SetLastWriteTimeUtc(newPaste, _clock.GetUtcNow().AddDays(-1));

        var sut = CreateSut();
        var result = sut.CleanupOldPastes(maxAgeDays: 30);

        result.Should().Be(1);
        _fs.FileExists(oldPaste).Should().BeFalse();
        _fs.FileExists(newPaste).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldPastes_ShouldIgnoreNonTxtFiles()
    {
        var pasteCacheDir = Path.Combine(JccDir, "paste-cache");
        _fs.CreateDirectory(pasteCacheDir);
        var jsonFile = Path.Combine(pasteCacheDir, "meta.json");
        _fs.WriteAllText(jsonFile, "{}");
        _fs.SetLastWriteTimeUtc(jsonFile, _clock.GetUtcNow().AddDays(-100));

        var sut = CreateSut();
        var result = sut.CleanupOldPastes(maxAgeDays: 30);

        result.Should().Be(0);
        _fs.FileExists(jsonFile).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldPlanFiles_ShouldDelegateToPlanModeManager()
    {
        _planModeManager.Setup(p => p.CleanupOldPlanFiles(30)).Returns(3);

        var sut = CreateSut();
        var result = sut.CleanupOldPlanFiles(maxAgeDays: 30);

        result.Should().Be(3);
        _planModeManager.Verify(p => p.CleanupOldPlanFiles(30), Times.Once());
    }

    [Fact]
    public void CleanupOldPlanFiles_WhenException_ShouldReturnZero()
    {
        _planModeManager.Setup(p => p.CleanupOldPlanFiles(It.IsAny<int>()))
            .Throws(new InvalidOperationException("test"));

        var sut = CreateSut();
        var result = sut.CleanupOldPlanFiles();

        result.Should().Be(0);
    }

    [Fact]
    public async Task CleanupStaleWorktreesAsync_ShouldDelegateToWorktreeService()
    {
        _worktreeService.Setup(w => w.CleanupStaleWorktreesAsync(It.IsAny<WorktreeOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var sut = CreateSut();
        var result = await sut.CleanupStaleWorktreesAsync();

        result.Should().Be(2);
        _worktreeService.Verify(w => w.CleanupStaleWorktreesAsync(null, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task CleanupStaleWorktreesAsync_WhenException_ShouldReturnZero()
    {
        _worktreeService.Setup(w => w.CleanupStaleWorktreesAsync(It.IsAny<WorktreeOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test"));

        var sut = CreateSut();
        var result = await sut.CleanupStaleWorktreesAsync();

        result.Should().Be(0);
    }
}
