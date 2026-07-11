namespace Core.Tests.Services.FeatureFlags;

public sealed class FeatureFlagServiceTests : IDisposable
{
    private readonly FeatureFlagService _service;
    private readonly HttpClient _httpClient;

    public FeatureFlagServiceTests()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new FeatureFlagOptions
        {
            EnableCache = false
        });
        _service = new FeatureFlagService(_httpClient, options);
    }

    public void Dispose()
    {
        _service.Dispose();
        _httpClient.Dispose();
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, FeatureFlag> GetCache(FeatureFlagService service)
    {
        var cacheField = typeof(FeatureFlagService).GetField("_cache", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, FeatureFlag>)cacheField!.GetValue(service)!;
    }

    [Fact]
    public async Task IsEnabledAsync_WithEnabledFlag_ReturnsTrue()
    {
        var cache = GetCache(_service);
        cache["test-feature"] = new FeatureFlag
        {
            Key = "test-feature",
            Enabled = true,
            RolloutPercentage = 100.0
        };

        var result = await _service.IsEnabledAsync("test-feature").ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_WithDisabledFlag_ReturnsFalse()
    {
        var cache = GetCache(_service);
        cache["disabled-feature"] = new FeatureFlag
        {
            Key = "disabled-feature",
            Enabled = false,
            RolloutPercentage = 100.0
        };

        var result = await _service.IsEnabledAsync("disabled-feature").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_WithNonExistentFlag_ReturnsFalse()
    {
        var result = await _service.IsEnabledAsync("nonexistent-feature").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_WithPercentageRollout_RespectsPercentage()
    {
        var cache = GetCache(_service);
        cache["full-rollout"] = new FeatureFlag { Key = "full-rollout", Enabled = true, RolloutPercentage = 100.0 };
        cache["zero-rollout"] = new FeatureFlag { Key = "zero-rollout", Enabled = true, RolloutPercentage = 0.0 };

        var fullResult = await _service.IsEnabledAsync("full-rollout").ConfigureAwait(true);
        var zeroResult = await _service.IsEnabledAsync("zero-rollout").ConfigureAwait(true);

        fullResult.Should().BeTrue();
        zeroResult.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_WithNoEndpoint_DoesNotThrow()
    {
        var act = () => _service.RefreshAsync();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetAllFlagsAsync_ReturnsAllFlags()
    {
        var cache = GetCache(_service);
        cache["flag-a"] = new FeatureFlag { Key = "flag-a", Enabled = true, RolloutPercentage = 100.0 };
        cache["flag-b"] = new FeatureFlag { Key = "flag-b", Enabled = false, RolloutPercentage = 50.0 };

        var flags = await _service.GetAllFlagsAsync().ConfigureAwait(true);

        flags.Should().HaveCount(2);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new FeatureFlagService(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
