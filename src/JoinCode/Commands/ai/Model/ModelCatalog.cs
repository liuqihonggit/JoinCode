namespace JoinCode.ChatCommands;

/// <summary>
/// 模型目录 — 委托给 IProviderDefinition 多态实现，消除 switch
/// </summary>
[Register]
public sealed class ModelCatalog : IModelCatalog
{
    public ModelEntry[] GetModelsForProvider(string provider)
    {
        var definition = ProviderDefinitionRegistry.TryGet(provider);
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

            var result = new ModelEntry[baseModels.Count + 1];
            for (int i = 0; i < baseModels.Count; i++)
                result[i] = baseModels[i];
            result[baseModels.Count] = new ModelEntry(
                customModelId,
                string.IsNullOrWhiteSpace(customName) ? customModelId : customName,
                128_000,
                string.IsNullOrWhiteSpace(customDesc) ? "自定义模型" : customDesc);

            return result;
        }

        // 未知 Provider：返回空列表
        return [];
    }

    public string? ResolveAlias(string input, string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.ResolveAlias(input);
    }

    public string GetProviderDisplayName(string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.DisplayName ?? provider;
    }

    public string GetDefaultModelForProvider(string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.DefaultModelId ?? CanonicalModel.Gpt4o.ToValue();
    }

    public string GetDefaultFastModelForProvider(string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.DefaultFastModelId ?? CanonicalModel.Gpt4oMini.ToValue();
    }

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

    public bool SupportsFastMode(string modelId, string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.SupportsFastMode(modelId) ?? false;
    }

    public bool SupportsEffort(string modelId, string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.SupportsEffort(modelId) ?? false;
    }

    public bool SupportsMaxEffort(string modelId, string provider)
    {
        return ProviderDefinitionRegistry.TryGet(provider)?.SupportsMaxEffort(modelId) ?? false;
    }

}
