
namespace JoinCode.Abstractions.Configuration.Llm;

public static class ModelConfigLoader
{
    private static volatile ModelConfigRoot _config;
    private static FrozenDictionary<string, ModelItemConfig> _modelById;
    private static FrozenDictionary<string, string> _aliasToModelId;

    static ModelConfigLoader()
    {
        var config = LoadCore();
        _config = config;
        _modelById = BuildModelById(config);
        _aliasToModelId = BuildAliasToModelId(config);
    }

    public static ModelConfigRoot Config => _config;

    /// <summary>重新加载配置（热重载）— 从嵌入资源 + 用户覆盖文件重新读取，原子替换所有静态缓存</summary>
    public static void Reload()
    {
        var newConfig = LoadCore();
        var newModelById = BuildModelById(newConfig);
        var newAliasToModelId = BuildAliasToModelId(newConfig);
        _config = newConfig;
        _modelById = newModelById;
        _aliasToModelId = newAliasToModelId;
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

    public static ModelProviderConfig? GetProviderConfig(string providerName)
    {
        return Config.Providers.GetValueOrDefault(providerName);
    }

    public static string GetDefaultModelId(string providerName)
    {
        return GetProviderConfig(providerName)?.DefaultModelId ?? string.Empty;
    }

    public static string GetDefaultFastModelId(string providerName)
    {
        return GetProviderConfig(providerName)?.DefaultFastModelId ?? string.Empty;
    }

    public static ModelEntry[] GetModels(string providerName)
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

    public static string? ResolveAlias(string providerName, string input)
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

    public static bool SupportsFastMode(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.FastMode ?? true;
    }

    public static bool SupportsEffort(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.Effort ?? false;
    }

    public static bool SupportsMaxEffort(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.MaxEffort ?? false;
    }

    public static bool SupportsThinkingMode(string providerName, string modelId)
    {
        var model = FindModel(providerName, modelId);
        return model?.Capabilities.ThinkingMode ?? false;
    }

    public static string GetCanonicalName(string fullModelName)
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

    public static ModelItemConfig? FindModel(string providerName, string modelId)
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

    public static IReadOnlyCollection<string> GetAllModelIds()
    {
        return _modelById.Keys;
    }

    public static string? FindProviderByModelId(string modelId)
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

    public static ModelItemConfig? FindModelByModelId(string modelId)
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

    /// <summary>用户覆盖文件路径 — ~/AppData/JoinCode/models.json</summary>
    public static string GetUserOverridePath()
    {
        return System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            AppData.AppDataConstants.AppDataFolder, "models.json");
    }

    private static ModelConfigRoot LoadCore()
    {
        var assembly = typeof(ModelConfigLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("models.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var json = reader.ReadToEnd();

        var config = System.Text.Json.JsonSerializer.Deserialize(json, ModelConfigJsonContext.Default.ModelConfigRoot)
            ?? new ModelConfigRoot();

        ApplyUserOverride(config);

        return config;
    }

#pragma warning disable JCC9001
    private static void ApplyUserOverride(ModelConfigRoot config)
    {
        var userConfigPath = GetUserOverridePath();

        if (!System.IO.File.Exists(userConfigPath))
            return;

        try
        {
            var userJson = System.IO.File.ReadAllText(userConfigPath);
            var userConfig = System.Text.Json.JsonSerializer.Deserialize(userJson, ModelConfigJsonContext.Default.ModelConfigRoot);
            if (userConfig is null)
                return;

            foreach (var kvp in userConfig.Providers)
            {
                config.Providers[kvp.Key] = kvp.Value;
            }
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"ModelConfigLoader: 用户覆盖文件加载失败: {ex.Message}");
        }
    }
#pragma warning restore JCC9001
}
