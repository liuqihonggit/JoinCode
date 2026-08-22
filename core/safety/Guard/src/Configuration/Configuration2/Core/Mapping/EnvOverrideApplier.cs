using JoinCode.Abstractions.Configuration.AppData;
using JoinCode.Abstractions.Configuration.Settings;

namespace Core.Configuration;

/// <summary>
/// 环境变量覆盖 SettingsJson — 集中启动参数的系统变量解析。
/// 流程:先读 settings.json → 再用 JCC_* 环境变量覆盖 SettingsJson 字段 → 最后映射到 WorkflowConfig。
/// 重构后: 环境变量覆盖写入 current 内部字段和 vendor 字典
/// </summary>
public static class EnvOverrideApplier
{
    /// <summary>
    /// 用 JCC_* 环境变量覆盖 SettingsJson 字段,返回新 SettingsJson(不可变,用 Merge 生成)。
    /// JCC_VENDOR → 设置 current.profile + 写入 vendor[vendor] 预设
    /// JCC_MODEL_ID → 覆盖 vendor[current.profile].model
    /// JCC_ENDPOINT → 覆盖 vendor[current.profile].endpoint
    /// JCC_PROFILE → 设置 current.profile
    /// </summary>
    public static SettingsJson Apply(SettingsJson? settings)
    {
        settings ??= new SettingsJson();

        var envVendor = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        var envModelId = Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue());
        var envEndpoint = Environment.GetEnvironmentVariable(JccEnvVar.Endpoint.ToValue());
        var envProfile = Environment.GetEnvironmentVariable(JccEnvVar.Profile.ToValue());

        // JCC_PROFILE 优先于 JCC_VENDOR 设 current.profile
        var effectiveProfile = !string.IsNullOrEmpty(envProfile) ? envProfile
            : !string.IsNullOrEmpty(envVendor) ? envVendor
            : null;

        if (string.IsNullOrEmpty(envVendor) && string.IsNullOrEmpty(envModelId)
            && string.IsNullOrEmpty(envEndpoint) && string.IsNullOrEmpty(effectiveProfile))
            return settings;

        // 确定 profile 名：环境变量 > 现有 current.profile
        var profileName = effectiveProfile ?? settings.Current?.Profile;

        // 构建 override 的 vendor 字典 — 环境变量覆盖写入对应 profile
        Dictionary<string, ProfileSettings>? overrideVendor = null;
        if (!string.IsNullOrEmpty(profileName))
        {
            var existingProfile = settings.Vendor is not null && settings.Vendor.TryGetValue(profileName, out var ep)
                ? ep : null;

            var inferredProtocol = InferProtocol(envVendor ?? existingProfile?.Provider);
            var inferredApiKeyEnvVar = InferApiKeyEnvVar(envVendor ?? existingProfile?.Provider);

            overrideVendor = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [profileName] = new ProfileSettings
                {
                    Provider = !string.IsNullOrEmpty(envVendor) ? envVendor : existingProfile?.Provider,
                    Protocol = existingProfile?.Protocol ?? inferredProtocol,
                    ApiKeyEnvVar = inferredApiKeyEnvVar ?? existingProfile?.ApiKeyEnvVar,
                    Model = !string.IsNullOrEmpty(envModelId) ? envModelId : existingProfile?.Model,
                    Endpoint = !string.IsNullOrEmpty(envEndpoint) ? envEndpoint : existingProfile?.Endpoint,
                    Models = existingProfile?.Models,
                },
            };
        }

        // 构建 override 的 current — 设置 profile
        var overrideCurrent = new CurrentSettings
        {
            Profile = effectiveProfile,
        };

        var overrideSettings = new SettingsJson
        {
            Vendor = overrideVendor,
            Current = overrideCurrent,
        };

        return SettingsJson.Merge(settings, overrideSettings);
    }

    /// <summary>
    /// 根据 vendor 名推断协议 — anthropic 用专属协议，azure 用 azure 协议，其余 openai-compatible
    /// </summary>
    private static string? InferProtocol(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return null;
        if (string.Equals(vendor, "anthropic", StringComparison.OrdinalIgnoreCase))
            return ProtocolKind.Anthropic.ToValue();
        if (string.Equals(vendor, "azure", StringComparison.OrdinalIgnoreCase))
            return ProtocolKind.Azure.ToValue();
        return ProtocolKind.OpenAiCompatible.ToValue();
    }

    /// <summary>
    /// 根据 vendor 名推断 API Key 环境变量名 — 让 ProviderDefinitionRegistry 能匹配环境变量
    /// </summary>
    private static string? InferApiKeyEnvVar(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return null;
        if (string.Equals(vendor, "openai", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.OpenAiApiKey.ToValue();
        if (string.Equals(vendor, "anthropic", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AnthropicApiKey.ToValue();
        if (string.Equals(vendor, "azure", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AzureOpenAiApiKey.ToValue();
        if (string.Equals(vendor, "deepseek", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.DeepSeekApiKey.ToValue();
        if (string.Equals(vendor, "agnes", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AgnesApiKey.ToValue();
        if (string.Equals(vendor, "sensenova", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.SenseNovaApiKey.ToValue();
        return null;
    }
}
