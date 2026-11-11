
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// </summary>
public interface IModelConfigLoader {
    /// <summary>获取模型配置根。</summary>
    ModelConfigRoot Config { get; }
    /// <summary>应用供应商配置字典。</summary>
    /// <param name="providers">供应商配置字典。</param>
    void ApplyProviders(Dictionary<string, ModelProviderConfig> providers);
    /// <summary>获取指定供应商配置。</summary>
    /// <param name="providerName">供应商名称。</param>
    ModelProviderConfig? GetProviderConfig(string providerName);
    /// <summary>获取默认模型标识。</summary>
    /// <param name="providerName">供应商名称。</param>
    string GetDefaultModelId(string providerName);
    /// <summary>获取默认快速模型标识。</summary>
    /// <param name="providerName">供应商名称。</param>
    string GetDefaultFastModelId(string providerName);
    /// <summary>获取指定供应商的模型列表。</summary>
    /// <param name="providerName">供应商名称。</param>
    ModelEntry[] GetModels(string providerName);
    /// <summary>解析模型别名。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="input">输入别名。</param>
    string? ResolveAlias(string providerName, string input);
    /// <summary>判断是否支持快速模式。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    bool SupportsFastMode(string providerName, string modelId);
    /// <summary>判断是否支持努力级别。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    bool SupportsEffort(string providerName, string modelId);
    /// <summary>判断是否支持最大努力级别。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    bool SupportsMaxEffort(string providerName, string modelId);
    /// <summary>判断是否支持思考模式。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    bool SupportsThinkingMode(string providerName, string modelId);
    /// <summary>判断是否支持指定模态。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    /// <param name="modality">模态类型。</param>
    bool SupportsModality(string providerName, string modelId, ModelModalityKind modality);
    /// <summary>获取模型支持的模态。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    ModelModalityKind GetModalities(string providerName, string modelId);
    /// <summary>获取规范名称。</summary>
    /// <param name="fullModelName">完整模型名称。</param>
    string GetCanonicalName(string fullModelName);
    /// <summary>查找模型配置。</summary>
    /// <param name="providerName">供应商名称。</param>
    /// <param name="modelId">模型标识。</param>
    ModelItemConfig? FindModel(string providerName, string modelId);
    /// <summary>获取所有模型标识集合。</summary>
    IReadOnlyCollection<string> GetAllModelIds();
    /// <summary>按模型标识查找供应商名称。</summary>
    /// <param name="modelId">模型标识。</param>
    string? FindProviderByModelId(string modelId);
    /// <summary>按模型标识查找模型配置。</summary>
    /// <param name="modelId">模型标识。</param>
    ModelItemConfig? FindModelByModelId(string modelId);
}
