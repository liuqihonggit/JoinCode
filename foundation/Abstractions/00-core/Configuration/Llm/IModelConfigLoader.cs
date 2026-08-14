
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// </summary>
public interface IModelConfigLoader
{
    ModelConfigRoot Config { get; }
    void ApplyProviders(Dictionary<string, ModelProviderConfig> providers);
    ModelProviderConfig? GetProviderConfig(string providerName);
    string GetDefaultModelId(string providerName);
    string GetDefaultFastModelId(string providerName);
    ModelEntry[] GetModels(string providerName);
    string? ResolveAlias(string providerName, string input);
    bool SupportsFastMode(string providerName, string modelId);
    bool SupportsEffort(string providerName, string modelId);
    bool SupportsMaxEffort(string providerName, string modelId);
    bool SupportsThinkingMode(string providerName, string modelId);
    string GetCanonicalName(string fullModelName);
    ModelItemConfig? FindModel(string providerName, string modelId);
    IReadOnlyCollection<string> GetAllModelIds();
    string? FindProviderByModelId(string modelId);
    ModelItemConfig? FindModelByModelId(string modelId);
}
