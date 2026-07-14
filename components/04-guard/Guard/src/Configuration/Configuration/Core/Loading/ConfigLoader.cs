namespace Core.Configuration;

public class ConfigLoader {
    private readonly MiddlewarePipeline<ConfigLoadContext>? _pipeline;
    private readonly IProviderDefinitionRegistry _registry;
    private readonly SettingsMapper _settingsMapper;

    public ConfigLoader(IEnumerable<IConfigLoadMiddleware>? middlewares = null, IProviderDefinitionRegistry? registry = null, SettingsMapper? settingsMapper = null)
    {
        _registry = registry ?? new ProviderDefinitionRegistry();
        _settingsMapper = settingsMapper ?? new SettingsMapper(_registry);
        if (middlewares is not null)
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
                throw new ConfigurationException(context.ErrorMessage ?? "加载配置失败");
            }

            return context.Result ?? throw new ConfigurationException("配置加载未产生结果");
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigurationException("加载配置失败", ex);
        }
    }

    /// <summary>
    /// 加载配置（向后兼容）
    /// 配置优先级（从低到高）: UserSettings → ProjectSettings → LocalSettings → FlagSettings → PolicySettings → 环境变量 → Provider 定义环境变量
    /// </summary>
    public async Task<WorkflowConfig> LoadConfigAsync(IFileSystem fs, CancellationToken cancellationToken = default) {
        try {
            // Step 1: 并行加载多源配置 + 规则文件
            var projectDir = fs.GetCurrentDirectory();
            var settingsTask = SettingsLoader.LoadAllSourcesAsync(
                fs,
                projectDir: projectDir,
                cancellationToken: cancellationToken);
            var rulesLoader = new ProjectRulesLoader(fs);
            var projectRulesTask = rulesLoader.LoadRulesAsync(projectDir, cancellationToken);
            var externalRulesLoader = new ExternalRulesLoader(fs);
            var externalRulesTask = externalRulesLoader.LoadProjectRulesAsync(projectDir, cancellationToken);

            await Task.WhenAll(settingsTask, projectRulesTask, externalRulesTask).ConfigureAwait(false);

            var settings = await settingsTask.ConfigureAwait(false);

            // Step 2: 注入 settings.env 到环境变量（低优先级，不覆盖已有环境变量）
            SettingsMapper.InjectEnvFromSettings(settings);

            // Step 3: SettingsJson → WorkflowConfig（JSON 反序列化映射）
            var config = _settingsMapper.ToWorkflowConfig(settings);

            // Step 4: 环境变量覆盖（Provider/Model/Endpoint 等，不含 API Key）
            _settingsMapper.ApplyEnvOverrides(config);

            // Step 5: 统一 API Key 解析（auth.json → JCC_API_KEY → Provider 专属变量）
            config.Provider.ApiKey = await ResolveApiKeyAsync(
                config.Provider.Provider, config.Provider.Definition, fs, cancellationToken).ConfigureAwait(false);

            // Step 6: 规则赋值
            config.ProjectRules = await projectRulesTask.ConfigureAwait(false);
            config.ExternalRules = await externalRulesTask.ConfigureAwait(false);

            // Step 7: 验证 Provider 配置 — Provider 必须有 API Key
            var definition = _registry.TryGet(config.Provider.Provider);
            if (definition is not null && !definition.IsValid(config.Provider))
            {
                throw new ConfigurationException(
                    $"Provider '{config.Provider.Provider}' 配置无效: 缺少 API Key。" +
                    $"请设置环境变量 {definition.ApiKeyEnvironmentVariable ?? "JCC_API_KEY"}" +
                    $" 或在 {WorkflowConstants.Paths.AuthFilePath} 中添加 '{config.Provider.Provider}' 键。");
            }

            return config;
        } catch (Exception ex) when (ex is not ConfigurationException) {
            throw new ConfigurationException("加载配置失败", ex);
        }
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 加载强类型配置
    /// </summary>
    public static async Task<SettingsJson?> LoadSettingsJsonAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
            AppDataConstants.SettingsFileName);

        var directory = Path.GetDirectoryName(settingsPath);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        await fs.WriteAllTextAsync(settingsPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 统一 API Key 解析 — 按优先级从低到高: auth.json → JCC_API_KEY → Provider 专属环境变量
    /// 对齐 TS 版: 环境变量 > auth.json > apiKeyHelper
    /// </summary>
    public async Task<string> ResolveApiKeyAsync(string provider, IProviderDefinition? definition, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        // 优先级 1 (最低): auth.json
        var apiKey = await LoadApiKeyFromJccAsync(provider, fs, cancellationToken).ConfigureAwait(false);

        // 优先级 2: JCC_API_KEY 环境变量
        var jccApiKey = Environment.GetEnvironmentVariable(JccEnvVar.ApiKey.ToValue());
        if (!string.IsNullOrEmpty(jccApiKey))
            apiKey = jccApiKey;

        // 优先级 3 (最高): Provider 专属环境变量（如 OPENAI_API_KEY）
        if (definition is not null)
        {
            var providerApiKey = definition.ResolveApiKeyFromEnv();
            if (!string.IsNullOrEmpty(providerApiKey))
                apiKey = providerApiKey;
        }

        return apiKey;
    }

    /// <summary>
    /// 从 ~/.jcc/auth.json 加载指定 provider 的 API Key
    /// </summary>
    public async Task<string> LoadApiKeyFromJccAsync(string provider, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var authPath = WorkflowConstants.Paths.AuthFilePath;

        if (!fs.FileExists(authPath))
            return string.Empty;

        try
        {
            var json = await fs.ReadAllTextAsync(authPath, cancellationToken).ConfigureAwait(false);
            var authData = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString);

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
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 保存 API Key 到 ~/.jcc/auth.json
    /// </summary>
    public static async Task SaveApiKeyToJccAsync(string provider, string apiKey, IFileSystem fs, CancellationToken cancellationToken = default)
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
                System.Diagnostics.Trace.WriteLine($"Failed to read auth file '{authPath}': {ex.Message}");
            }
        }

        authData[provider] = apiKey;

        var outputJson = JsonSerializer.Serialize(authData, ConfigIndentedJsonContext.Default.DictionaryStringString);
        await fs.WriteAllTextAsync(authPath, outputJson, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 读取指定键的值（兼容旧版扁平 KV 格式）
    /// </summary>
    public static async Task<string?> LoadSettingFromSettingsJsonAsync(string key, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
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
            System.Diagnostics.Trace.WriteLine($"Failed to load setting '{key}' from settings.json: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 从 ~/.jcc/settings.json 同步读取指定键的值（兼容旧版扁平 KV 格式）
    /// P1-3: 为 Lazy&lt;T&gt; 加载场景提供同步入口，避免 sync-over-async 阻塞
    /// </summary>
    public static string? LoadSettingFromSettingsJson(string key, IFileSystem fs)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
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
            System.Diagnostics.Trace.WriteLine($"Failed to load setting '{key}' from settings.json: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 从 settings.json 文本中按键名获取值（兼容旧版扁平 KV 格式）
    /// 优先尝试强类型反序列化，回退到扁平 KV 格式
    /// </summary>
    private static string? TryGetSettingFromJson(string json, string key)
    {
        // 优先尝试强类型反序列化
        var settings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        if (settings is not null)
        {
            var value = GetSettingByKey(settings, key);
            if (value is not null) return value;
        }

        // 回退到扁平 KV 格式（兼容旧版）
        var data = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString);
        if (data is not null && data.TryGetValue(key, out var flatValue))
            return flatValue;

        return null;
    }

    /// <summary>
    /// 将指定键值对写入 ~/.jcc/settings.json — 对齐 TS updateSettingsForSource
    /// </summary>
    public static async Task SaveSettingToSettingsJsonAsync(string key, string? value, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
            AppDataConstants.SettingsFileName);

        var directory = Path.GetDirectoryName(settingsPath);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        // 读取现有 settings
        SettingsJson? existingSettings = null;
        Dictionary<string, string>? flatData = null;

        if (fs.FileExists(settingsPath))
        {
            try
            {
                var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
                existingSettings = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
                flatData = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DictionaryStringString);
            }
            catch (Exception ex)
            {
                // 文件损坏，重新创建
                System.Diagnostics.Trace.WriteLine($"Failed to read settings file '{settingsPath}': {ex.Message}");
            }
        }

        // 如果已有强类型数据，更新强类型字段
        if (existingSettings is not null)
        {
            var updatedSettings = UpdateSettingByKey(existingSettings, key, value);
            var outputJson = JsonSerializer.Serialize(updatedSettings, ConfigIndentedJsonContext.Default.SettingsJson);
            await fs.WriteAllTextAsync(settingsPath, outputJson, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 回退到扁平 KV 格式（兼容旧版）
        flatData ??= [];
        if (value is not null)
            flatData[key] = value;
        else
            flatData.Remove(key);

        var flatJson = JsonSerializer.Serialize(flatData, ConfigIndentedJsonContext.Default.DictionaryStringString);
        await fs.WriteAllTextAsync(settingsPath, flatJson, cancellationToken).ConfigureAwait(false);
    }

    #region 内部辅助方法

    /// <summary>
    /// 从 ~/.jcc/global.json 读取全局配置值 — 对齐 TS getGlobalConfig
    /// </summary>
    public static async Task<string?> LoadSettingFromGlobalConfigAsync(string key, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var globalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
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
            System.Diagnostics.Trace.WriteLine($"Failed to load setting from global.json: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 将键值对写入 ~/.jcc/global.json — 对齐 TS saveGlobalConfig
    /// </summary>
    public static async Task SaveSettingToGlobalConfigAsync(string key, string? value, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var globalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
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
                System.Diagnostics.Trace.WriteLine($"Failed to read global config file '{globalPath}': {ex.Message}");
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
    /// 从强类型 SettingsJson 中按键名获取值 — 委托给源码生成器自动生成的 GetSettingByKey
    /// </summary>
    private static string? GetSettingByKey(SettingsJson settings, string key)
        => settings.GetSettingByKey(key);

    /// <summary>
    /// 更新强类型 SettingsJson 中指定键的值，返回新对象（不可变）— 委托给源码生成器自动生成的 UpdateSettingByKey
    /// </summary>
    private static SettingsJson UpdateSettingByKey(SettingsJson settings, string key, string? value)
        => settings.UpdateSettingByKey(key, value);

    #endregion
}
