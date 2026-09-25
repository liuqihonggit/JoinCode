namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 默认模型 ID 统一数据源 — P1-⑥ 单数据源改造
/// <para>每个供应商的默认模型 ID 在此定义一次,消费方委托获取</para>
/// <para>替代 SettingsLoader/PipeQueryService/StartupWorkflow 中分散的硬编码默认模型名</para>
/// </summary>
public static class DefaultModelCatalog {
    private static readonly FrozenDictionary<VendorKind, string> DefaultModels = new Dictionary<VendorKind, string> {
        [VendorKind.OpenAi] = "gpt-5.6-sol",
        [VendorKind.Anthropic] = "claude-opus-5",
        [VendorKind.DeepSeek] = "deepseek-v4-flash",
        [VendorKind.Azure] = "gpt-5.6-sol",
        [VendorKind.Agnes] = "agnes-2.0-flash",
        [VendorKind.Sensenova] = "sensenova-6.8-flash-lite",
        [VendorKind.Bedrock] = "claude-opus-5",
        [VendorKind.Zhipu] = "glm-5.3",
        [VendorKind.Jev] = "gpt-5.6-sol",
    }.ToFrozenDictionary();

    /// <summary>
    /// 全局 fallback 默认模型 ID — 当供应商未知或未注册时使用
    /// </summary>
    public const string FallbackModel = "gpt-5.6-sol";

    /// <summary>
    /// 获取供应商的默认模型 ID
    /// </summary>
    /// <param name="vendor">供应商类型</param>
    /// <returns>该供应商的默认模型 ID,未知供应商返回 FallbackModel</returns>
    public static string GetDefaultModel(VendorKind vendor)
        => DefaultModels.GetValueOrDefault(vendor, FallbackModel);
}
