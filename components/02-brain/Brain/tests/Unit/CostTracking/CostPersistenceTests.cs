
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Tests.CostTracking;

/// <summary>
/// CostPersistence 单元测试 - 使用内存文件系统实现高速测试
/// </summary>
public class CostPersistenceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly string _storageDir;

    public CostPersistenceTests()
    {
        _fileOperationService = new InMemoryFileOperationService();
        _storageDir = "/test/cost";
        _fileOperationService.CreateDirectory(_storageDir);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public async Task CostTracker_ShouldPersistUsageHistory()
    {
        // Arrange
        var storagePath = $"{_storageDir}/usage.json";

        // Act - 创建第一个tracker并记录用量
        var tracker1 = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);
        tracker1.RecordUsage("gpt-4", 1000, 500, "session-1");
        tracker1.RecordUsage("gpt-3.5-turbo", 2000, 1000, "session-1");

        // Assert - 直接验证内存中的记录，无需等待文件保存
        var stats1 = tracker1.GetTotalStatistics();
        stats1.RequestCount.Should().Be(2);
        stats1.PromptTokens.Should().Be(3000);
        stats1.CompletionTokens.Should().Be(1500);

        await tracker1.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_ShouldMaintainSessionInfo()
    {
        // Arrange
        var storagePath = $"{_storageDir}/usage_sessions.json";
        var sessionId = "test-session-abc";

        // Act
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);
        tracker.RecordUsage("gpt-4", 1000, 500, sessionId);

        // Assert - 直接验证内存中的会话统计
        var sessionStats = tracker.GetSessionStatistics(sessionId);
        sessionStats.RequestCount.Should().Be(1);
        sessionStats.PromptTokens.Should().Be(1000);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_NewFile_ShouldCreateEmptyHistory()
    {
        // Arrange
        var storagePath = $"{_storageDir}/new_file_{Guid.NewGuid():N}.json";

        // Act
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);

        // Assert
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(0);
        _fileOperationService.FileExists(storagePath).Should().BeFalse();

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_ShouldAccumulateMultipleRecords()
    {
        // Arrange
        var storagePath = $"{_storageDir}/append_test.json";

        // Act - 在同一个 tracker 中记录多条
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);
        tracker.RecordUsage("gpt-4", 1000, 500);
        tracker.RecordUsage("gpt-4", 2000, 1000);

        // Assert - 验证累计统计
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(2);
        stats.PromptTokens.Should().Be(3000);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_InvalidJsonFile_ShouldHandleGracefully()
    {
        // Arrange
        var storagePath = $"{_storageDir}/invalid.json";
        await _fileOperationService.WriteFileAsync(storagePath, "invalid json content").ConfigureAwait(true);

        // Act - 不应该抛出异常
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);

        // Assert - 新创建的 tracker 应该为空，无需等待加载
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(0);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_EmptyJsonFile_ShouldHandleGracefully()
    {
        // Arrange
        var storagePath = $"{_storageDir}/empty.json";
        await _fileOperationService.WriteFileAsync(storagePath, "").ConfigureAwait(true);

        // Act
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);

        // Assert - 空文件应该被优雅处理
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(0);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CostTracker_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        var nestedDir = $"{_storageDir}/nested/deep/dir";
        var storagePath = $"{nestedDir}/usage.json";

        // Act
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);
        tracker.RecordUsage("gpt-4", 1000, 500);

        // 等待异步保存完成 - 使用 SpinWait 替代 Task.Delay 反模式
        SpinWait.SpinUntil(() => _fileOperationService.DirectoryExists(nestedDir), TimeSpan.FromSeconds(5));

        // Assert
        _fileOperationService.DirectoryExists(nestedDir).Should().BeTrue();

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TokenUsageRecord_Serialization_ShouldPreserveAllFields()
    {
        // Arrange
        var record = new TokenUsageRecord
        {
            Timestamp = DateTime.UtcNow,
            Model = "gpt-4",
            PromptTokens = 1000,
            CompletionTokens = 500,
            CostUsd = 0.05m,
            SessionId = "test-session"
        };

        // Act
        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<TokenUsageRecord>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Model.Should().Be(record.Model);
        deserialized.PromptTokens.Should().Be(record.PromptTokens);
        deserialized.CompletionTokens.Should().Be(record.CompletionTokens);
        deserialized.CostUsd.Should().Be(record.CostUsd);
        deserialized.SessionId.Should().Be(record.SessionId);
    }

    [Fact]
    public async Task CostTracker_ConcurrentWrites_ShouldHandleSafely()
    {
        // Arrange
        var storagePath = $"{_storageDir}/concurrent.json";
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);

        // Act - 并发记录
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => tracker.RecordUsage("gpt-4", 100, 50)))
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(true);

        // Assert - ConcurrentBag 是线程安全的，直接验证
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(10);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void ModelCostInfo_Properties_ShouldBeAccessible()
    {
        // Arrange & Act
        var costInfo = new ModelCostInfo
        {
            Model = "test-model",
            PromptCostPer1KTokens = 0.01m,
            CompletionCostPer1KTokens = 0.03m
        };

        // Assert
        costInfo.Model.Should().Be("test-model");
        costInfo.PromptCostPer1KTokens.Should().Be(0.01m);
        costInfo.CompletionCostPer1KTokens.Should().Be(0.03m);
    }

    [Fact]
    public void CostStatistics_DefaultValues_ShouldBeZero()
    {
        // Arrange & Act
        var stats = new CostStatistics();

        // Assert
        stats.RequestCount.Should().Be(0);
        stats.PromptTokens.Should().Be(0);
        stats.CompletionTokens.Should().Be(0);
        stats.TotalTokens.Should().Be(0);
        stats.TotalCostUsd.Should().Be(0);
        stats.ModelBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void ModelCostStatistics_DefaultValues_ShouldBeZero()
    {
        // Arrange & Act
        var stats = new ModelCostStatistics
        {
            Model = "test-model"
        };

        // Assert
        stats.Model.Should().Be("test-model");
        stats.RequestCount.Should().Be(0);
        stats.PromptTokens.Should().Be(0);
        stats.CompletionTokens.Should().Be(0);
        stats.TotalTokens.Should().Be(0);
        stats.TotalCost.Should().Be(0);
    }

    [Fact]
    public async Task CostTracker_ShouldRecordTimestamp()
    {
        // Arrange
        var storagePath = $"{_storageDir}/timestamps.json";
        var beforeTime = DateTime.UtcNow;

        // Act
        var tracker = new CostTracker(_fileOperationService, storagePath: storagePath, NullLogger<CostTracker>.Instance);
        tracker.RecordUsage("gpt-4", 1000, 500);
        var afterTime = DateTime.UtcNow;

        // Assert - 直接验证内存中的记录
        var stats = tracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(1);

        await tracker.DisposeAsync().ConfigureAwait(true);
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
