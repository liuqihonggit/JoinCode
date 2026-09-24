
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// 通过 ApplyProviders 接收数据，所有查询方法操作内存索引
/// </summary>
public sealed class ModelConfigLoader : IModelConfigLoader {
    private volatile ModelConfigRoot _config;
    private FrozenDictionary<string, ModelItemConfig> _modelById;
    private FrozenDictionary<string, string> _aliasToModelId;
    private FrozenDictionary<string, ModelEntry[]> _modelsByProvider = FrozenDictionary<string, ModelEntry[]>.Empty;

    public ModelConfigLoader() {
        _config = new ModelConfigRoot();
        _modelById = FrozenDictionary<string, ModelItemConfig>.Empty;
        _aliasToModelId = FrozenDictionary<string, string>.Empty;
    }

    /// <summary>获取当前模型配置根节点。</summary>
    public ModelConfigRoot Config => _config;

    /// <summary>
    /// 从 SettingsJson.Vendor 构建的 providers 数据灌入 — 唯一的数据入口
    /// 由 Core 层在加载 settings.json 后调用，热重载时再次调用
    /// </summary>
    public void ApplyProviders(Dictionary<string, ModelProviderConfig> providers) {
        var config = new ModelConfigRoot { Providers = providers };
        _config = config;
        _modelById = BuildModelById(config);
        _aliasToModelId = BuildAliasToModelId(config);
        _modelsByProvider = BuildModelsByProvider(config);
    }

    private static FrozenDictionary<string, ModelItemConfig> BuildModelById(ModelConfigRoot config) {
        var idDict = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            foreach (var model in provider.Value.Models) {
                idDict[model.Id] = model;
            }
        }
        return idDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string> BuildAliasToModelId(ModelConfigRoot config) {
        var aliasDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            foreach (var model in provider.Value.Models) {
                foreach (var alias in model.Aliases) {
                    aliasDict[alias] = model.Id;
                }
            }
        }
        return aliasDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>按供应商名称获取供应商配置。</summary>
    public ModelProviderConfig? GetProviderConfig(string providerName) {
        return Config.Providers.GetValueOrDefault(providerName);
    }

    /// <summary>获取指定供应商的默认模型 ID。</summary>
    public string GetDefaultModelId(string providerName) {
        return GetProviderConfig(providerName)?.DefaultModelId ?? string.Empty;
    }

    /// <summary>获取指定供应商的默认快速模型 ID。</summary>
    public string GetDefaultFastModelId(string providerName) {
        return GetProviderConfig(providerName)?.DefaultFastModelId ?? string.Empty;
    }

    /// <summary>获取指定供应商的所有模型条目。</summary>
    public ModelEntry[] GetModels(string providerName) {
        return _modelsByProvider.GetValueOrDefault(providerName) ?? [];
    }

    private static FrozenDictionary<string, ModelEntry[]> BuildModelsByProvider(ModelConfigRoot config) {
        var dict = new Dictionary<string, ModelEntry[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            var models = provider.Value.Models;
            var entries = new ModelEntry[models.Count];
            for (var i = 0; i < models.Count; i++) {
                var m = models[i];
                entries[i] = new ModelEntry(m.Id, m.DisplayName, m.ContextWindow, m.Description);
            }
            dict[provider.Key] = entries;
        }
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>解析别名到模型 ID。</summary>
    public string? ResolveAlias(string providerName, string input) {
        var providerConfig = GetProviderConfig(providerName);
        if (providerConfig is null)
            return null;

        var lower = input.ToLowerInvariant();
        foreach (var model in providerConfig.Models) {
            foreach (var alias in model.Aliases) {
                if (string.Equals(alias, lower, StringComparison.OrdinalIgnoreCase))
                    return model.Id;
            }
        }
        return null;
    }

    /// <summary>判断指定模型是否支持快速模式。</summary>
    public bool SupportsFastMode(string providerName, string modelId) {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.FastMode ?? true;
    }

    /// <summary>判断指定模型是否支持努力级别。</summary>
    public bool SupportsEffort(string providerName, string modelId) {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.Effort ?? false;
    }

    /// <summary>判断指定模型是否支持最大努力级别。</summary>
    public bool SupportsMaxEffort(string providerName, string modelId) {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.MaxEffort ?? false;
    }

    /// <summary>判断指定模型是否支持思考模式。</summary>
    public bool SupportsThinkingMode(string providerName, string modelId) {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.ThinkingMode ?? false;
    }

    /// <summary>判断指定模型是否支持给定模态。</summary>
    public bool SupportsModality(string providerName, string modelId, ModelModalityKind modality) {
        var modalities = GetModalities(providerName, modelId);
        return modalities.HasFlag(modality);
    }

    /// <summary>获取指定模型的模态能力标志。</summary>
    public ModelModalityKind GetModalities(string providerName, string modelId) {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.Modalities ?? ModelModalityKind.Text;
    }

    /// <summary>根据完整模型名获取规范名称。</summary>
    public string GetCanonicalName(string fullModelName) {
        var name = fullModelName.ToLowerInvariant();

        foreach (var model in _modelById.Values) {
            if (name.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal)) {
                return !string.IsNullOrEmpty(model.CanonicalId) ? model.CanonicalId : model.Id;
            }
        }

        return fullModelName;
    }

    /// <summary>按供应商名称和模型 ID 查找模型配置。</summary>
    public ModelItemConfig? FindModel(string providerName, string modelId) {
        var providerConfig = GetProviderConfig(providerName);
        if (providerConfig is null)
            return null;

        foreach (var model in providerConfig.Models) {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                return model;
        }

        return null;
    }

    /// <summary>获取所有模型 ID 集合。</summary>
    public IReadOnlyCollection<string> GetAllModelIds() {
        return _modelById.Keys;
    }

    /// <summary>按模型 ID 查找所属供应商名称。</summary>
    public string? FindProviderByModelId(string modelId) {
        foreach (var provider in Config.Providers) {
            foreach (var model in provider.Value.Models) {
                if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                    return provider.Key;
            }
        }
        return null;
    }

    /// <summary>按模型 ID 模糊查找模型配置。</summary>
    public ModelItemConfig? FindModelByModelId(string modelId) {
        var lower = modelId.ToLowerInvariant();
        foreach (var provider in Config.Providers) {
            foreach (var model in provider.Value.Models) {
                if (lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
                    return model;
            }
        }
        return null;
    }
}
