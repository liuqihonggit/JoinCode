
using JoinCode.Abstractions.Attributes;

namespace Core.Configuration;

/// <summary>
/// 配置服务实现 - 内存缓存 + 磁盘持久化 + 变更通知
/// 对齐 TS 版 ConfigTool: 双存储源(global/settings) + appStateKey 热更新同步
/// </summary>
[Register]
public sealed partial class ConfigurationService : IConfigurationService
{
    private readonly ConcurrentDictionary<string, string> _configurations = new();
    private readonly IFileSystem _fs;
    private readonly IRemoteSettingsService? _remoteSettingsService;
    private readonly IConfigChangeNotifier? _configChangeNotifier;
    [Inject] private readonly ILogger<ConfigurationService>? _logger;

    public event EventHandler<SettingChangeEventArgs>? SettingChanged;

    public ConfigurationService(IFileSystem fs, IRemoteSettingsService? remoteSettingsService = null, IConfigChangeNotifier? configChangeNotifier = null, ILogger<ConfigurationService>? logger = null)
    {
        _fs = fs;
        _remoteSettingsService = remoteSettingsService;
        _configChangeNotifier = configChangeNotifier;
        _logger = logger;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => GetAsync(key, SettingSource.UserSettings, cancellationToken);

    public async Task<string?> GetAsync(string key, SettingSource source, CancellationToken cancellationToken = default)
    {
        // 1. 先查内存缓存
        if (_configurations.TryGetValue(key, out var value))
            return value;

        // 2. 按存储源分流读取 — 对齐 TS: global → ~/.claude.json, settings → ~/.claude/settings.json
        try
        {
            string? diskValue = source == SettingSource.GlobalConfig
                ? await ConfigLoader.LoadSettingFromGlobalConfigAsync(key, _fs, cancellationToken).ConfigureAwait(false)
                : await ConfigLoader.LoadSettingFromSettingsJsonAsync(key, _fs, cancellationToken).ConfigureAwait(false);

            if (diskValue is not null)
            {
                _configurations[key] = diskValue;
                return diskValue;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "从 {Source} 读取设置失败: {Key}", source.ToValue(), key);
        }

        // 3. 最后查远程设置（仅 settings 源）
        if (source == SettingSource.UserSettings && _remoteSettingsService != null)
        {
            try
            {
                value = await _remoteSettingsService.GetSettingAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "获取远程设置失败: {Key}", key);
            }
        }

        return value;
    }

    public Task<bool> SetAsync(string key, string value, CancellationToken cancellationToken = default)
        => SetAsync(key, value, SettingSource.UserSettings, null, cancellationToken);

    public Task<bool> SetAsync(string key, string value, SettingSource source, CancellationToken cancellationToken = default)
        => SetAsync(key, value, source, null, cancellationToken);

    public async Task<bool> SetAsync(string key, string value, SettingSource source, string? appStateKey, CancellationToken cancellationToken = default)
    {
        // 获取旧值用于变更通知
        var oldValue = _configurations.TryGetValue(key, out var existing) ? existing : null;

        _configurations[key] = value;

        // 按存储源分流持久化 — 对齐 TS: global → saveGlobalConfig, settings → updateSettingsForSource
        try
        {
            // 对齐 TS markInternalWrite — 写入前标记内部写，防止 FileSystemWatcher 回声
            var targetPath = source == SettingSource.GlobalConfig
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder, AppDataConstants.GlobalConfigFileName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            _configChangeNotifier?.MarkInternalWrite(targetPath);

            if (source == SettingSource.GlobalConfig)
            {
                await ConfigLoader.SaveSettingToGlobalConfigAsync(key, value, _fs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ConfigLoader.SaveSettingToSettingsJsonAsync(key, value, _fs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "持久化设置到 {Source} 失败: {Key}", source.ToValue(), key);
        }

        // 触发变更通知（对齐 TS 版 appStateKey 热更新同步）
        OnSettingChanged(key, oldValue, value, source, appStateKey);

        return true;
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var oldValue = _configurations.TryGetValue(key, out var existing) ? existing : null;

        _configurations.TryRemove(key, out _);

        try
        {
            // 对齐 TS markInternalWrite — 写入前标记内部写
            var targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            _configChangeNotifier?.MarkInternalWrite(targetPath);

            await ConfigLoader.SaveSettingToSettingsJsonAsync(key, null, _fs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "从 settings.json 删除设置失败: {Key}", key);
        }

        // 触发变更通知
        OnSettingChanged(key, oldValue, null);

        return true;
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var localConfigurations = new Dictionary<string, string>(_configurations);

        if (_remoteSettingsService != null)
        {
            try
            {
                var merged = await _remoteSettingsService.GetMergedSettingsAsync(localConfigurations, cancellationToken).ConfigureAwait(false);
                foreach (var kvp in merged)
                {
                    if (!localConfigurations.ContainsKey(kvp.Key))
                    {
                        localConfigurations[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "合并远程设置失败");
            }
        }

        return localConfigurations;
    }

    private void OnSettingChanged(string key, string? oldValue, string? newValue, SettingSource source = SettingSource.UserSettings, string? appStateKey = null)
    {
        try
        {
            SettingChanged?.Invoke(this, new SettingChangeEventArgs
            {
                Key = key,
                OldValue = oldValue,
                NewValue = newValue,
                Source = source,
                AppStateKey = appStateKey
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "触发设置变更通知失败: {Key}", key);
        }
    }
}
