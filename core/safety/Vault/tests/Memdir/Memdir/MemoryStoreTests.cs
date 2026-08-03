
namespace Core.Tests.Memdir;

public class MemoryStoreTests : IDisposable
{
    private readonly string _tempStoragePath;
    private readonly MemoryStore _store;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;

    public MemoryStoreTests()
    {
        _tempStoragePath = "/test/memories.json";
        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _store = new MemoryStore(Options.Create(new MemdirOptions { StoragePath = _tempStoragePath }), _fileOperationServiceMock.Object, NullLogger<MemoryStore>.Instance);
    }

    public void Dispose()
    {
    }

    [Fact]
    public void AddMemory_ShouldAddToStore()
    {
        // Arrange
        var content = "Test memory content";

        // Act
        _store.AddMemory(content);

        // Assert - AddMemory 已经同步添加到内存字典，无需等待
        var results = _store.Search(content).ToList();
        results.Should().ContainSingle();
        results.First().Content.Should().Be(content);
    }

    [Fact]
    public void AddMemory_WithType_ShouldStoreType()
    {
        // Arrange
        var content = "Type test memory";
        var type = MemoryType.Project;

        // Act
        _store.AddMemory(content, type);

        // Assert
        var results = _store.Search(content, type).ToList();
        results.Should().ContainSingle();
        results.First().Type.Should().Be(type);
    }

    [Fact]
    public void AddMemory_WithTags_ShouldStoreTags()
    {
        // Arrange
        var content = "Tagged memory test";
        var tags = new List<string> { "tag1", "tag2" };

        // Act
        _store.AddMemory(content, tags: tags);

        // Assert
        var results = _store.SearchByTags(tags).ToList();
        results.Should().ContainSingle();
        results.First().Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void AddMemory_WithSource_ShouldStoreSource()
    {
        // Arrange
        var content = "Source test memory";
        var source = "test_source.txt";

        // Act
        _store.AddMemory(content, source: source);

        // Assert
        var results = _store.Search(content).ToList();
        results.First().Source.Should().Be(source);
    }

    [Fact]
    public void Search_WithQuery_ShouldReturnMatchingMemories()
    {
        // Arrange
        _store.AddMemory("apple fruit is sweet");
        _store.AddMemory("banana fruit is yellow");
        _store.AddMemory("carrot vegetable is orange");

        // Act
        var results = _store.Search("fruit").ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(m => m.Content.Contains("apple"));
        results.Should().Contain(m => m.Content.Contains("banana"));
    }

    [Fact]
    public void Search_WithType_ShouldFilterByType()
    {
        // Arrange
        _store.AddMemory("work task 1", MemoryType.Project);
        _store.AddMemory("work task 2", MemoryType.Project);
        _store.AddMemory("personal note", MemoryType.User);

        // Act
        var results = _store.Search("task", MemoryType.Project).ToList();

        // Assert
        results.Should().HaveCount(2);
        results.All(m => m.Type == MemoryType.Project).Should().BeTrue();
    }

    [Fact]
    public void Search_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            _store.AddMemory($"Memory number {i}");
        }

        // Act
        var results = _store.Search("Memory", limit: 5).ToList();

        // Assert
        results.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public void Search_NoMatch_ShouldReturnEmpty()
    {
        // Act
        var results = _store.Search("xyz_nonexistent_12345");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void SearchByTags_ShouldReturnMatchingMemories()
    {
        // Arrange
        _store.AddMemory("Memory with tag1", tags: new List<string> { "tag1", "tag2" });
        _store.AddMemory("Memory with tag2", tags: new List<string> { "tag2", "tag3" });
        _store.AddMemory("Memory with tag3", tags: new List<string> { "tag3" });

        // Act
        var results = _store.SearchByTags(new List<string> { "tag2" }).ToList();

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public void SearchByType_ShouldReturnMatchingMemories()
    {
        // Arrange
        _store.AddMemory("Feedback 1", MemoryType.Feedback);
        _store.AddMemory("Feedback 2", MemoryType.Feedback);
        _store.AddMemory("User note", MemoryType.User);

        // Act
        var results = _store.SearchByType(MemoryType.Feedback).ToList();

        // Assert
        results.Should().HaveCount(2);
        results.All(m => m.Type == MemoryType.Feedback).Should().BeTrue();
    }

    [Fact]
    public void GetMemory_ShouldReturnMemory()
    {
        // Arrange
        _store.AddMemory("Test memory for get");
        var allMemories = _store.Search("Test memory for get");
        var memoryId = allMemories.First().Id;

        // Act
        var memory = _store.GetMemory(memoryId);

        // Assert
        memory.Should().NotBeNull();
        memory!.Content.Should().Contain("Test memory for get");
    }

    [Fact]
    public void GetMemory_NonExistent_ShouldReturnNull()
    {
        // Act
        var memory = _store.GetMemory("nonexistent_id");

        // Assert
        memory.Should().BeNull();
    }

    [Fact]
    public void DeleteMemory_Existing_ShouldRemoveMemory()
    {
        // Arrange
        _store.AddMemory("Memory to delete");
        var memory = _store.Search("Memory to delete").First();
        var memoryId = memory.Id;

        // Act
        var result = _store.DeleteMemory(memoryId);

        // Assert
        result.Should().BeTrue();
        _store.Search("Memory to delete").Should().BeEmpty();
    }

    [Fact]
    public void DeleteMemory_NonExistent_ShouldReturnFalse()
    {
        // Act
        var result = _store.DeleteMemory("nonexistent_id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ArchiveMemory_Existing_ShouldArchiveMemory()
    {
        // Arrange
        _store.AddMemory("Memory to archive");
        var memory = _store.Search("Memory to archive").First();

        // Act
        var result = _store.ArchiveMemory(memory.Id);

        // Assert
        result.Should().BeTrue();
        var archived = _store.GetMemory(memory.Id);
        archived!.IsArchived.Should().BeTrue();
        archived.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public void ArchiveMemory_NonExistent_ShouldReturnFalse()
    {
        // Act
        var result = _store.ArchiveMemory("nonexistent_id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetTypes_ShouldReturnAllTypes()
    {
        // Arrange
        _store.AddMemory("Work task 1", MemoryType.Project);
        _store.AddMemory("Work task 2", MemoryType.Project);
        _store.AddMemory("Personal note", MemoryType.User);

        // Act
        var types = _store.GetTypes().ToList();

        // Assert
        types.Should().Contain(MemoryType.Project);
        types.Should().Contain(MemoryType.User);
    }

    [Fact]
    public void GetAllTags_ShouldReturnAllTags()
    {
        // Arrange
        _store.AddMemory("Memory 1", tags: new List<string> { "tag1", "tag2" });
        _store.AddMemory("Memory 2", tags: new List<string> { "tag2", "tag3" });

        // Act
        var tags = _store.GetAllTags().ToList();

        // Assert
        tags.Should().Contain("tag1");
        tags.Should().Contain("tag2");
        tags.Should().Contain("tag3");
    }

    [Fact]
    public void GetStatistics_ShouldReturnCorrectStats()
    {
        // Arrange
        _store.AddMemory("Work 1", MemoryType.Project);
        _store.AddMemory("Work 2", MemoryType.Project);
        _store.AddMemory("Personal", MemoryType.User);

        // Act
        var stats = _store.GetStatistics();

        // Assert
        stats.TotalCount.Should().Be(3);
        stats.TypeCounts[MemoryType.Project].Should().Be(2);
        stats.TypeCounts[MemoryType.User].Should().Be(1);
    }

    [Fact]
    public void MemoryEntry_ShouldHaveAutoGeneratedId()
    {
        // Arrange
        _store.AddMemory("Test memory");

        // Act
        var memory = _store.Search("Test memory").First();

        // Assert
        memory.Id.Should().NotBeNullOrEmpty();
        memory.Id.Length.Should().Be(16);
    }

    [Fact]
    public void MemoryEntry_ShouldTrackAccessCount()
    {
        // Arrange
        _store.AddMemory("Access count test");
        var memory = _store.Search("Access count test").First();
        var initialCount = memory.AccessCount;

        // Act
        _store.Search("Access count test");
        var afterSearch = _store.GetMemory(memory.Id);

        // Assert
        afterSearch!.AccessCount.Should().BeGreaterThan(initialCount);
    }

    [Fact]
    public void MemoryEntry_ShouldHaveCreatedAtTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;
        _store.AddMemory("Timestamp test");
        var after = DateTime.UtcNow;

        // Act
        var memory = _store.Search("Timestamp test").First();

        // Assert
        memory.CreatedAt.Should().BeOnOrAfter(before);
        memory.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void MemoryEntry_ShouldHaveLastAccessedAt()
    {
        // Arrange
        _store.AddMemory("Last accessed test");

        // Act
        var memory = _store.Search("Last accessed test").First();

        // Assert
        memory.LastAccessedAt.Should().BeOnOrAfter(memory.CreatedAt);
    }

    [Fact]
    public void MemoryEntry_ShouldHaveTtl()
    {
        // Arrange
        _store.AddMemory("TTL test", MemoryType.User);

        // Act
        var memory = _store.Search("TTL test").First();

        // Assert
        memory.Ttl.Should().BeGreaterThan(TimeSpan.Zero);
        memory.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void MemoryStatistics_DefaultValues_ShouldBeEmpty()
    {
        // Arrange & Act - empty store
        var stats = _store.GetStatistics();

        // Assert
        stats.TotalCount.Should().Be(0);
        stats.TypeCounts.Should().BeEmpty();
        stats.TagCounts.Should().BeEmpty();
        stats.MostAccessed.Should().BeEmpty();
        stats.RecentlyAdded.Should().BeEmpty();
        stats.ArchivedCount.Should().Be(0);
        stats.ExpiredCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_NonExistentFile_ShouldCreateEmptyStore()
    {
        // Arrange
        var newPath = "/test/new_memories.json";

        // Act
        var store = new MemoryStore(Options.Create(new MemdirOptions { StoragePath = newPath }), _fileOperationServiceMock.Object, NullLogger<MemoryStore>.Instance);

        // Assert
        store.Search("anything").Should().BeEmpty();
    }

    [Fact]
    public void SearchByTags_ReturnsLazyEnumerable()
    {
        // Arrange - 添加记忆
        _store.AddMemory("延迟求值测试", tags: new List<string> { "lazy" });

        // Act - 获取 IEnumerable，验证返回的不是已物化的 List
        var result = _store.SearchByTags(new List<string> { "lazy" });

        // Assert - 返回类型不应该是 List<T>，证明是延迟求值的 IEnumerable
        result.Should().NotBeOfType<List<MemoryEntry>>();
        // 验证迭代后能正确获取数据
        var materialized = result.ToList();
        materialized.Should().ContainSingle(m => m.Content == "延迟求值测试");
    }

    [Fact]
    public void SearchByType_ReturnsLazyEnumerable()
    {
        // Arrange - 添加记忆
        _store.AddMemory("延迟求值类型测试", MemoryType.Feedback);

        // Act - 获取 IEnumerable，验证返回的不是已物化的 List
        var result = _store.SearchByType(MemoryType.Feedback);

        // Assert - 返回类型不应该是 List<T>，证明是延迟求值的 IEnumerable
        result.Should().NotBeOfType<List<MemoryEntry>>();
        // 验证迭代后能正确获取数据
        var materialized = result.ToList();
        materialized.Should().ContainSingle(m => m.Content == "延迟求值类型测试");
    }

    [Fact]
    public void SearchByTags_MultipleIterations_ReturnsConsistentResults()
    {
        // Arrange - 添加多条记忆
        _store.AddMemory("标签迭代A", tags: new List<string> { "iter" });
        _store.AddMemory("标签迭代B", tags: new List<string> { "iter" });

        // Act - 获取延迟的 IEnumerable
        var result = _store.SearchByTags(new List<string> { "iter" });

        // 多次迭代应返回相同的结果（延迟求值每次迭代重新执行查询）
        var firstIteration = result.ToList();
        var secondIteration = result.ToList();

        // Assert - 两次迭代结果一致
        firstIteration.Should().HaveCount(2);
        secondIteration.Should().HaveCount(2);
        firstIteration.Select(m => m.Id).Order()
            .Should().BeEquivalentTo(secondIteration.Select(m => m.Id).Order());
    }

    [Fact]
    public void SearchByType_MultipleIterations_ReturnsConsistentResults()
    {
        // Arrange - 添加多条记忆
        _store.AddMemory("类型迭代A", MemoryType.Project);
        _store.AddMemory("类型迭代B", MemoryType.Project);

        // Act - 获取延迟的 IEnumerable
        var result = _store.SearchByType(MemoryType.Project);

        // 多次迭代应返回相同的结果（延迟求值每次迭代重新执行查询）
        var firstIteration = result.ToList();
        var secondIteration = result.ToList();

        // Assert - 两次迭代结果一致
        firstIteration.Should().HaveCount(2);
        secondIteration.Should().HaveCount(2);
        firstIteration.Select(m => m.Id).Order()
            .Should().BeEquivalentTo(secondIteration.Select(m => m.Id).Order());
    }

    [Fact]
    public void SearchByTags_WithLimit_RespectsLimitAfterMaterialization()
    {
        // Arrange - 添加5条匹配记忆
        for (int i = 0; i < 5; i++)
        {
            _store.AddMemory($"标签记忆 {i}", tags: new List<string> { "limit" });
        }

        // Act - 使用 limit=3，然后物化
        var result = _store.SearchByTags(new List<string> { "limit" }, limit: 3).ToList();

        // Assert - 物化后结果不超过 limit
        result.Should().HaveCount(3);
    }

    [Fact]
    public void SearchByType_WithLimit_RespectsLimitAfterMaterialization()
    {
        // Arrange - 添加5条匹配记忆
        for (int i = 0; i < 5; i++)
        {
            _store.AddMemory($"类型记忆 {i}", MemoryType.User);
        }

        // Act - 使用 limit=3，然后物化
        var result = _store.SearchByType(MemoryType.User, limit: 3).ToList();

        // Assert - 物化后结果不超过 limit
        result.Should().HaveCount(3);
    }
}
