
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型配置查询服务 — 数据从 SettingsJson.Vendor 流入，不碰文件
/// 通过 ApplyProviders 接收数据，所有查询方法操作内存索引
/// </summary>
public sealed class ModelConfigLoader : IModelConfigLoader {
    /// <summary>
    /// 不可变模型配置索引 — 从 ModelConfigRoot 一次性构建的全部检索结构。
    /// 作为 ModelConfigLoader 的唯一数据源，通过 CAS 原子交换发布，查询端零锁读取，无死锁。
    /// </summary>
    private sealed class ModelConfigIndex {
        public readonly ModelConfigRoot Config;
        public readonly FrozenDictionary<string, ModelItemConfig> ModelById;
        public readonly FrozenDictionary<string, ModelEntry[]> ModelsByProvider;
        public readonly FrozenDictionary<string, FrozenDictionary<string, ModelItemConfig>> ModelByProviderAndId;
        public readonly FrozenDictionary<string, FrozenDictionary<string, string>> AliasByProviderAndInput;
        public readonly FrozenDictionary<string, string> ProviderByModelId;

        /// <summary>构建不可变配置索引 — 所有检索结构在同一调用中构建，保证相互一致。</summary>
        public ModelConfigIndex(
            ModelConfigRoot config,
            FrozenDictionary<string, ModelItemConfig> modelById,
            FrozenDictionary<string, ModelEntry[]> modelsByProvider,
            FrozenDictionary<string, FrozenDictionary<string, ModelItemConfig>> modelByProviderAndId,
            FrozenDictionary<string, FrozenDictionary<string, string>> aliasByProviderAndInput,
            FrozenDictionary<string, string> providerByModelId) {
            Config = config;
            ModelById = modelById;
            ModelsByProvider = modelsByProvider;
            ModelByProviderAndId = modelByProviderAndId;
            AliasByProviderAndInput = aliasByProviderAndInput;
            ProviderByModelId = providerByModelId;
        }

        public static readonly ModelConfigIndex Empty = new(
            new ModelConfigRoot(),
            FrozenDictionary<string, ModelItemConfig>.Empty,
            FrozenDictionary<string, ModelEntry[]>.Empty,
            FrozenDictionary<string, FrozenDictionary<string, ModelItemConfig>>.Empty,
            FrozenDictionary<string, FrozenDictionary<string, string>>.Empty,
            FrozenDictionary<string, string>.Empty);

        /// <summary>从配置根一次性构建全部索引 — 唯一的构建入口。</summary>
        public static ModelConfigIndex Build(ModelConfigRoot config) {
            return new ModelConfigIndex(
                config,
                BuildModelById(config),
                BuildModelsByProvider(config),
                BuildModelByProviderAndId(config),
                BuildAliasByProviderAndInput(config),
                BuildProviderByModelId(config));
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

        /// <summary>两层嵌套 provider → (modelId → config)，消除复合 key 拼接，零分配 O(1) 查找。</summary>
        private static FrozenDictionary<string, FrozenDictionary<string, ModelItemConfig>> BuildModelByProviderAndId(ModelConfigRoot config) {
            var outer = new Dictionary<string, FrozenDictionary<string, ModelItemConfig>>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in config.Providers) {
                var inner = new Dictionary<string, ModelItemConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var model in provider.Value.Models) {
                    inner[model.Id] = model;
                }
                outer[provider.Key] = inner.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            }
            return outer.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>两层嵌套 provider → (alias → modelId)，消除复合 key 拼接，零分配 O(1) 查找。</summary>
        private static FrozenDictionary<string, FrozenDictionary<string, string>> BuildAliasByProviderAndInput(ModelConfigRoot config) {
            var outer = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in config.Providers) {
                var inner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var model in provider.Value.Models) {
                    foreach (var alias in model.Aliases) {
                        inner[alias] = model.Id;
                    }
                }
                outer[provider.Key] = inner.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            }
            return outer.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>modelId → providerName 索引，供 FindProviderByModelId O(1) 查找。</summary>
        private static FrozenDictionary<string, string> BuildProviderByModelId(ModelConfigRoot config) {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in config.Providers) {
                foreach (var model in provider.Value.Models) {
                    dict[model.Id] = provider.Key;
                }
            }
            return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }

    private ModelConfigIndex _index = ModelConfigIndex.Empty;

    public ModelConfigLoader() { }

    /// <summary>获取当前模型配置根节点。</summary>
    public ModelConfigRoot Config => Volatile.Read(ref _index).Config;

    /// <summary>
    /// 从 SettingsJson.Vendor 构建的 providers 数据灌入 — 唯一的数据入口
    /// 由 Core 层在加载 settings.json 后调用，热重载时再次调用
    /// </summary>
    public void ApplyProviders(Dictionary<string, ModelProviderConfig> providers) {
        var newIndex = ModelConfigIndex.Build(new ModelConfigRoot { Providers = providers });
        Interlocked.Exchange(ref _index, newIndex);
    }

    /// <summary>按供应商名称获取供应商配置。</summary>
    public ModelProviderConfig? GetProviderConfig(string providerName) {
        return Volatile.Read(ref _index).Config.Providers.GetValueOrDefault(providerName);
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
        return Volatile.Read(ref _index).ModelsByProvider.GetValueOrDefault(providerName) ?? [];
    }

    /// <summary>解析别名到模型 ID。</summary>
    public string? ResolveAlias(string providerName, string input) {
        return Volatile.Read(ref _index).AliasByProviderAndInput.GetValueOrDefault(providerName)?.GetValueOrDefault(input);
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
        var idx = Volatile.Read(ref _index);
        var exact = idx.ModelById.GetValueOrDefault(fullModelName);
        if (exact is not null)
            return !string.IsNullOrEmpty(exact.CanonicalId) ? exact.CanonicalId : exact.Id;

        var name = fullModelName.ToLowerInvariant();
        foreach (var model in idx.ModelById.Values) {
            if (name.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal)) {
                return !string.IsNullOrEmpty(model.CanonicalId) ? model.CanonicalId : model.Id;
            }
        }

        return fullModelName;
    }

    /// <summary>按供应商名称和模型 ID 查找模型配置。</summary>
    public ModelItemConfig? FindModel(string providerName, string modelId) {
        return Volatile.Read(ref _index).ModelByProviderAndId.GetValueOrDefault(providerName)?.GetValueOrDefault(modelId);
    }

    /// <summary>获取所有模型 ID 集合。</summary>
    public IReadOnlyCollection<string> GetAllModelIds() {
        return Volatile.Read(ref _index).ModelById.Keys;
    }

    /// <summary>按模型 ID 查找所属供应商名称。</summary>
    public string? FindProviderByModelId(string modelId) {
        return Volatile.Read(ref _index).ProviderByModelId.GetValueOrDefault(modelId);
    }

    /// <summary>按模型 ID 模糊查找模型配置。</summary>
    public ModelItemConfig? FindModelByModelId(string modelId) {
        var idx = Volatile.Read(ref _index);
        var exact = idx.ModelById.GetValueOrDefault(modelId);
        if (exact is not null) return exact;

        var lower = modelId.ToLowerInvariant();
        foreach (var model in idx.ModelById.Values) {
            if (lower.Contains(model.Id.ToLowerInvariant(), StringComparison.Ordinal))
                return model;
        }
        return null;
    }
}
