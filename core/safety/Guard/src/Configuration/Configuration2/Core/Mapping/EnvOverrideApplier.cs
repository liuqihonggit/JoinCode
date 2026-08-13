using JoinCode.Abstractions.Configuration.AppData;
using JoinCode.Abstractions.Configuration.Settings;

namespace Core.Configuration;

/// <summary>
/// 环境变量覆盖 SettingsJson — 集中启动参数的系统变量解析。
/// 流程:先读 settings.json → 再用 JCC_* 环境变量覆盖 SettingsJson 字段 → 最后映射到 WorkflowConfig。
/// 这样参数变量就是应用的(环境变量优先级高于 JSON)。
/// 仅处理 SettingsJson 有对应字段的配置键;WorkflowConfig 特有字段(Protocol/OrganizationId/ApiVersion/EnableOAuth/CodeExecution/StateFilePath)
/// 仍由 SettingsMapper.ApplyEnvOverrides 在 WorkflowConfig 层覆盖。
/// </summary>
public static class EnvOverrideApplier
{
    /// <summary>
    /// 用 JCC_* 环境变量覆盖 SettingsJson 字段,返回新 SettingsJson(不可变,用 Merge 生成)。
    /// 覆盖键:Provider(JCC_VENDOR)、Model(JCC_MODEL_ID)、Endpoint(JCC_ENDPOINT)、CurrentProfile(JCC_PROFILE)。
    /// JCC_VENDOR 同时设 CurrentProfile=vendor(切换供应商预设)。
    /// </summary>
    public static SettingsJson Apply(SettingsJson? settings)
    {
        settings ??= new SettingsJson();

        var envVendor = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        var envModelId = Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue());
        var envEndpoint = Environment.GetEnvironmentVariable(JccEnvVar.Endpoint.ToValue());
        var envProfile = Environment.GetEnvironmentVariable(JccEnvVar.Profile.ToValue());

        // JCC_PROFILE 优先于 JCC_VENDOR 设 CurrentProfile（显式 profile > vendor 同名 profile）
        var effectiveProfile = !string.IsNullOrEmpty(envProfile) ? envProfile
            : !string.IsNullOrEmpty(envVendor) ? envVendor
            : null;

        if (string.IsNullOrEmpty(envVendor) && string.IsNullOrEmpty(envModelId)
            && string.IsNullOrEmpty(envEndpoint) && string.IsNullOrEmpty(effectiveProfile))
            return settings;

        var overrideSettings = new SettingsJson
        {
            Provider = !string.IsNullOrEmpty(envVendor) ? envVendor : null,
            Model = !string.IsNullOrEmpty(envModelId) ? envModelId : null,
            Endpoint = !string.IsNullOrEmpty(envEndpoint) ? envEndpoint : null,
            CurrentProfile = effectiveProfile,
        };

        return SettingsJson.Merge(settings, overrideSettings);
    }
}
