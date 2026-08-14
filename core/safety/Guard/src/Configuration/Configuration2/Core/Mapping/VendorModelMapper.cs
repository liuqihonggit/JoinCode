
namespace Core.Configuration;

/// <summary>
/// SettingsJson.Vendor → ModelConfigRoot 转换器
/// 将 settings.json 的 vendor[profile].models 映射为 ModelConfigLoader 所需的 Dictionary&lt;string, ModelProviderConfig&gt;
/// </summary>
public static class VendorModelMapper
{
    /// <summary>
    /// 从 SettingsJson.Vendor 构建 ModelProviderConfig 字典
    /// vendor[profile].models → ModelProviderConfig.Models
    /// vendor[profile].model → ModelProviderConfig.DefaultModelId
    /// </summary>
    public static Dictionary<string, ModelProviderConfig> BuildProviders(SettingsJson? settings)
    {
        var providers = new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase);

        if (settings?.Vendor is null)
            return providers;

        foreach (var (profileName, profile) in settings.Vendor)
        {
            var providerConfig = new ModelProviderConfig
            {
                DefaultModelId = profile.Model ?? string.Empty,
                DefaultFastModelId = string.Empty,
            };

            if (profile.Models is not null)
            {
                foreach (var model in profile.Models)
                {
                    if (!string.IsNullOrEmpty(model.Id))
                        providerConfig.Models.Add(model);
                }
            }

            if (string.IsNullOrEmpty(providerConfig.DefaultFastModelId) && providerConfig.Models.Count > 0)
            {
                var fastModel = providerConfig.Models.FirstOrDefault(m => m.Capabilities.FastMode);
                providerConfig.DefaultFastModelId = fastModel?.Id ?? providerConfig.Models[0].Id;
            }

            providers[profileName] = providerConfig;
        }

        return providers;
    }
}
