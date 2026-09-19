
namespace Bridge.Tests;

/// <summary>
/// FlushGate 单元测试
/// 测试批量添加、手动/自动刷新、事件触发
/// </summary>
public sealed class FlushGateTests : IAsyncDisposable {
    private readonly FlushGate<string> _sut = new(logger: NullLogger.Instance);

    private static FlushGate<string> CreateSut(FlushGateOptions? options = null) =>
        new(options, NullLogger.Instance);

    public ValueTask DisposeAsync() => _sut.DisposeAsync();

    [Fact]
    public async Task AddAsync_ShouldAddItemToBatch() {
        // Arrange
        await using var sut = CreateSut();

        // Act
        await sut.AddAsync("item-1").ConfigureAwait(true);

        // Assert
        var batchSize = await sut.GetCurrentBatchSizeAsync().ConfigureAwait(true);
        batchSize.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_ShouldRaiseBatchFlushed_WithItems() {
        // Arrange
        await using var sut = CreateSut();
        await sut.AddAsync("item-a").ConfigureAwait(true);
        await sut.AddAsync("item-b").ConfigureAwait(true);

        IReadOnlyList<string>? flushedItems = null;
        sut.BatchFlushed += (_, args) => flushedItems = args.Items;

        // Act
        await sut.FlushAsync().ConfigureAwait(true);

        // Assert
        flushedItems.Should().NotBeNull();
        flushedItems.Should().HaveCount(2);
        flushedItems.Should().Contain("item-a");
        flushedItems.Should().Contain("item-b");
    }

    [Fact]
    public async Task AddAsync_ShouldAutoFlush_WhenBatchIsFull() {
        // Arrange - MaxBatchSize=2，添加第2个条目时触发自动刷新
        var options = new FlushGateOptions { MaxBatchSize = 2, FlushIntervalMs = 60000, MaxWaitMs = 60000 };
        var sut = CreateSut(options);

        IReadOnlyList<string>? flushedItems = null;
        sut.BatchFlushed += (_, args) => flushedItems = args.Items;

        // Act
        await sut.AddAsync("item-1").ConfigureAwait(true);
        await sut.AddAsync("item-2").ConfigureAwait(true); // 达到 MaxBatchSize，应自动刷新

        // Assert
        flushedItems.Should().NotBeNull();
        flushedItems.Should().HaveCount(2);

        // 刷新后批次应清空
        var batchSize = await sut.GetCurrentBatchSizeAsync().ConfigureAwait(true);
        batchSize.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_ShouldClearBatch() {
        // Arrange
        await using var sut = CreateSut();
        await sut.AddAsync("item-x").ConfigureAwait(true);
        await sut.AddAsync("item-y").ConfigureAwait(true);

        // Act
        await sut.FlushAsync().ConfigureAwait(true);

        // Assert
        var batchSize = await sut.GetCurrentBatchSizeAsync().ConfigureAwait(true);
        batchSize.Should().Be(0);
    }

    [Fact]
    public async Task BatchFlushed_ShouldContainAllAddedItems() {
        // Arrange
        await using var sut = CreateSut();
        var allItems = new List<string>();
        sut.BatchFlushed += (_, args) => allItems.AddRange(args.Items);

        // Act - 添加5个条目并手动刷新
        for (var i = 0; i < 5; i++) {
            await sut.AddAsync($"item-{i}").ConfigureAwait(true);
        }

        await sut.FlushAsync().ConfigureAwait(true);

        // Assert
        allItems.Should().HaveCount(5);
        for (var i = 0; i < 5; i++) {
            allItems.Should().Contain($"item-{i}");
        }
    }
}