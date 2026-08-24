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
    /// vendor → 协议推断表 — 仅 anthropic/azure 有专属协议，其余默认 OpenAiCompatible
    /// 查表替代 if-else 链: O(1) 查找, AOT 零分配, key 用编译期常量 VendorKindConstants
    /// 用 FrozenDictionary.Create 显式传 OrdinalIgnoreCase 比较器(ToFrozenDictionary 无参版会丢失比较器)
    /// </summary>
    private static readonly FrozenDictionary<string, ProtocolKind> ProtocolByVendor =
        FrozenDictionary.Create(
            StringComparer.OrdinalIgnoreCase,
            new KeyValuePair<string, ProtocolKind>[]
            {
                new(VendorKindConstants.Anthropic, ProtocolKind.Anthropic),
                new(VendorKindConstants.Azure, ProtocolKind.Azure),
            });

    /// <summary>
    /// vendor → API Key 环境变量名推断表 — 6 个供应商有映射, bedrock/未知返回 null
    /// 查表替代 if-else 链: O(1) 查找, AOT 零分配, key/value 用编译期常量
    /// 用 FrozenDictionary.Create 显式传 OrdinalIgnoreCase 比较器
    /// </summary>
    private static readonly FrozenDictionary<string, string> ApiKeyEnvVarByVendor =
        FrozenDictionary.Create(
            StringComparer.OrdinalIgnoreCase,
            new KeyValuePair<string, string>[]
            {
                new(VendorKindConstants.OpenAi, ProviderEnvVarConstants.OpenAiApiKey),
                new(VendorKindConstants.Anthropic, ProviderEnvVarConstants.AnthropicApiKey),
                new(VendorKindConstants.Azure, ProviderEnvVarConstants.AzureOpenAiApiKey),
                new(VendorKindConstants.DeepSeek, ProviderEnvVarConstants.DeepSeekApiKey),
                new(VendorKindConstants.Agnes, ProviderEnvVarConstants.AgnesApiKey),
                new(VendorKindConstants.Sensenova, ProviderEnvVarConstants.SenseNovaApiKey),
            });

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
    /// 根据 vendor 名推断协议 — 查表: anthropic/azure 有专属协议，其余 openai-compatible
    /// </summary>
    internal static string? InferProtocol(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return null;
        return (ProtocolByVendor.TryGetValue(vendor, out var protocol)
            ? protocol
            : ProtocolKind.OpenAiCompatible).ToValue();
    }

    /// <summary>
    /// 根据 vendor 名推断 API Key 环境变量名 — 查表: 6 个供应商有映射, 其余返回 null
    /// </summary>
    internal static string? InferApiKeyEnvVar(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return null;
        return ApiKeyEnvVarByVendor.TryGetValue(vendor, out var envVar) ? envVar : null;
    }
}
