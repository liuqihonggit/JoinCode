namespace Core.Tests.Services.Configuration;

public sealed class RemoteManagedSettingsServiceTests : IDisposable
{
    private readonly RemoteManagedSettingsService _service;
    private readonly HttpClient _httpClient;

    public RemoteManagedSettingsServiceTests()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new RemoteSettingsOptions
        {
            EnableCache = false
        });
        _service = new RemoteManagedSettingsService(_httpClient, options);
    }

    public void Dispose()
    {
        _service.Dispose();
        _httpClient.Dispose();
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, ManagedSetting> GetSettings(RemoteManagedSettingsService service)
    {
        var settingsField = typeof(RemoteManagedSettingsService).BaseType!.GetField("_cache", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, ManagedSetting>)settingsField!.GetValue(service)!;
    }

    [Fact]
    public async Task GetSettingAsync_FromLocal_ReturnsValue()
    {
        var settings = GetSettings(_service);
        settings["test-key"] = new ManagedSetting
        {
            Key = "test-key",
            Value = "test-value",
            Scope = SettingScope.User,
            IsReadOnly = false
        };

        var result = await _service.GetSettingAsync("test-key").ConfigureAwait(true);

        result.Should().Be("test-value");
    }

    [Fact]
    public async Task GetSettingAsync_LocalMiss_ReturnsNull()
    {
        var result = await _service.GetSettingAsync("nonexistent-key").ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSettingAsync_Typed_ReturnsTypedValue()
    {
        var settings = GetSettings(_service);
        settings["bool-key"] = new ManagedSetting
        {
            Key = "bool-key",
            Value = "true",
            Scope = SettingScope.User,
            IsReadOnly = false
        };

        var result = await _service.GetSettingAsync<bool>("bool-key").ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetMergedSettingsAsync_CombinesLocalAndRemote()
    {
        var settings = GetSettings(_service);
        settings["remote-key"] = new ManagedSetting
        {
            Key = "remote-key",
            Value = "remote-value",
            Scope = SettingScope.User,
            IsReadOnly = false
        };

        var localSettings = new Dictionary<string, string>
        {
            ["local-key"] = "local-value"
        };

        var merged = await _service.GetMergedSettingsAsync(localSettings).ConfigureAwait(true);

        merged.Should().ContainKey("local-key");
        merged.Should().ContainKey("remote-key");
    }

    [Fact]
    public async Task GetMergedSettingsAsync_SystemLevelSetting_OverridesLocal()
    {
        var settings = GetSettings(_service);
        settings["override-key"] = new ManagedSetting
        {
            Key = "override-key",
            Value = "system-value",
            Scope = SettingScope.System,
            IsReadOnly = true
        };

        var localSettings = new Dictionary<string, string>
        {
            ["override-key"] = "local-value"
        };

        var merged = await _service.GetMergedSettingsAsync(localSettings).ConfigureAwait(true);

        merged["override-key"].Should().Be("system-value");
    }

    [Fact]
    public async Task GetMergedSettingsAsync_ReadOnlySetting_OverridesLocal()
    {
        var settings = GetSettings(_service);
        settings["readonly-key"] = new ManagedSetting
        {
            Key = "readonly-key",
            Value = "readonly-value",
            Scope = SettingScope.User,
            IsReadOnly = true
        };

        var localSettings = new Dictionary<string, string>
        {
            ["readonly-key"] = "local-value"
        };

        var merged = await _service.GetMergedSettingsAsync(localSettings).ConfigureAwait(true);

        merged["readonly-key"].Should().Be("readonly-value");
    }

    [Fact]
    public async Task RefreshAsync_WithNoEndpoint_DoesNotThrow()
    {
        var act = () => _service.RefreshAsync();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new RemoteManagedSettingsService(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
