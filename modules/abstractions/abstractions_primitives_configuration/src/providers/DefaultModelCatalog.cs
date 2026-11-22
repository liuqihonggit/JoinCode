namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 默认模型 ID 统一数据源 — 模型版本号不硬编码在代码中，只从配置文件获取。
/// <para>当配置文件未指定 DefaultModelId 时，由 ModelConfigLoader.ApplyProviders 自动取 Models 列表第一个模型。</para>
/// <para>FallbackModel 为空串 — 消费方须确保从配置链路获取到有效模型名，不应依赖代码兜底。</para>
/// </summary>
public static class DefaultModelCatalog {
    /// <summary>
    /// 全局 fallback 默认模型 ID — 空串，表示无代码兜底，模型名必须来自配置文件
    /// </summary>
    public const string FallbackModel = "";

    /// <summary>
    /// 获取供应商的默认模型 ID — 始终返回空串，模型名必须从 ModelConfigLoader/ProviderDefinition 配置链路获取
    /// </summary>
    /// <param name="vendor">供应商类型</param>
    /// <returns>空串 — 此方法仅保留向后兼容签名，实际默认模型由配置文件决定</returns>
    public static string GetDefaultModel(VendorKind vendor)
        => FallbackModel;
}
