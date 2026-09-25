
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// 通过 ApplyProviders 接收数据，所有查询方法操作内存索引
/// </summary>
public sealed class ModelConfigLoader : IModelConfigLoader {
    private volatile ModelConfigRoot _config;
    private FrozenDictionary<string, ModelItemConfig> _modelById;
    private FrozenDictionary<string, ModelEntry[]> _modelsByProvider = FrozenDictionary<string, ModelEntry[]>.Empty;
    private FrozenDictionary<string, ModelItemConfig> _modelByProviderAndId = FrozenDictionary<string, ModelItemConfig>.Empty;
    private FrozenDictionary<string, string> _aliasByProviderAndInput = FrozenDictionary<string, string>.Empty;
    private FrozenDictionary<string, string> _providerByModelId = FrozenDictionary<string, string>.Empty;

    public ModelConfigLoader() {
        _config = new ModelConfigRoot();
        _modelById = FrozenDictionary<string, ModelItemConfig>.Empty;
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
        _modelsByProvider = BuildModelsByProvider(config);
        _modelByProviderAndId = BuildModelByProviderAndId(config);
        _aliasByProviderAndInput = BuildAliasByProviderAndInput(config);
        _providerByModelId = BuildProviderByModelId(config);
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

    /// <summary>构建 (provider, modelId) → ModelItemConfig 复合索引，供 FindModel O(1) 查找。</summary>
    private static FrozenDictionary<string, ModelItemConfig> BuildModelByProviderAndId(ModelConfigRoot config) {
        var dict = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            foreach (var model in provider.Value.Models) {
                dict[provider.Key + "\0" + model.Id] = model;
            }
        }
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>构建 (provider, alias) → modelId 复合索引，供 ResolveAlias O(1) 查找。</summary>
    private static FrozenDictionary<string, string> BuildAliasByProviderAndInput(ModelConfigRoot config) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            foreach (var model in provider.Value.Models) {
                foreach (var alias in model.Aliases) {
                    dict[provider.Key + "\0" + alias] = model.Id;
                }
            }
        }
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>构建 modelId → providerName 索引，供 FindProviderByModelId O(1) 查找。</summary>
    private static FrozenDictionary<string, string> BuildProviderByModelId(ModelConfigRoot config) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in config.Providers) {
            foreach (var model in provider.Value.Models) {
                dict[model.Id] = provider.Key;
            }
        }
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>解析别名到模型 ID。</summary>
    public string? ResolveAlias(string providerName, string input) {
        return _aliasByProviderAndInput.GetValueOrDefault(providerName + "\0" + input);
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
        var exact = _modelById.GetValueOrDefault(fullModelName);
        if (exact is not null)
            return !string.IsNullOrEmpty(exact.CanonicalId) ? exact.CanonicalId : exact.Id;

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
        return _modelByProviderAndId.GetValueOrDefault(providerName + "\0" + modelId);
    }

    /// <summary>获取所有模型 ID 集合。</summary>
    public IReadOnlyCollection<string> GetAllModelIds() {
        return _modelById.Keys;
    }

    /// <summary>按模型 ID 查找所属供应商名称。</summary>
    public string? FindProviderByModelId(string modelId) {
        return _providerByModelId.GetValueOrDefault(modelId);
    }

    /// <summary>按模型 ID 模糊查找模型配置。</summary>
    public ModelItemConfig? FindModelByModelId(string modelId) {
        var exact = _modelById.GetValueOrDefault(modelId);
        if (exact is not null) return exact;

        var lower = modelId.ToLowerInvariant();
        foreach (var model in _modelById.Values) {
            if (lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
                return model;
        }
        return null;
    }
}
