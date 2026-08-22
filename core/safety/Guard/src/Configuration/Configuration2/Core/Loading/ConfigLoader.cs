namespace Core.Configuration;

public class ConfigLoader {
    private readonly MiddlewarePipeline<ConfigLoadContext>? _pipeline;
    private readonly IProviderDefinitionRegistry _registry;
    private readonly SettingsMapper _settingsMapper;
    private readonly IModelConfigLoader? _modelConfigLoader;

    public ConfigLoader(IEnumerable<IConfigLoadMiddleware>? middlewares = null, ILoggerFactory? loggerFactory = null, IProviderDefinitionRegistry? registry = null, SettingsMapper? settingsMapper = null, IModelConfigLoader? modelConfigLoader = null)
    {
        _registry = registry ?? new ProviderDefinitionRegistry(modelConfigLoader ?? new ModelConfigLoader());
        _settingsMapper = settingsMapper ?? new SettingsMapper(_registry);
        _modelConfigLoader = modelConfigLoader;
        if (middlewares is not null && loggerFactory is not null)
        {
            _pipeline = new PipelineBuilder<ConfigLoadContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(middlewares)
                .Build();
        }
        else if (middlewares is not null)
        {
            _pipeline = new MiddlewarePipeline<ConfigLoadContext>(middlewares);
        }
    }

    /// <summary>
    /// 管道化加载配置 — 通过中间件管道执行7步配置加载
    /// </summary>
    public async Task<WorkflowConfig> LoadAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        if (_pipeline is null)
        {
            return await LoadConfigAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        var context = new ConfigLoadContext
        {
            FileSystem = fs,
            ProjectDirectory = fs.GetCurrentDirectory(),
            CancellationToken = cancellationToken
        };

        try
        {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (context.Failed)
            {
                throw new ConfigurationException(context.ErrorMessage ?? "[GRD004] 加载配置失败");
            }

            return context.Result ?? throw new ConfigurationException("[GRD001] 配置加载未产生结果");
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigurationException("[GRD005] 加载配置失败", ex);
        }
    }

    /// <summary>
    /// 加载配置（向后兼容）
    /// 配置优先级（从低到高）: UserSettings → ProjectSettings → LocalSettings → FlagSettings → PolicySettings → 环境变量 → Provider 定义环境变量
    /// </summary>
    public async Task<WorkflowConfig> LoadConfigAsync(IFileSystem fs, CancellationToken cancellationToken = default) {
        try {
            // Step 1: 并行加载多源配置 + 规则文件 + auth.json（4 路并行 I/O）
            var projectDir = fs.GetCurrentDirectory();
            var settingsTask = SettingsLoader.LoadAllSourcesAsync(
                fs,
                projectDir: projectDir,
                cancellationToken: cancellationToken);
            var rulesLoader = new ProjectRulesLoader(fs);
            var projectRulesTask = rulesLoader.LoadRulesAsync(projectDir, cancellationToken);
            var externalRulesLoader = new ExternalRulesLoader(fs);
            var externalRulesTask = externalRulesLoader.LoadProjectRulesAsync(projectDir, cancellationToken);
            var authTask = LoadAuthFileAsync(fs, cancellationToken);

            await Task.WhenAll(settingsTask, projectRulesTask, externalRulesTask, authTask).ConfigureAwait(false);

            var settings = await settingsTask.ConfigureAwait(false);
            var preloadedAuthData = await authTask.ConfigureAwait(false);

            // Step 1.5: 将 SettingsJson.Vendor 模型数据灌入 ModelConfigLoader（唯一数据入口）
            if (_modelConfigLoader is not null)
            {
                var providers = VendorModelMapper.BuildProviders(settings);
                _modelConfigLoader.ApplyProviders(providers);
            }

            // Step 2: 注入 settings.env 到环境变量（低优先级，不覆盖已有环境变量）
            SettingsMapper.InjectEnvFromSettings(settings);

            // Step 2.5: 环境变量覆盖 SettingsJson — 集中启动参数解析（JCC_VENDOR/MODEL_ID/ENDPOINT/PROFILE）
            settings = EnvOverrideApplier.Apply(settings);

            // Step 2.6: 环境变量指定的模型可能不在 settings.json models 列表中（如 JCC_MODEL_ID 指定新模型）
            // 此时 ModelConfigLoader 中没有该模型的模态信息，需从模型 ID 推断并补注册
            EnsureEnvModelInConfig(settings);

            // Step 3: SettingsJson → WorkflowConfig（JSON 反序列化映射）
            var config = _settingsMapper.ToWorkflowConfig(settings);

            // Step 4: 环境变量覆盖（Provider/Model/Endpoint 等，不含 API Key）
            _settingsMapper.ApplyEnvOverrides(config, settings);

            // Step 5: 统一 API Key 解析（auth.json → Provider 专属变量）— auth.json 已在 Step 1 预读
            config.Provider.ApiKey = await ResolveApiKeyAsync(
                config.Provider.Vendor, config.Provider.Definition, fs, cancellationToken, preloadedAuthData).ConfigureAwait(false);

            // Step 6: 规则赋值
            config.ProjectRules = await projectRulesTask.ConfigureAwait(false);
            config.ExternalRules = await externalRulesTask.ConfigureAwait(false);

            // Step 7: 验证 Provider 配置 — Provider 必须有 API Key
            var definition = _registry.TryGet(config.Provider.Vendor);
            if (definition is not null && !definition.IsValid(config.Provider))
            {
                throw new ConfigurationException(
                    $"Provider '{config.Provider.Vendor}' 配置无效: 缺少 API Key。" +
                    $"请设置环境变量 {definition.ApiKeyEnvironmentVariable ?? "供应商专属变量"}" +
                    $" 或在 {WorkflowConstants.Paths.AuthFilePath} 中添加 '{config.Provider.Vendor}' 键。");
            }

            return config;
        } catch (Exception ex) when (ex is not ConfigurationException) {
            throw new ConfigurationException("[GRD006] 加载配置失败", ex);
        }
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 加载强类型配置
    /// </summary>
    public static async Task<SettingsJson?> LoadSettingsJsonAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);

        if (!fs.FileExists(settingsPath))
            return null;

        try
        {
            var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 保存 SettingsJson 到 ~/.jcc/settings.json
    /// </summary>
    public static async Task SaveSettingsJsonAsync(SettingsJson settings, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);

        var directory = Path.GetDirectoryName(settingsPath);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        await fs.WriteAllTextAsync(settingsPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 统一 API Key 解析 — 按优先级从低到高: auth.json → Provider 专属环境变量
    /// </summary>
    public async Task<string> ResolveApiKeyAsync(string provider, IProviderDefinition? definition, IFileSystem fs, CancellationToken cancellationToken = default, Dictionary<string, string>? preloadedAuthData = null)
    {
        var sources = new List<(string Source, string Key)>(2);

        // 优先级 1 (最低): auth.json（若调用方已预读则直接用，避免重复 I/O）
        var apiKey = preloadedAuthData is not null
            ? ResolveApiKeyFromAuth(preloadedAuthData, provider)
            : await LoadApiKeyFromJccAsync(provider, fs, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(apiKey))
            sources.Add(("auth.json", apiKey));

        // 优先级 2 (最高): Provider 专属环境变量（如 DEEPSEEK_API_KEY、OPENAI_API_KEY）
        string? providerEnvVarName = null;
        if (definition is not null)
        {
            var providerApiKey = definition.ResolveApiKeyFromEnv();
            if (!string.IsNullOrEmpty(providerApiKey))
            {
                providerEnvVarName = definition.ApiKeyEnvironmentVariable ?? "provider-specific";
                sources.Add(($"{providerEnvVarName} 环境变量", providerApiKey));
                apiKey = providerApiKey;
            }
        }
        else
        {
            // 回退: definition 为 null（无 settings.json）时，根据 provider 名推断 API Key 环境变量名
            var inferredEnvVar = InferApiKeyEnvVar(provider);
            if (inferredEnvVar is not null)
            {
                var envValue = Environment.GetEnvironmentVariable(inferredEnvVar);
                if (!string.IsNullOrEmpty(envValue))
                {
                    sources.Add(($"{inferredEnvVar} 环境变量", envValue));
                    apiKey = envValue;
                }
            }
        }

        WarnOnApiKeyConflict(provider, sources);

        return apiKey;
    }

    /// <summary>
    /// 根据 vendor 名推断 API Key 环境变量名 — 当 settings.json 不存在、ProviderDefinitionRegistry 为空时使用
    /// </summary>
    private static string? InferApiKeyEnvVar(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return null;
        if (string.Equals(vendor, "openai", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.OpenAiApiKey.ToValue();
        if (string.Equals(vendor, "anthropic", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AnthropicApiKey.ToValue();
        if (string.Equals(vendor, "azure", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AzureOpenAiApiKey.ToValue();
        if (string.Equals(vendor, "deepseek", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.DeepSeekApiKey.ToValue();
        if (string.Equals(vendor, "agnes", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.AgnesApiKey.ToValue();
        if (string.Equals(vendor, "sensenova", StringComparison.OrdinalIgnoreCase))
            return ProviderEnvVar.SenseNovaApiKey.ToValue();
        return null;
    }

    /// <summary>
    /// 当多个 API Key 来源同时设置且值不同时，输出警告 — 避免静默覆盖导致 401 难以排查
    /// </summary>
    private static void WarnOnApiKeyConflict(string provider, List<(string Source, string Key)> sources)
    {
        if (sources.Count <= 1) return;

        var distinctKeys = sources.Select(s => s.Key).Distinct(StringComparer.Ordinal).ToList();
        if (distinctKeys.Count == 1) return;

        var used = sources[^1];
        var maskedKey = MaskKey(used.Key);
        var overridden = sources[..^1];

        var overriddenDesc = string.Join(", ", overridden.Select(s => $"{s.Source}({MaskKey(s.Key)})"));
        Diag.WriteLifecycle($"[WARN] API Key 来源冲突 | provider={provider} | 已设置 {sources.Count} 个来源: {overriddenDesc} → 最终使用 {used.Source}({maskedKey})");
        Diag.WriteLifecycle($"[WARN] 若遇到 401 认证失败，请检查 {used.Source} 是否有效，或清除冲突的环境变量");
    }

    /// <summary>
    /// 脱敏 API Key — 仅显示前 8 位和后 4 位，中间用 ... 替代
    /// </summary>
    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "<empty>";
        if (key.Length <= 12) return $"{key[..4]}...";
        return $"{key[..8]}...{key[^4..]}";
    }

    /// <summary>
    /// 读取 auth.json 文件内容 — 供并行预加载使用，与 settings/rules 并行避免串行 I/O
    /// </summary>
    private static async Task<Dictionary<string, string>?> LoadAuthFileAsync(IFileSystem fs, CancellationToken cancellationToken)
    {
        var authPath = WorkflowConstants.Paths.AuthFilePath;
        if (!fs.FileExists(authPath)) return null;
        try
        {
            var json = await fs.ReadAllTextAsync(authPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从已加载的 auth 数据中解析指定 provider 的 API Key（同步，无 I/O）
    /// </summary>
    private string ResolveApiKeyFromAuth(Dictionary<string, string>? authData, string provider)
    {
        if (authData is null || !authData.TryGetValue(provider, out var apiKey))
            return string.Empty;

        // Azure 等复合格式：auth.json 中存储的是 JSON 对象而非纯 API Key
        var definition = _registry.TryGet(provider);
        if (definition is not null && definition.IsCompoundAuthFormat(apiKey))
        {
            var compoundData = JsonSerializer.Deserialize(apiKey, ConfigJsonContext.Default.DictionaryStringString);
            return definition.ExtractApiKeyFromCompound(apiKey)
                ?? compoundData?.GetValueOrDefault("apiKey", string.Empty)
                ?? string.Empty;
        }

        return apiKey;
    }

    /// <summary>
    /// 从 ~/.jcc/auth.json 加载指定 provider 的 API Key
    /// </summary>
    public async Task<string> LoadApiKeyFromJccAsync(string provider, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var authData = await LoadAuthFileAsync(fs, cancellationToken).ConfigureAwait(false);
        return ResolveApiKeyFromAuth(authData, provider);
    }

    /// <summary>
    /// 保存 API Key 到 ~/.jcc/auth.json
    /// </summary>
    public static async Task SaveApiKeyToJccAsync(string provider, string apiKey, IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        var authPath = WorkflowConstants.Paths.AuthFilePath;
        var directory = Path.GetDirectoryName(authPath);

        if (!string.IsNullOrEmpty(directory) && !fs.DirectoryExists(directory))
            fs.CreateDirectory(directory);

        var authData = new Dictionary<string, string>();

        if (fs.FileExists(authPath))
        {
            try
            {
                var json = await fs.ReadAllTextAsync(authPath, cancellationToken).ConfigureAwait(false);
                authData = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                // 文件损坏，重新创建
                logger?.LogWarning(ex, "Failed to read auth file '{AuthPath}'", authPath);
            }
        }

        authData[provider] = apiKey;

        var outputJson = JsonSerializer.Serialize(authData, ConfigIndentedJsonContext.Default.DictionaryStringString);
        await fs.WriteAllTextAsync(authPath, outputJson, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 读取指定键的值（兼容旧版扁平 KV 格式）
    /// </summary>
    public static async Task<string?> LoadSettingFromSettingsJsonAsync(string key, IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        var settingsPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);

        if (!fs.FileExists(settingsPath))
            return null;

        try
        {
            var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            return TryGetSettingFromJson(json, key);
        }
        catch (Exception ex)
        {
            // 文件损坏或格式错误，忽略
            logger?.LogWarning(ex, "Failed to load setting '{Key}' from settings.json", key);
        }

        return null;
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 同步读取指定键的值（兼容旧版扁平 KV 格式）
    /// P1-3: 为 Lazy&lt;T&gt; 加载场景提供同步入口，避免 sync-over-async 阻塞
    /// </summary>
    public static string? LoadSettingFromSettingsJson(string key, IFileSystem fs, ILogger? logger = null)
    {
        var settingsPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);

        if (!fs.FileExists(settingsPath))
            return null;

        try
        {
            var json = fs.ReadAllText(settingsPath);
            return TryGetSettingFromJson(json, key);
        }
        catch (Exception ex)
        {
            // 文件损坏或格式错误，忽略
            logger?.LogWarning(ex, "Failed to load setting '{Key}' from settings.json", key);
        }

        return null;
    }

    /// <summary>
    /// 从 settings.json 文本中按键名获取值（兼容旧版扁平 KV 格式）
    /// 优先尝试强类型反序列化，回退到扁平 KV 格式
    /// </summary>
    private static string? TryGetSettingFromJson(string json, string key)
    {
        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        if (settings is not null)
        {
            var value = GetSettingByKey(settings, key);
            if (value is not null) return value;
        }

        return null;
    }

    /// <summary>
    /// 将指定键值对写入 ~/.jcc/settings.json — 对齐 TS updateSettingsForSource
    /// </summary>
    public static async Task SaveSettingToSettingsJsonAsync(string key, string? value, IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        var settingsPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);

        var directory = Path.GetDirectoryName(settingsPath);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        // 读取现有 settings — 统一用强类型 SettingsJson，不再回退到扁平 KV 格式
        SettingsJson? existingSettings = null;

        if (fs.FileExists(settingsPath))
        {
            try
            {
                var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
                existingSettings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
            }
            catch (Exception ex)
            {
                // 文件损坏，重新创建
                logger?.LogWarning(ex, "Failed to read settings file '{SettingsPath}'", settingsPath);
            }
        }

        existingSettings ??= new SettingsJson();
        var updatedSettings2 = UpdateSettingByKey(existingSettings, key, value);
        var outputJson2 = JsonSerializer.Serialize(updatedSettings2, ConfigIndentedJsonContext.Default.SettingsJson);
        await fs.WriteAllTextAsync(settingsPath, outputJson2, cancellationToken).ConfigureAwait(false);
    }

    #region 内部辅助方法

    /// <summary>
    /// 从 ~/.jcc/global.json 读取全局配置值 — 对齐 TS getGlobalConfig
    /// </summary>
    public static async Task<string?> LoadSettingFromGlobalConfigAsync(string key, IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        var globalPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.GlobalConfigFileName);

        if (!fs.FileExists(globalPath))
            return null;

        try
        {
            var json = await fs.ReadAllTextAsync(globalPath, cancellationToken).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringJsonElement);
            if (data is not null && data.TryGetValue(key, out var element))
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => element.GetRawText(),
                };
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load setting from global.json");
        }

        return null;
    }

    /// <summary>
    /// 将键值对写入 ~/.jcc/global.json — 对齐 TS saveGlobalConfig
    /// </summary>
    public static async Task SaveSettingToGlobalConfigAsync(string key, string? value, IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        var globalPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            AppDataConstants.GlobalConfigFileName);

        var directory = Path.GetDirectoryName(globalPath);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        Dictionary<string, JsonElement> data = new(StringComparer.Ordinal);

        if (fs.FileExists(globalPath))
        {
            try
            {
                var json = await fs.ReadAllTextAsync(globalPath, cancellationToken).ConfigureAwait(false);
                data = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringJsonElement) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to read global config file '{GlobalPath}'", globalPath);
            }
        }

        if (value is not null)
        {
            using var doc = JsonDocument.Parse($"\"{JsonEncodeValue(value)}\"");
            data[key] = doc.RootElement.Clone();
        }
        else
        {
            data.Remove(key);
        }

        var outputJson = JsonSerializer.Serialize(data, ConfigIndentedJsonContext.Default.DictionaryStringJsonElement);
        await fs.WriteAllTextAsync(globalPath, outputJson, cancellationToken).ConfigureAwait(false);
    }

    private static string JsonEncodeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从强类型 SettingsJson 中按键名获取值 — 路由到 CurrentSettings.GetSettingByKey
    /// </summary>
    private static string? GetSettingByKey(SettingsJson settings, string key)
        => settings.Current?.GetSettingByKey(key);

    /// <summary>
    /// 更新强类型 SettingsJson 中指定键的值，返回新对象（不可变）— 路由到 CurrentSettings.UpdateSettingByKey
    /// </summary>
    private static SettingsJson UpdateSettingByKey(SettingsJson settings, string key, string? value)
    {
        var updatedCurrent = settings.Current is not null
            ? settings.Current.UpdateSettingByKey(key, value)
            : new CurrentSettings().UpdateSettingByKey(key, value);

        return new SettingsJson
        {
            Vendor = settings.Vendor,
            Current = updatedCurrent,
        };
    }

    #endregion

    /// <summary>
    /// 确保环境变量指定的模型在 ModelConfigLoader 中注册
    /// <para>JCC_MODEL_ID 指定的模型必须已在 settings.json 的 vendor.{profile}.models 列表中注册</para>
    /// <para>未注册时无条件抛 ConfigurationException[GRD016] — 配置大于代码，不从模型 ID 推断模态</para>
    /// <para>例外: models 列表为空且 autoFetchModels=true 时跳过检查 — 首次运行时骨架 models 为空，由 AutoFetchModels 异步填充</para>
    /// </summary>
    private void EnsureEnvModelInConfig(SettingsJson settings)
    {
        if (_modelConfigLoader is null) return;
        if (settings.Vendor is null || settings.Current?.Profile is not { Length: > 0 } profile) return;
        if (!settings.Vendor.TryGetValue(profile, out var profileSettings)) return;

        var modelId = profileSettings.Model;
        if (string.IsNullOrEmpty(modelId)) return;

        if (_modelConfigLoader.FindModel(profile, modelId) is not null) return;

        // 首次运行: models 为空或 null 且启用自动拉取 → 跳过 GRD016，由 AutoFetchModels 异步填充
        // 注意: EnvOverrideApplier.Apply 合并 ProfileSettings 时可能将 Models 置 null
        if ((profileSettings.Models is null || profileSettings.Models.Count == 0) && settings.AutoFetchModels)
            return;

        throw new ConfigurationException(
            $"[GRD016] 模型 '{modelId}' 未在 settings.json 的 vendor.{profile}.models 列表中注册。" +
            $"请在 settings.json 中添加该模型的描述（含 Capabilities/Modalities）后重试。",
            configurationKey: $"{profile}.models[{modelId}]",
            configurationValue: modelId);
    }
}
