
namespace JoinCode.ChatCommands;

/// <summary>
/// 模型目录 — 提供按供应商查询模型列表、解析别名、获取默认模型及能力判定等功能
/// </summary>
[Register(typeof(IModelCatalog), ServiceLifetime.Singleton)]
public sealed partial class ModelCatalog(IProviderDefinitionRegistry registry, IModelConfigLoader? modelConfigLoader = null) : ServiceEntity, IModelCatalog
{
    private readonly IProviderDefinitionRegistry _registry = registry;
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;

    /// <summary>
    /// 获取指定供应商的可用模型列表,支持通过环境变量注入自定义模型
    /// </summary>
    /// <param name="provider">供应商标识</param>
    /// <returns>该供应商的模型数组;若供应商不存在返回空数组</returns>
    public ModelEntry[] GetModelsForProvider(string provider)
    {
        var definition = _registry.TryGet(provider);
        if (definition is not null)
        {
            var baseModels = definition.AvailableModels;

            var customModelId = Environment.GetEnvironmentVariable(JccEnvVar.CustomModelOption.ToValue());
            if (string.IsNullOrWhiteSpace(customModelId))
                return baseModels.ToArray();

            var existing = Array.FindIndex(baseModels.ToArray(), m =>
                m.Id.Equals(customModelId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                return baseModels.ToArray();

            var customName = Environment.GetEnvironmentVariable(JccEnvVar.CustomModelOptionName.ToValue());
            var customDesc = Environment.GetEnvironmentVariable(JccEnvVar.CustomModelOptionDescription.ToValue());

            var baseList = baseModels.ToList();
            var result = new ModelEntry[baseList.Count + 1];
            for (int i = 0; i < baseList.Count; i++)
                result[i] = baseList[i];
            result[baseList.Count] = new ModelEntry(
                customModelId,
                string.IsNullOrWhiteSpace(customName) ? customModelId : customName,
                128_000,
                string.IsNullOrWhiteSpace(customDesc) ? "自定义模型" : customDesc);

            return result;
        }

        return [];
    }

    /// <summary>
    /// 解析模型别名,将用户输入的别名转换为对应供应商下的标准模型 ID
    /// </summary>
    /// <param name="input">用户输入的模型别名</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>解析后的标准模型 ID;若未匹配别名则返回 null</returns>
    public string? ResolveAlias(string input, string provider)
    {
        return _registry.TryGet(provider)?.ResolveAlias(input);
    }

    /// <summary>
    /// 获取供应商的显示名称,若供应商未注册则回退到原始标识
    /// </summary>
    /// <param name="provider">供应商标识</param>
    /// <returns>供应商显示名称</returns>
    public string GetProviderDisplayName(string provider)
    {
        return _registry.TryGet(provider)?.DisplayName ?? provider;
    }

    /// <summary>
    /// 获取指定供应商的默认模型 ID,依次从供应商定义、模型配置加载器、硬编码常量回退
    /// </summary>
    /// <param name="provider">供应商标识</param>
    /// <returns>默认模型 ID</returns>
    public string GetDefaultModelForProvider(string provider)
    {
        return _registry.TryGet(provider)?.DefaultModelId ?? _modelConfigLoader?.GetDefaultModelId(VendorKindConstants.OpenAi) ?? "gpt-4o";
    }

    /// <summary>
    /// 获取指定供应商的默认快速模型 ID,依次从供应商定义、模型配置加载器、硬编码常量回退
    /// </summary>
    /// <param name="provider">供应商标识</param>
    /// <returns>默认快速模型 ID</returns>
    public string GetDefaultFastModelForProvider(string provider)
    {
        return _registry.TryGet(provider)?.DefaultFastModelId ?? _modelConfigLoader?.GetDefaultFastModelId(VendorKindConstants.OpenAi) ?? "gpt-4o-mini";
    }

    /// <summary>
    /// 确保当前模型出现在模型列表中,若不存在则追加一项占位条目
    /// </summary>
    /// <param name="models">原始模型列表</param>
    /// <param name="currentModelId">当前模型 ID</param>
    /// <returns>包含当前模型的列表;若当前模型 ID 为空或 unknown 则原样返回</returns>
    public ModelEntry[] EnsureCurrentModelInList(ModelEntry[] models, string currentModelId)
    {
        if (string.IsNullOrWhiteSpace(currentModelId) || currentModelId == "unknown")
            return models;

        var existing = Array.FindIndex(models, m =>
            m.Id.Equals(currentModelId, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            return models;

        var result = new ModelEntry[models.Length + 1];
        Array.Copy(models, result, models.Length);
        result[models.Length] = new ModelEntry(currentModelId, currentModelId, 128_000, "当前模型");

        return result;
    }

    /// <summary>
    /// 判断指定模型是否支持快速模式
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>支持快速模式返回 true;否则返回 false</returns>
    public bool SupportsFastMode(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.SupportsFastMode(modelId) ?? false;
    }

    /// <summary>
    /// 判断指定模型是否支持 effort 参数
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>支持 effort 参数返回 true;否则返回 false</returns>
    public bool SupportsEffort(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.SupportsEffort(modelId) ?? false;
    }

    /// <summary>
    /// 判断指定模型是否支持 max effort 等级
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>支持 max effort 返回 true;否则返回 false</returns>
    public bool SupportsMaxEffort(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.SupportsMaxEffort(modelId) ?? false;
    }

    /// <summary>
    /// 判断指定模型是否支持给定的模态(文本、图像、音频等)
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <param name="modality">待检查的模态</param>
    /// <returns>支持该模态返回 true;否则返回 false</returns>
    public bool SupportsModality(string modelId, string provider, ModelModalityKind modality)
    {
        return _registry.TryGet(provider)?.SupportsModality(modelId, modality) ?? false;
    }

    /// <summary>
    /// 获取指定模型支持的模态集合
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="provider">供应商标识</param>
    /// <returns>模型支持的模态;若供应商未注册则回退为 Text</returns>
    public ModelModalityKind GetModalities(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.GetModalities(modelId) ?? ModelModalityKind.Text;
    }
}
