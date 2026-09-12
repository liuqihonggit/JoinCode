
namespace Core.Tests.Memdir;

public sealed class AssistantDailyLogServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOpService;
    private readonly MemoryStore _memoryStore;
    private readonly Mock<IMemoryPaths> _memoryPathsMock;
    private readonly string _tempBasePath;

    public AssistantDailyLogServiceTests()
    {
        _fileOpService = new InMemoryFileOperationService();
        _tempBasePath = "/test/memdir/daily-log";
        _memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = Path.Combine(_tempBasePath, "store.json") }),
            _fileOpService,
            NullLogger<MemoryStore>.Instance);

        _memoryPathsMock = new Mock<IMemoryPaths>();
        _memoryPathsMock.Setup(p => p.GetUserMemoryDirectory(It.IsAny<string?>()))
            .Returns(Path.Combine(_tempBasePath, "users", "default"));
    }

    private AssistantDailyLogService CreateSut()
    {
        return new AssistantDailyLogService(
            _memoryStore,
            _memoryPathsMock.Object,
            _fileOpService,
            NullLogger<AssistantDailyLogService>.Instance);
    }

    [Fact]
    public async Task AppendEntryAsync_ShouldAddEntry_ToDailyLog()
    {
        // Arrange
        var sut = CreateSut();
        var content = "执行了代码重构操作";

        // Act
        var entry = await sut.AppendEntryAsync(content, DailyLogCategory.Action).ConfigureAwait(true);

        // Assert
        entry.Should().NotBeNull();
        entry.Content.Should().Be(content);
        entry.Category.Should().Be(DailyLogCategory.Action);

        // 验证日志文件中确实包含该条目
        var logFile = await sut.GetDailyLogAsync().ConfigureAwait(true);
        logFile.Entries.Should().ContainSingle(e => e.Content == content);
    }

    [Fact]
    public async Task GetDailyLogAsync_ShouldReturnTodayLog()
    {
        // Arrange
        var sut = CreateSut();
        await sut.AppendEntryAsync("观察到的信息", DailyLogCategory.Observation).ConfigureAwait(true);
        await sut.AppendEntryAsync("做出的决策", DailyLogCategory.Decision).ConfigureAwait(true);

        // Act
        var logFile = await sut.GetDailyLogAsync().ConfigureAwait(true);

        // Assert
        logFile.Should().NotBeNull();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        logFile.Date.Should().Be(today);
        logFile.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDailyLogForDateAsync_ShouldReturnEmptyLog_WhenNoLogForDate()
    {
        // Arrange
        var sut = CreateSut();
        var pastDate = DateTime.UtcNow.AddDays(-30);

        // Act
        var logFile = await sut.GetDailyLogForDateAsync(pastDate).ConfigureAwait(true);

        // Assert
        logFile.Should().NotBeNull();
        logFile.Date.Should().Be(pastDate.ToString("yyyy-MM-dd"));
        logFile.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildDailyLogPromptAsync_ShouldReturnNonEmptyString_WhenEntriesExist()
    {
        // Arrange
        var sut = CreateSut();
        await sut.AppendEntryAsync("执行了操作A", DailyLogCategory.Action).ConfigureAwait(true);
        await sut.AppendEntryAsync("观察到结果B", DailyLogCategory.Observation).ConfigureAwait(true);

        // Act
        var prompt = await sut.BuildDailyLogPromptAsync().ConfigureAwait(true);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("今日日志");
        prompt.Should().Contain("执行了操作A");
        prompt.Should().Contain("观察到结果B");
    }

    [Fact]
    public async Task BuildDailyLogPromptAsync_ShouldReturnEmptyString_WhenNoEntries()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var prompt = await sut.BuildDailyLogPromptAsync().ConfigureAwait(true);

        // Assert
        prompt.Should().BeEmpty();
    }

    public void Dispose()
    {
        _fileOpService.Dispose();
    }
}
