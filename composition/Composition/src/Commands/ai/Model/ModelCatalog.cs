
namespace JoinCode.ChatCommands;

[Register]
public sealed partial class ModelCatalog(IProviderDefinitionRegistry registry, IModelConfigLoader? modelConfigLoader = null) : ServiceEntity, IModelCatalog
{
    private readonly IProviderDefinitionRegistry _registry = registry;
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;

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

    public string? ResolveAlias(string input, string provider)
    {
        return _registry.TryGet(provider)?.ResolveAlias(input);
    }

    public string GetProviderDisplayName(string provider)
    {
        return _registry.TryGet(provider)?.DisplayName ?? provider;
    }

    public string GetDefaultModelForProvider(string provider)
    {
        return _registry.TryGet(provider)?.DefaultModelId ?? _modelConfigLoader?.GetDefaultModelId("openai") ?? "gpt-4o";
    }

    public string GetDefaultFastModelForProvider(string provider)
    {
        return _registry.TryGet(provider)?.DefaultFastModelId ?? _modelConfigLoader?.GetDefaultFastModelId("openai") ?? "gpt-4o-mini";
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
        return _registry.TryGet(provider)?.SupportsFastMode(modelId) ?? false;
    }

    public bool SupportsEffort(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.SupportsEffort(modelId) ?? false;
    }

    public bool SupportsMaxEffort(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.SupportsMaxEffort(modelId) ?? false;
    }

    public bool SupportsModality(string modelId, string provider, ModelModalityKind modality)
    {
        return _registry.TryGet(provider)?.SupportsModality(modelId, modality) ?? false;
    }

    public ModelModalityKind GetModalities(string modelId, string provider)
    {
        return _registry.TryGet(provider)?.GetModalities(modelId) ?? ModelModalityKind.Text;
    }
}
