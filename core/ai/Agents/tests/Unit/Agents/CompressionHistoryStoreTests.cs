namespace Core.Tests.Agents;

/// <summary>
/// CompressionHistoryStore 单元测试
/// 测试压缩历史存储的添加、查询、增量读取、清空、统计和自动淘汰
/// </summary>
public sealed class CompressionHistoryStoreTests
{
    private static CompressionReport CreateReport(
        string? reportId = null,
        int originalTokens = 1000,
        int compressedTokens = 500,
        bool isSuccess = true,
        string? errorMessage = null)
    {
        return CompressionReport.Create(new CompressionReportOptions(
            OriginalTokenCount: originalTokens,
            CompressedTokenCount: compressedTokens,
            PreservedInfo: new List<string> { "preserved" },
            LostInfo: new List<string>(),
            IsSuccess: isSuccess,
            ErrorMessage: errorMessage));
    }

    [Fact]
    public async Task AddAsync_GetAllAsync_ReturnsAllReports()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 10);
        var r1 = CreateReport();
        var r2 = CreateReport();
        await store.AddAsync(r1).ConfigureAwait(true);
        await store.AddAsync(r2).ConfigureAwait(true);

        // Act
        var all = await store.GetAllAsync().ConfigureAwait(true);

        // Assert
        all.Should().HaveCount(2);
        all[0].Should().BeSameAs(r1);
        all[1].Should().BeSameAs(r2);
    }

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsEmptyList()
    {
        // Arrange
        using var store = new CompressionHistoryStore();

        // Act
        var all = await store.GetAllAsync().ConfigureAwait(true);

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task FindByIdAsync_ExistingId_ReturnsReport()
    {
        // Arrange
        using var store = new CompressionHistoryStore();
        var report = CreateReport();
        await store.AddAsync(report).ConfigureAwait(true);

        // Act
        var found = await store.FindByIdAsync(report.ReportId).ConfigureAwait(true);

        // Assert
        found.Should().NotBeNull();
        found!.ReportId.Should().Be(report.ReportId);
    }

    [Fact]
    public async Task FindByIdAsync_NonExistingId_ReturnsNull()
    {
        // Arrange
        using var store = new CompressionHistoryStore();
        await store.AddAsync(CreateReport()).ConfigureAwait(true);

        // Act
        var found = await store.FindByIdAsync("nonexistent-id").ConfigureAwait(true);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_EmptyStore_ReturnsNull()
    {
        // Arrange
        using var store = new CompressionHistoryStore();

        // Act
        var found = await store.FindByIdAsync("any-id").ConfigureAwait(true);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsMostRecentByTimestamp()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 100);
        var older = CreateReport();
        // Manually set timestamps via CompressionReport.Create with a slight delay
        await store.AddAsync(older).ConfigureAwait(true);
        await Task.Delay(10).ConfigureAwait(true);
        var newer = CreateReport();
        await store.AddAsync(newer).ConfigureAwait(true);

        // Act
        var recent = await store.GetRecentAsync(count: 1).ConfigureAwait(true);

        // Assert
        recent.Should().HaveCount(1);
        recent[0].Should().BeSameAs(newer, "应返回时间戳最新的报告");
    }

    [Fact]
    public async Task GetRecentAsync_CountExceedsTotal_ReturnsAll()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 100);
        await store.AddAsync(CreateReport()).ConfigureAwait(true);
        await store.AddAsync(CreateReport()).ConfigureAwait(true);

        // Act
        var recent = await store.GetRecentAsync(count: 10).ConfigureAwait(true);

        // Assert
        recent.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentAsync_EmptyStore_ReturnsEmptyList()
    {
        // Arrange
        using var store = new CompressionHistoryStore();

        // Act
        var recent = await store.GetRecentAsync().ConfigureAwait(true);

        // Assert
        recent.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_RemovesAllReports()
    {
        // Arrange
        using var store = new CompressionHistoryStore();
        await store.AddAsync(CreateReport()).ConfigureAwait(true);
        await store.AddAsync(CreateReport()).ConfigureAwait(true);

        // Act
        await store.ClearAsync().ConfigureAwait(true);
        var all = await store.GetAllAsync().ConfigureAwait(true);

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_EmptyStore_DoesNotThrow()
    {
        // Arrange
        using var store = new CompressionHistoryStore();

        // Act
        var ex = await Record.ExceptionAsync(() => store.ClearAsync()).ConfigureAwait(true);

        // Assert
        ex.Should().BeNull();
    }

    [Fact]
    public async Task GetStatisticsAsync_EmptyStore_ReturnsZeroStatistics()
    {
        // Arrange
        using var store = new CompressionHistoryStore();

        // Act
        var stats = await store.GetStatisticsAsync().ConfigureAwait(true);

        // Assert
        stats.Should().ContainKey("TotalOperations");
        stats["TotalOperations"].GetInt32().Should().Be(0);
        stats.Should().ContainKey("AverageCompressionRatio");
        stats["AverageCompressionRatio"].GetDouble().Should().Be(0.0);
        stats.Should().ContainKey("TotalTokensSaved");
        stats["TotalTokensSaved"].GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithReports_ReturnsCorrectStatistics()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 100);
        await store.AddAsync(CreateReport(originalTokens: 1000, compressedTokens: 500)).ConfigureAwait(true);
        await store.AddAsync(CreateReport(originalTokens: 2000, compressedTokens: 1000)).ConfigureAwait(true);

        // Act
        var stats = await store.GetStatisticsAsync().ConfigureAwait(true);

        // Assert
        stats["TotalOperations"].GetInt32().Should().Be(2);
        stats["SuccessfulOperations"].GetInt32().Should().Be(2);
        stats["FailedOperations"].GetInt32().Should().Be(0);
        // Average ratio: (0.5 + 0.5) / 2 = 0.5
        stats["AverageCompressionRatio"].GetDouble().Should().BeApproximately(0.5, 0.001);
        // Total tokens saved: (1000-500) + (2000-1000) = 1500
        stats["TotalTokensSaved"].GetInt32().Should().Be(1500);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithFailedReports_CountsFailedOperations()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 100);
        await store.AddAsync(CreateReport(isSuccess: true, originalTokens: 1000, compressedTokens: 500)).ConfigureAwait(true);
        await store.AddAsync(CreateReport(isSuccess: false, originalTokens: 800, compressedTokens: 800, errorMessage: "error")).ConfigureAwait(true);

        // Act
        var stats = await store.GetStatisticsAsync().ConfigureAwait(true);

        // Assert
        stats["TotalOperations"].GetInt32().Should().Be(2);
        stats["SuccessfulOperations"].GetInt32().Should().Be(1);
        stats["FailedOperations"].GetInt32().Should().Be(1);
        // Only successful report with CompressionRatio > 0 contributes to average
        stats["AverageCompressionRatio"].GetDouble().Should().BeApproximately(0.5, 0.001);
        // Tokens saved only from successful: 1000 - 500 = 500
        stats["TotalTokensSaved"].GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithReports_ContainsLastOperationTime()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 100);
        await store.AddAsync(CreateReport()).ConfigureAwait(true);

        // Act
        var stats = await store.GetStatisticsAsync().ConfigureAwait(true);

        // Assert
        stats.Should().ContainKey("LastOperationTime");
        var lastTime = stats["LastOperationTime"].GetString();
        lastTime.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AddAsync_ExceedsMaxSize_EvictsOldestReport()
    {
        // Arrange
        const int maxSize = 3;
        using var store = new CompressionHistoryStore(maxSize: maxSize);
        var r1 = CreateReport();
        var r2 = CreateReport();
        var r3 = CreateReport();
        var r4 = CreateReport();

        await store.AddAsync(r1).ConfigureAwait(true);
        await store.AddAsync(r2).ConfigureAwait(true);
        await store.AddAsync(r3).ConfigureAwait(true);

        // Act — add a 4th report, exceeding maxSize
        await store.AddAsync(r4).ConfigureAwait(true);
        var all = await store.GetAllAsync().ConfigureAwait(true);

        // Assert — oldest (r1) should be evicted
        all.Should().HaveCount(maxSize);
        all[0].Should().BeSameAs(r2, "r1 应被淘汰，r2 应为最旧");
        all[1].Should().BeSameAs(r3);
        all[2].Should().BeSameAs(r4);
    }

    [Fact]
    public async Task AddAsync_MaxSizeOne_OnlyLatestRemains()
    {
        // Arrange
        using var store = new CompressionHistoryStore(maxSize: 1);
        var r1 = CreateReport();
        var r2 = CreateReport();

        await store.AddAsync(r1).ConfigureAwait(true);

        // Act
        await store.AddAsync(r2).ConfigureAwait(true);
        var all = await store.GetAllAsync().ConfigureAwait(true);

        // Assert
        all.Should().HaveCount(1);
        all[0].Should().BeSameAs(r2, "maxSize=1 时只保留最新报告");
    }

    [Fact]
    public async Task AddAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var store = new CompressionHistoryStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var ex = await Record.ExceptionAsync(() =>
            store.AddAsync(CreateReport(), cts.Token)).ConfigureAwait(true);

        // Assert
        ex.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task FindByIdAsync_AfterEviction_EvictedReportNotFound()
    {
        // Arrange
        const int maxSize = 2;
        using var store = new CompressionHistoryStore(maxSize: maxSize);
        var r1 = CreateReport();
        await store.AddAsync(r1).ConfigureAwait(true);
        await store.AddAsync(CreateReport()).ConfigureAwait(true);
        await store.AddAsync(CreateReport()).ConfigureAwait(true); // evicts r1

        // Act
        var found = await store.FindByIdAsync(r1.ReportId).ConfigureAwait(true);

        // Assert
        found.Should().BeNull("被淘汰的报告不应再被找到");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var store = new CompressionHistoryStore();

        // Act
        var ex = Record.Exception(() => store.Dispose());

        // Assert
        ex.Should().BeNull();
    }
}
