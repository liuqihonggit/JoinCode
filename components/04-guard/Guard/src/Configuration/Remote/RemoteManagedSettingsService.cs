
namespace Core.Configuration.Remote;

[Register]
public sealed partial class RemoteManagedSettingsService : IRemoteSettingsService, IDisposable
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<RemoteManagedSettingsService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly RemoteSettingsOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;
    private readonly ConcurrentDictionary<string, ManagedSetting> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCts = new();
    private DateTime _lastFetchTime = DateTime.MinValue;
    private bool _disposed;

    private static readonly RemoteSettingsJsonContext JsonContext = RemoteSettingsJsonContext.Default;

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public RemoteManagedSettingsService(
        HttpClient httpClient,
        IOptions<RemoteSettingsOptions>? options = null,
        ILogger<RemoteManagedSettingsService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _options = options?.Value ?? new RemoteSettingsOptions();

        if (!string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _refreshTimer = new Timer(
                _ => _ = RefreshAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false),
                null,
                _options.RefreshInterval,
                _options.RefreshInterval);
        }
        else
        {
            _refreshTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        return _settings.TryGetValue(key, out var setting) ? setting.Value : null;
    }

    public async Task<T?> GetSettingAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        var value = await GetSettingAsync(key, cancellationToken).ConfigureAwait(false);

        if (value == null)
        {
            return defaultValue;
        }

        try
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)value;
            }

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)bool.Parse(value);
            }

            if (typeof(T) == typeof(int))
            {
                return (T)(object)int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (typeof(T) == typeof(double))
            {
                return (T)(object)double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            return defaultValue;
        }
        catch (FormatException)
        {
            _logger?.LogWarning("无法将设置 '{Key}' 的值 '{Value}' 转换为类型 {Type}", key, value, typeof(T).Name);
            return defaultValue;
        }
    }

    public async Task<IReadOnlyDictionary<string, ManagedSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return _settings.ToFrozenDictionary();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiEndpoint))
        {
            _logger?.LogDebug("未配置远程设置 API 端点，跳过刷新");
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger?.LogDebug("正在刷新远程托管设置");

            var requestUrl = _options.ApiEndpoint;
            if (!string.IsNullOrEmpty(_options.ClientKey))
            {
                var separator = requestUrl.Contains('?') ? "&" : "?";
                requestUrl = $"{requestUrl}{separator}clientKey={Uri.EscapeDataString(_options.ClientKey)}";
            }

            var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var settingsResponse = JsonSerializer.Deserialize(json, JsonContext.RemoteSettingsResponse);

            if (settingsResponse?.Settings != null)
            {
                var previousSettings = new Dictionary<string, ManagedSetting>(_settings);

                _settings.Clear();
                foreach (var setting in settingsResponse.Settings)
                {
                    _settings[setting.Key] = setting;
                }

                NotifyChanges(previousSettings);
                _lastFetchTime = _clock.GetUtcNow();

                _logger?.LogInformation("已刷新 {Count} 条远程托管设置", settingsResponse.Settings.Count);
                RecordRemoteSettingsMetrics("refresh", true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "刷新远程托管设置失败");
            RecordRemoteSettingsMetrics("refresh", false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetMergedSettingsAsync(Dictionary<string, string> localSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localSettings);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var merged = new Dictionary<string, string>(localSettings, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _settings)
        {
            if (kvp.Value.IsReadOnly || kvp.Value.Scope == SettingScope.System || kvp.Value.Scope == SettingScope.Organization)
            {
                merged[kvp.Key] = kvp.Value.Value;
            }
            else if (!merged.ContainsKey(kvp.Key))
            {
                merged[kvp.Key] = kvp.Value.Value;
            }
        }

        return merged.ToFrozenDictionary();
    }

    private void NotifyChanges(Dictionary<string, ManagedSetting> previousSettings)
    {
        if (!_options.EnableNotifications) return;

        foreach (var kvp in _settings)
        {
            var hasPrevious = previousSettings.TryGetValue(kvp.Key, out var previous);
            if (!hasPrevious || previous?.Value != kvp.Value.Value)
            {
                SettingChanged?.Invoke(this, new SettingChangedEventArgs
                {
                    Key = kvp.Key,
                    OldValue = previous?.Value,
                    NewValue = kvp.Value.Value,
                    Scope = kvp.Value.Scope
                });
            }
        }

        foreach (var kvp in previousSettings)
        {
            if (!_settings.ContainsKey(kvp.Key))
            {
                SettingChanged?.Invoke(this, new SettingChangedEventArgs
                {
                    Key = kvp.Key,
                    OldValue = kvp.Value.Value,
                    NewValue = null,
                    Scope = kvp.Value.Scope
                });
            }
        }
    }

    private void RecordRemoteSettingsMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("settings.remote.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Remote settings operation count");

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_settings.IsEmpty || (_options.EnableCache && _clock.GetUtcNow() - _lastFetchTime > _options.CacheExpiration))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposeCts.Cancel();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
        _disposeCts.Dispose();
        _disposed = true;
    }
}
