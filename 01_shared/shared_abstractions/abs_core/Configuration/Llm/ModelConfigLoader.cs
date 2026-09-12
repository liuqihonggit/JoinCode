
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// 通过 ApplyProviders 接收数据，所有查询方法操作内存索引
/// </summary>
public sealed class ModelConfigLoader : IModelConfigLoader
{
    private volatile ModelConfigRoot _config;
    private FrozenDictionary<string, ModelItemConfig> _modelById;
    private FrozenDictionary<string, string> _aliasToModelId;

    public ModelConfigLoader()
    {
        _config = new ModelConfigRoot();
        _modelById = FrozenDictionary<string, ModelItemConfig>.Empty;
        _aliasToModelId = FrozenDictionary<string, string>.Empty;
    }

    public ModelConfigRoot Config => _config;

    /// <summary>
    /// 从 SettingsJson.Vendor 构建的 providers 数据灌入 — 唯一的数据入口
    /// 由 Core 层在加载 settings.json 后调用，热重载时再次调用
    /// </summary>
    public void ApplyProviders(Dictionary<string, ModelProviderConfig> providers)
    {
        var config = new ModelConfigRoot { Providers = providers };
        _config = config;
        _modelById = BuildModelById(config);
        _aliasToModelId = BuildAliasToModelId(config);
    }

    private static FrozenDictionary<string, ModelItemConfig> BuildModelById(ModelConfigRoot config)
    {
        var idDict = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                idDict[model.Id] = model;
            }
        }
        return idDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string> BuildAliasToModelId(ModelConfigRoot config)
    {
        var aliasDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                foreach (var alias in model.Aliases)
                {
                    aliasDict[alias] = model.Id;
                }
            }
        }
        return aliasDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public ModelProviderConfig? GetProviderConfig(string providerName)
    {
        return Config.Providers.GetValueOrDefault(providerName);
    }

    public string GetDefaultModelId(string providerName)
    {
        return GetProviderConfig(providerName)?.DefaultModelId ?? string.Empty;
    }

    public string GetDefaultFastModelId(string providerName)
    {
        return GetProviderConfig(providerName)?.DefaultFastModelId ?? string.Empty;
    }

    public ModelEntry[] GetModels(string providerName)
    {
        var providerConfig = GetProviderConfig(providerName);
        if (providerConfig is null)
            return [];

        var entries = new ModelEntry[providerConfig.Models.Count];
        for (int i = 0; i < providerConfig.Models.Count; i++)
        {
            var m = providerConfig.Models[i];
            entries[i] = new ModelEntry(m.Id, m.DisplayName, m.ContextWindow, m.Description);
        }
        return entries;
    }

    public string? ResolveAlias(string providerName, string input)
    {
        var providerConfig = GetProviderConfig(providerName);
        if (providerConfig is null)
            return null;

        var lower = input.ToLowerInvariant();
        foreach (var model in providerConfig.Models)
        {
            foreach (var alias in model.Aliases)
            {
                if (string.Equals(alias, lower, StringComparison.OrdinalIgnoreCase))
                    return model.Id;
            }
        }
        return null;
    }

    public bool SupportsFastMode(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.FastMode ?? true;
    }

    public bool SupportsEffort(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.Effort ?? false;
    }

    public bool SupportsMaxEffort(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.MaxEffort ?? false;
    }

    public bool SupportsThinkingMode(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.ThinkingMode ?? false;
    }

    public bool SupportsModality(string providerName, string modelId, ModelModalityKind modality)
    {
        var modalities = GetModalities(providerName, modelId);
        return modalities.HasFlag(modality);
    }

    public ModelModalityKind GetModalities(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.Modalities ?? ModelModalityKind.Text;
    }

    public string GetCanonicalName(string fullModelName)
    {
        var name = fullModelName.ToLowerInvariant();

        foreach (var model in _modelById.Values)
        {
            if (name.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return !string.IsNullOrEmpty(model.CanonicalId) ? model.CanonicalId : model.Id;
            }
        }

        return fullModelName;
    }

    public ModelItemConfig? FindModel(string providerName, string modelId)
    {
        var providerConfig = GetProviderConfig(providerName);
        if (providerConfig is null)
            return null;

        foreach (var model in providerConfig.Models)
        {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                return model;
        }

        return null;
    }

    public IReadOnlyCollection<string> GetAllModelIds()
    {
        return _modelById.Keys;
    }

    public string? FindProviderByModelId(string modelId)
    {
        foreach (var provider in Config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                    return provider.Key;
            }
        }
        return null;
    }

    public ModelItemConfig? FindModelByModelId(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        foreach (var provider in Config.Providers)
        {
            foreach (var model in provider.Value.Models)
            {
                if (lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
                    return model;
            }
        }
        return null;
    }
}
