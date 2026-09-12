
namespace Core.Tests.Memdir;

public sealed class MemorySearchHistoryServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOpService;
    private readonly MemoryStore _memoryStore;
    private readonly string _tempBasePath;

    public MemorySearchHistoryServiceTests()
    {
        _fileOpService = new InMemoryFileOperationService();
        _tempBasePath = "/test/memdir/search-history";
        _memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = Path.Combine(_tempBasePath, "store.json") }),
            _fileOpService,
            NullLogger<MemoryStore>.Instance);
    }

    private MemorySearchHistoryService CreateSut()
    {
        return new MemorySearchHistoryService(
            _memoryStore,
            NullLogger<MemorySearchHistoryService>.Instance);
    }

    [Fact]
    public async Task RecordSearchAsync_ShouldAddSearchToHistory()
    {
        // Arrange
        var sut = CreateSut();
        var query = "如何优化性能";
        var topIds = ImmutableList.Create("mem1", "mem2");

        // Act
        var entry = await sut.RecordSearchAsync(query, resultCount: 5, topIds).ConfigureAwait(true);

        // Assert
        entry.Should().NotBeNull();
        entry.Query.Should().Be(query);
        entry.ResultCount.Should().Be(5);
        entry.TopMemoryIds.Should().BeEquivalentTo(topIds);
        entry.SearchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetRecentSearches_ShouldReturnRecordedSearches()
    {
        // Arrange
        var sut = CreateSut();
        await sut.RecordSearchAsync("查询1", resultCount: 3).ConfigureAwait(true);
        await sut.RecordSearchAsync("查询2", resultCount: 5).ConfigureAwait(true);

        // Act
        var searches = sut.GetRecentSearches();

        // Assert
        searches.Should().HaveCount(2);
        // 最新的排在前面
        searches[0].Query.Should().Be("查询2");
        searches[1].Query.Should().Be("查询1");
    }

    [Fact]
    public async Task GetRecentSearches_ShouldRespectLimit()
    {
        // Arrange
        var sut = CreateSut();
        for (var i = 0; i < 15; i++)
        {
            await sut.RecordSearchAsync($"查询{i}", resultCount: i).ConfigureAwait(true);
        }

        // Act
        var searches = sut.GetRecentSearches(limit: 5);

        // Assert
        searches.Should().HaveCount(5);
    }

    [Fact]
    public async Task BuildSearchingPastContextSectionAsync_ShouldReturnSection_WithPromptText()
    {
        // Arrange
        var sut = CreateSut();

        // 在 MemoryStore 中添加一些过往对话记忆（内容包含查询关键词）
        _memoryStore.AddMemory("performance optimization feedback for database queries", MemoryType.Feedback, title: "performance feedback");
        _memoryStore.AddMemory("user prefers async programming patterns", MemoryType.User, title: "user preference");

        // Act
        var section = await sut.BuildSearchingPastContextSectionAsync("performance optimization").ConfigureAwait(true);

        // Assert
        section.Should().NotBeNull();
        section.PromptText.Should().NotBeNullOrEmpty();
        section.ReferencedMemoryCount.Should().BeGreaterThan(0);
        section.ReferencedMemoryIds.Should().NotBeEmpty();
        section.BuiltAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SearchPastConversationsAsync_ShouldSearchMemoryStore()
    {
        // Arrange
        var sut = CreateSut();

        // 添加不同类型的记忆（内容包含查询关键词）
        _memoryStore.AddMemory("database query optimization solution", MemoryType.Feedback, title: "optimization solution");
        _memoryStore.AddMemory("user likes dark theme", MemoryType.User, title: "theme preference");
        _memoryStore.AddMemory("project architecture design document", MemoryType.Project, title: "architecture document");

        // Act
        var results = await sut.SearchPastConversationsAsync("optimization").ConfigureAwait(true);

        // Assert
        results.Should().NotBeNull();
        results.Should().Contain(m => m.Type == MemoryType.Feedback);
    }

    public void Dispose()
    {
        _fileOpService.Dispose();
    }
}
