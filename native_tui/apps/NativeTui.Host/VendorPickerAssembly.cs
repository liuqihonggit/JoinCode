namespace NativeTui.Host;

/// <summary>
/// 供应商选择器装配（TASK058）— 清单从 VendorKind 枚举 + IModelCatalog 投影
/// （DisplayName 用目录显示名，Description 显默认模型，IsCurrent 标记 WorkflowConfig 当前供应商）；
/// 切换链对齐 GUI JccChatSession.SetVendorAsync 语义：WorkflowConfig.Provider 原地改
/// （Vendor/ModelId 可变属性，DI 单例引用不变全消费方可见）+ IFastModeService 主模型跟随 +
/// IConfigurationService 持久化 profile（键语义对齐 VendorCommand/SettingsMapper）+ fast mode 自关。
/// </summary>
internal static class VendorPickerAssembly
{
    /// <summary>从引擎服务投影供应商清单 — 枚举为唯一数据源（VendorKind，规则7），目录缺失时显示名退化为枚举值。</summary>
    internal static IReadOnlyList<ModelPickerItem> LoadItems(IServiceProvider services)
    {
        var current = CurrentVendor(services);
        var catalog = services.GetService<IModelCatalog>();
        return Enum.GetValues<VendorKind>()
            .Select(kind => kind.ToValue())
            .Select(vendor => {
                var displayName = catalog?.GetProviderDisplayName(vendor) ?? vendor;
                var defaultModel = catalog?.GetDefaultModelForProvider(vendor) ?? string.Empty;
                var description = defaultModel.Length > 0 ? $"默认模型: {defaultModel}" : string.Empty;
                return new ModelPickerItem(vendor, displayName, description, vendor.Equals(current, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
    }

    /// <summary>切换链 — config.Provider 原地改 + 主模型跟随 + profile 持久化 + fast mode 自关（新默认模型不支持时）。</summary>
    internal static void ApplyVendor(IServiceProvider services, string vendor)
    {
        var config = services.GetService<WorkflowConfig>();
        var catalog = services.GetService<IModelCatalog>();
        var defaultModelId = catalog?.GetDefaultModelForProvider(vendor) ?? string.Empty;

        if (config?.Provider is { } provider) {
            provider.Vendor = vendor;
            if (defaultModelId.Length > 0) {
                provider.ModelId = defaultModelId;
            }
        }

        var fastMode = services.GetService<IFastModeService>();
        if (defaultModelId.Length > 0) {
            fastMode?.SetPrimaryModel(defaultModelId);
        }

        services.GetService<IConfigurationService>()?.SetAsync("profile", vendor).GetAwaiter().GetResult();

        if (fastMode is { IsFastModeActive: true } && defaultModelId.Length > 0) {
            if (catalog is not null && !catalog.SupportsFastMode(defaultModelId, vendor)) {
                fastMode.Deactivate();
            }
        }
    }

    /// <summary>当前供应商 — WorkflowConfig DI 单例优先（vendor 切换后实时），环境变量兜底（对齐 ModelPickerAssembly.CurrentProvider 取值顺序）。</summary>
    internal static string CurrentVendor(IServiceProvider services) =>
        services.GetService<WorkflowConfig>()?.Provider?.Vendor
        ?? Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue())
        ?? VendorKind.OpenAi.ToValue();
}
