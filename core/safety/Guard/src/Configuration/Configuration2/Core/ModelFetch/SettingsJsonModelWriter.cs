namespace Core.Configuration.ModelFetch;

/// <summary>
/// settings.json 模型列表写回器 — 直接操作 SettingsJson 对象，用 SettingsLoader.SaveSettingsAsync 保存
/// 写回前调用 IConfigChangeNotifier.MarkInternalWrite 防抖，避免自身写入触发循环刷新
/// </summary>
public sealed class SettingsJsonModelWriter
{
    private readonly IFileSystem _fs;
    private readonly IConfigChangeNotifier? _changeNotifier;
    private readonly ILogger<SettingsJsonModelWriter>? _logger;

    public SettingsJsonModelWriter(IFileSystem fs, IConfigChangeNotifier? changeNotifier = null, ILogger<SettingsJsonModelWriter>? logger = null)
    {
        _fs = fs;
        _changeNotifier = changeNotifier;
        _logger = logger;
    }

    /// <summary>
    /// 将合并后的模型列表写回 settings.json — 构建新 SettingsJson 对象，保留所有原有字段
    /// </summary>
    /// <param name="settings">当前 SettingsJson 对象（从文件加载）</param>
    /// <param name="updates">供应商名 → 合并后的模型列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WriteAsync(
        SettingsJson settings,
        IReadOnlyDictionary<string, List<ModelItemConfig>> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return;
        if (settings.Vendor is null) return;

        var newVendor = new Dictionary<string, ProfileSettings>(settings.Vendor, StringComparer.OrdinalIgnoreCase);
        foreach (var (profile, models) in updates)
        {
            if (!newVendor.TryGetValue(profile, out var oldProfile)) continue;
            newVendor[profile] = new ProfileSettings
            {
                Provider = oldProfile.Provider,
                Protocol = oldProfile.Protocol,
                Model = oldProfile.Model,
                Endpoint = oldProfile.Endpoint,
                ApiKeyEnvVar = oldProfile.ApiKeyEnvVar,
                Models = models,
                ModelsEndpoint = oldProfile.ModelsEndpoint,
            };
        }

        var newSettings = new SettingsJson
        {
            Vendor = newVendor,
            Current = settings.Current,
            AutoFetchModels = settings.AutoFetchModels,
        };

        var settingsPath = SettingsLoader.GetUserSettingsPath();
        _changeNotifier?.MarkInternalWrite(settingsPath);
        await SettingsLoader.SaveSettingsAsync(_fs, SettingSource.UserSettings, newSettings, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("[SettingsJsonModelWriter] 已更新 {Count} 个供应商的模型列表", updates.Count);
    }
}
