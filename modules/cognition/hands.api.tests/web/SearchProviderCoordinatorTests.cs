namespace Core.Tests.Web;

/// <summary>
/// SearchProviderCoordinator 单元测试 — 验证多供应商调度、优先级排序、失败回退、错误聚合（ADR 0138）。
/// </summary>
public sealed class SearchProviderCoordinatorTests {
    /// <summary>
    /// 创建可配置的 fake 搜索供应商。
    /// </summary>
    private sealed class FakeSearchProvider : ISearchProvider {
        private readonly SearchProviderResult _result;
        public string Name { get; }
        public int Priority { get; }
        public bool IsAvailable { get; }
        public bool WasInvoked { get; private set; }

        public FakeSearchProvider(string name, int priority, bool isAvailable, SearchProviderResult result) {
            Name = name;
            Priority = priority;
            IsAvailable = isAvailable;
            _result = result;
        }

        public Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
            WasInvoked = true;
            return Task.FromResult(_result);
        }
    }

    private static SearchProviderResult SuccessResult(string name, int count = 1) => new(
        true, name,
        Enumerable.Range(0, count).Select(i => new SearchResultItem($"Title{i}", $"https://example{i}.com")).ToList());

    private static SearchProviderResult FailureResult(string name, string error) => new(
        false, name, [], 0, ErrorMessage: error);

    [Fact]
    public async Task SearchAsync_SingleAvailableProvider_ReturnsItsResult() {
        var provider = new FakeSearchProvider("Tavily", 100, true, SuccessResult("Tavily"));
        await using var coordinator = new SearchProviderCoordinator(new[] { provider });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        provider.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_FirstProviderSucceeds_DoesNotInvokeSecond() {
        var first = new FakeSearchProvider("Tavily", 100, true, SuccessResult("Tavily"));
        var second = new FakeSearchProvider("Serper", 200, true, SuccessResult("Serper"));
        await using var coordinator = new SearchProviderCoordinator(new[] { first, second });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        first.WasInvoked.Should().BeTrue();
        second.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_FirstFails_SecondSucceeds_ReturnsSecond() {
        var first = new FakeSearchProvider("Tavily", 100, true, FailureResult("Tavily", "API error"));
        var second = new FakeSearchProvider("Serper", 200, true, SuccessResult("Serper"));
        await using var coordinator = new SearchProviderCoordinator(new[] { first, second });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        first.WasInvoked.Should().BeTrue();
        second.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_AllFail_AggregatesErrors() {
        var first = new FakeSearchProvider("Tavily", 100, true, FailureResult("Tavily", "API error"));
        var second = new FakeSearchProvider("Serper", 200, true, FailureResult("Serper", "Timeout"));
        await using var coordinator = new SearchProviderCoordinator(new[] { first, second });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Tavily");
        result.ErrorMessage.Should().Contain("API error");
        result.ErrorMessage.Should().Contain("Serper");
        result.ErrorMessage.Should().Contain("Timeout");
    }

    [Fact]
    public async Task SearchAsync_UnavailableProvider_Skipped() {
        var unavailable = new FakeSearchProvider("Tavily", 100, false, FailureResult("Tavily", "should not be called"));
        var available = new FakeSearchProvider("Serper", 200, true, SuccessResult("Serper"));
        await using var coordinator = new SearchProviderCoordinator(new[] { unavailable, available });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        unavailable.WasInvoked.Should().BeFalse();
        available.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_ProvidersOrderedByPriority_LowestFirst() {
        var lowPriority = new FakeSearchProvider("SubAgent", 999, true, SuccessResult("SubAgent"));
        var highPriority = new FakeSearchProvider("Tavily", 100, true, SuccessResult("Tavily"));
        await using var coordinator = new SearchProviderCoordinator(new[] { lowPriority, highPriority });

        await coordinator.SearchAsync(new SearchRequest("test"));

        highPriority.WasInvoked.Should().BeTrue();
        lowPriority.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_NoAvailableProviders_ReturnsConfigError() {
        var provider = new FakeSearchProvider("Tavily", 100, false, SuccessResult("Tavily"));
        await using var coordinator = new SearchProviderCoordinator(new[] { provider });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No search provider is available");
    }

    [Fact]
    public async Task SearchAsync_ProviderThrows_ContinuesToNext() {
        var throwing = new ThrowingSearchProvider("Tavily", 100);
        var fallback = new FakeSearchProvider("Serper", 200, true, SuccessResult("Serper"));
        await using var coordinator = new SearchProviderCoordinator(new ISearchProvider[] { throwing, fallback });

        var result = await coordinator.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        fallback.WasInvoked.Should().BeTrue();
    }

    private sealed class ThrowingSearchProvider : ISearchProvider {
        public ThrowingSearchProvider(string name, int priority) {
            Name = name;
            Priority = priority;
        }
        public string Name { get; }
        public int Priority { get; }
        public bool IsAvailable => true;
        public Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Network error");
    }
}
