
namespace Core.Configuration.Remote;

/// <summary>
/// 远程托管设置服务 — 从远程端点拉取托管设置，提供本地与远程设置合并、变更通知能力
/// </summary>
[Register(typeof(RemoteCacheRefreshServiceBase<ManagedSetting>), ServiceLifetime.Singleton)]
[Register(typeof(IRemoteSettingsService), ServiceLifetime.Singleton)]
public sealed partial class RemoteManagedSettingsService : RemoteCacheRefreshServiceBase<ManagedSetting>, IRemoteSettingsService
{
    private static readonly RemoteSettingsJsonContext JsonContext = RemoteSettingsJsonContext.Default;

    /// <inheritdoc />
    protected override string MetricsPrefix => "settings.remote";

    /// <inheritdoc />
    protected override string RefreshLogLabel => "远程托管设置";

    /// <summary>
    /// 设置变更事件 — 当远程设置的值发生新增、修改或删除时触发
    /// </summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// 构造远程托管设置服务
    /// </summary>
    public RemoteManagedSettingsService(
        HttpClient httpClient,
        IOptions<RemoteSettingsOptions>? options = null,
        ILogger<RemoteManagedSettingsService>? logger = null,
        ITelemetryService? telemetryService = null,
        IClockService? clock = null)
        : base(httpClient, options?.Value ?? new RemoteSettingsOptions(), logger, telemetryService, clock)
    {
    }

    /// <inheritdoc />
    protected override async Task<RemoteRefreshResult<ManagedSetting>> FetchAndDeserializeAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var settingsResponse = RelaxedJsonSerializer.Deserialize(json, JsonContext.RemoteSettingsResponse);

        return new RemoteRefreshResult<ManagedSetting>
        {
            Items = settingsResponse?.Settings?.ToDictionary(s => s.Key, s => s, StringComparer.OrdinalIgnoreCase) ?? []
        };
    }

    /// <inheritdoc />
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var previousSettings = new Dictionary<string, ManagedSetting>(Cache);

        await base.RefreshAsync(cancellationToken).ConfigureAwait(false);

        NotifyChanges(previousSettings);
    }

    /// <summary>
    /// 异步获取指定键的设置值 — 首次调用会触发缓存加载
    /// </summary>
    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        return Cache.TryGetValue(key, out var setting) ? setting.Value : null;
    }

    /// <summary>
    /// 异步获取指定键的强类型设置值 — 支持 string/bool/int/double 类型转换，转换失败返回默认值
    /// </summary>
    public async Task<T?> GetSettingAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        var value = await GetSettingAsync(key, cancellationToken).ConfigureAwait(false);

        if (value == null) return defaultValue;

        try
        {
            if (typeof(T) == typeof(string)) return (T)(object)value;
            if (typeof(T) == typeof(bool)) return (T)(object)bool.Parse(value);
            if (typeof(T) == typeof(int)) return (T)(object)int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            if (typeof(T) == typeof(double)) return (T)(object)double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            return defaultValue;
        }
        catch (FormatException)
        {
            Logger?.LogWarning("无法将设置 '{Key}' 的值 '{Value}' 转换为类型 {Type}", key, value, typeof(T).Name);
            return defaultValue;
        }
    }

    /// <summary>
    /// 异步获取所有远程托管设置的只读快照
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ManagedSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);
        return Cache.ToFrozenDictionary();
    }

    /// <summary>
    /// 异步合并本地与远程设置 — 只读/System/Organization 级远程设置强制覆盖本地，其余仅填补本地缺失
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetMergedSettingsAsync(Dictionary<string, string> localSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localSettings);

        await EnsureCacheAsync(cancellationToken).ConfigureAwait(false);

        var merged = new Dictionary<string, string>(localSettings, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in Cache)
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
        var options = RefreshOptions as RemoteSettingsOptions;
        if (options?.EnableNotifications != true) return;

        foreach (var kvp in Cache)
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
            if (!Cache.ContainsKey(kvp.Key))
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
}
