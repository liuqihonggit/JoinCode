
namespace Core.Tests.Memdir;

/// <summary>
/// Memory 组件 DI 集成测试
/// 验证 IMemoryScanner、IMemoryTruncator、IMemoryRelevanceSelector、IMemoryAgeCalculator
/// 可以通过 DI 容器正确解析
/// </summary>
public sealed class MemoryComponentIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public MemoryComponentIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystem>(_ => TestFileSystem.Current);
        services.AddSingleton<IFileOperationService, InMemoryFileOperationService>();
        services.AddMemdirServices(_ => "/test/memdir/storage");

        // [Register] 特性的自动注册在 AddAutoRegisteredServices() 中（Sync 项目），
        // 此测试仅调用 AddMemdirServices，需手动注册这些类型
        services.AddSingleton<IMemoryPaths, MemoryPaths>();
        services.AddSingleton<IMemoryScanner, MemoryScanner>();
        services.AddSingleton<IMemoryTruncator, MemoryTruncator>();
        services.AddSingleton<IMemoryRelevanceSelector, MemoryRelevanceSelector>();
        services.AddSingleton<IMemoryAgeCalculator, MemoryAgeCalculator>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public void MemoryScanner_IsResolvableFromDI()
    {
        // Act
        var scanner = _serviceProvider.GetRequiredService<IMemoryScanner>();

        // Assert
        scanner.Should().NotBeNull();
        scanner.Should().BeOfType<MemoryScanner>();
    }

    [Fact]
    public void MemoryTruncator_IsResolvableFromDI()
    {
        // Act
        var truncator = _serviceProvider.GetRequiredService<IMemoryTruncator>();

        // Assert
        truncator.Should().NotBeNull();
        truncator.Should().BeOfType<MemoryTruncator>();
    }

    [Fact]
    public void MemoryRelevanceSelector_IsResolvableFromDI()
    {
        // Act
        var selector = _serviceProvider.GetRequiredService<IMemoryRelevanceSelector>();

        // Assert
        selector.Should().NotBeNull();
        selector.Should().BeOfType<MemoryRelevanceSelector>();
    }

    [Fact]
    public void MemoryAgeCalculator_IsResolvableFromDI()
    {
        // Act
        var calculator = _serviceProvider.GetRequiredService<IMemoryAgeCalculator>();

        // Assert
        calculator.Should().NotBeNull();
        calculator.Should().BeOfType<MemoryAgeCalculator>();
    }

    [Fact]
    public void AllMemoryComponents_AreSingletons()
    {
        // Act
        var scanner1 = _serviceProvider.GetRequiredService<IMemoryScanner>();
        var scanner2 = _serviceProvider.GetRequiredService<IMemoryScanner>();
        var truncator1 = _serviceProvider.GetRequiredService<IMemoryTruncator>();
        var truncator2 = _serviceProvider.GetRequiredService<IMemoryTruncator>();
        var selector1 = _serviceProvider.GetRequiredService<IMemoryRelevanceSelector>();
        var selector2 = _serviceProvider.GetRequiredService<IMemoryRelevanceSelector>();
        var calculator1 = _serviceProvider.GetRequiredService<IMemoryAgeCalculator>();
        var calculator2 = _serviceProvider.GetRequiredService<IMemoryAgeCalculator>();

        // Assert - 单例应返回同一实例
        ReferenceEquals(scanner1, scanner2).Should().BeTrue();
        ReferenceEquals(truncator1, truncator2).Should().BeTrue();
        ReferenceEquals(selector1, selector2).Should().BeTrue();
        ReferenceEquals(calculator1, calculator2).Should().BeTrue();
    }

    [Fact]
    public async Task MemoryRelevanceSelector_DelegatesToMemoryAgeCalculator()
    {
        // Arrange
        var selector = _serviceProvider.GetRequiredService<IMemoryRelevanceSelector>();
        var memories = new List<MemoryEntry>
        {
            MemoryEntry.Create(
                MemoryType.Project,
                "This is a test memory about C# performance optimization",
                title: "C# Performance Tips",
                tags: new[] { "csharp", "performance" })
        };

        // Act
        var result = await selector.SelectRelevantMemoriesAsync(memories, "C# performance", maxResults: 10).ConfigureAwait(true);

        // Assert - 应能正常返回结果（内部调用了 MemoryAgeCalculator）
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MemoryManagementService_UsesMemoryScanner()
    {
        // Arrange
        var fileOpService = new InMemoryFileOperationService();
        var memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = "/test/memdir/mms-scanner/store.json" }),
            fileOpService,
            NullLogger<MemoryStore>.Instance);

        var mockScanner = new Mock<IMemoryScanner>();
        mockScanner.Setup(s => s.ScanAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryEntry>());

        var sut = new MemoryManagementService(
            memoryStore,
            optional: new MemoryOptionalServices(MemoryScanner: mockScanner.Object),
            logger: NullLogger<MemoryManagementService>.Instance);

        // Act
        await sut.ScanMemoriesAsync("test query").ConfigureAwait(true);

        // Assert - ScanMemoriesAsync 应委托给 IMemoryScanner.ScanAllAsync
        mockScanner.Verify(
            s => s.ScanAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MemoryManagementService_UsesMemoryTruncator()
    {
        // Arrange
        var fileOpService = new InMemoryFileOperationService();
        var memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = "/test/memdir/mms-truncator/store.json" }),
            fileOpService,
            NullLogger<MemoryStore>.Instance);

        // 添加一条记忆以便截断器被调用
        memoryStore.AddMemory("This is a long content about C# performance optimization that should be truncated", MemoryType.Project, title: "Long Memory");

        var mockTruncator = new Mock<IMemoryTruncator>();
        mockTruncator.Setup(t => t.SmartTruncate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TruncationThreshold?>()))
            .Returns((string content, string query, TruncationThreshold? _) => content);

        var sut = new MemoryManagementService(
            memoryStore,
            optional: new MemoryOptionalServices(MemoryTruncator: mockTruncator.Object),
            logger: NullLogger<MemoryManagementService>.Instance);

        // Act
        await sut.ScanMemoriesAsync("C# performance").ConfigureAwait(true);

        // Assert - ScanMemoriesAsync 应委托给 IMemoryTruncator.SmartTruncate
        mockTruncator.Verify(
            t => t.SmartTruncate(It.IsAny<string>(), "C# performance", It.IsAny<TruncationThreshold?>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task MemoryManagementService_UsesRelevanceSelector()
    {
        // Arrange
        var fileOpService = new InMemoryFileOperationService();
        var memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = "/test/memdir/mms-selector/store.json" }),
            fileOpService,
            NullLogger<MemoryStore>.Instance);

        // 添加一条记忆以便相关性选择器被调用
        memoryStore.AddMemory("C# performance tips and tricks", MemoryType.Project, title: "Perf Tips");

        var mockSelector = new Mock<IMemoryRelevanceSelector>();
        mockSelector.Setup(s => s.SelectRelevantMemoriesAsync(
                It.IsAny<IEnumerable<MemoryEntry>>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScoredMemory>());

        var sut = new MemoryManagementService(
            memoryStore,
            optional: new MemoryOptionalServices(RelevanceSelector: mockSelector.Object),
            logger: NullLogger<MemoryManagementService>.Instance);

        // Act
        await sut.ScanMemoriesAsync("C# performance").ConfigureAwait(true);

        // Assert - ScanMemoriesAsync 应委托给 IMemoryRelevanceSelector.SelectRelevantMemoriesAsync
        mockSelector.Verify(
            s => s.SelectRelevantMemoriesAsync(
                It.IsAny<IEnumerable<MemoryEntry>>(),
                "C# performance",
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MemoryManagementService_UsesAgeCalculator()
    {
        // Arrange
        var fileOpService = new InMemoryFileOperationService();
        var memoryStore = new MemoryStore(
            Options.Create(new MemdirOptions { StoragePath = "/test/memdir/mms-age/store.json" }),
            fileOpService,
            NullLogger<MemoryStore>.Instance);

        // 添加记忆以便 GetMemoryAgeInfoAsync 有数据
        memoryStore.AddMemory("Test memory content for age calculation", MemoryType.Project, title: "Age Test");

        var mockAgeCalculator = new Mock<IMemoryAgeCalculator>();
        mockAgeCalculator.Setup(c => c.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(0.5);
        mockAgeCalculator.Setup(c => c.ShouldArchive(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()))
            .Returns(false);

        var sut = new MemoryManagementService(
            memoryStore,
            optional: new MemoryOptionalServices(AgeCalculator: mockAgeCalculator.Object),
            logger: NullLogger<MemoryManagementService>.Instance);

        // Act
        await sut.GetMemoryAgeInfoAsync().ConfigureAwait(true);

        // Assert - GetMemoryAgeInfoAsync 应委托给 IMemoryAgeCalculator.CalculateAgedRelevance 和 ShouldArchive
        mockAgeCalculator.Verify(
            c => c.CalculateAgedRelevance(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()),
            Times.AtLeastOnce);
        mockAgeCalculator.Verify(
            c => c.ShouldArchive(It.IsAny<MemoryEntry>(), It.IsAny<DateTime?>()),
            Times.AtLeastOnce);
    }
}
