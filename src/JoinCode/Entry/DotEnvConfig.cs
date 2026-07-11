namespace JoinCode.Entry;

/// <summary>
/// .env/api.json 本地开发配置 — Debug/Release 均可使用
/// 解析 Claude Code 格式的 JSON 配置，映射到 JoinCode 配置系统
/// </summary>
internal sealed class DotEnvConfig
{
    public string? ApiKey { get; set; }
    public string? Provider { get; set; }
    public string? Endpoint { get; set; }
    public string? ModelId { get; set; }
    public string? EffortLevel { get; set; }

    /// <summary>
    /// 从 .env/api.json 文件解析配置
    /// </summary>
    public static DotEnvConfig? LoadFrom(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return null;

        try
        {
            var content = System.IO.File.ReadAllText(filePath);
            var json = System.Text.Json.JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("env", out var envObj))
                return null;

            var config = new DotEnvConfig();

            // 多态：遍历 ProviderDefinitionRegistry 注册表匹配环境变量，替代 if-else 链硬编码
            // 新增供应商时无需修改此文件，只需在 ProviderDefinitionRegistry 注册即可
            foreach (var providerName in Core.Configuration.Providers.ProviderDefinitionRegistry.RegisteredProviders)
            {
                var def = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet(providerName);
                if (def?.ApiKeyEnvironmentVariable is not null && envObj.TryGetProperty(def.ApiKeyEnvironmentVariable, out var keyVal) && keyVal.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    config.Provider = providerName;
                    config.ApiKey = keyVal.GetString();
                    break;
                }
            }

            // ANTHROPIC_AUTH_TOKEN 兼容（Anthropic 旧版环境变量名，不在 ApiKeyEnvironmentVariable 中）
            if (config.Provider is null && envObj.TryGetProperty("ANTHROPIC_AUTH_TOKEN", out var authTokenVal) && authTokenVal.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                config.Provider = "anthropic";
                config.ApiKey = authTokenVal.GetString();
            }

            // JCC_API_KEY 通用回退
            if (config.ApiKey is null && envObj.TryGetProperty("JCC_API_KEY", out var jccKeyVal) && jccKeyVal.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                config.ApiKey = jccKeyVal.GetString();
            }

            // JCC_PROVIDER 显式指定
            if (envObj.TryGetProperty("JCC_PROVIDER", out var providerVal) && providerVal.ValueKind == System.Text.Json.JsonValueKind.String)
                config.Provider = providerVal.GetString();

            // 多态：遍历注册表匹配 Endpoint 环境变量，替代硬编码 ANTHROPIC_BASE_URL
            var rawEndpoint = envObj.EnumerateObject()
                .FirstOrDefault(p => p.Name is "JCC_ENDPOINT").Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? envObj.EnumerateObject().First(p => p.Name is "JCC_ENDPOINT").Value.GetString()
                : null;

            // 各 Provider 的 Endpoint 环境变量匹配
            if (rawEndpoint is null && config.Provider is not null)
            {
                var def = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet(config.Provider);
                if (def?.EndpointEnvironmentVariable is not null && envObj.TryGetProperty(def.EndpointEnvironmentVariable, out var epVal) && epVal.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    rawEndpoint = epVal.GetString();
                }
            }

            // ANTHROPIC_BASE_URL 兼容（旧版环境变量名）
            if (rawEndpoint is null && envObj.TryGetProperty("ANTHROPIC_BASE_URL", out var anthropicBaseVal) && anthropicBaseVal.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                rawEndpoint = anthropicBaseVal.GetString();
            }

            if (rawEndpoint is not null)
            {
                // 去掉末尾的 /v1 或 /v1/，因为 GetChatEndpoint 会追加 "v1/messages"
                var trimmed = rawEndpoint.TrimEnd('/');
                if (trimmed.EndsWith("/v1", System.StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[..^3];
                config.Endpoint = trimmed + "/";
            }

            // Model: JCC_MODEL_ID（通用），ANTHROPIC_DEFAULT_SONNET_MODEL（兼容旧版）
            config.ModelId = envObj.EnumerateObject()
                .FirstOrDefault(p => p.Name is "JCC_MODEL_ID" or "ANTHROPIC_DEFAULT_SONNET_MODEL")
                .Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? envObj.EnumerateObject()
                    .First(p => p.Name is "JCC_MODEL_ID" or "ANTHROPIC_DEFAULT_SONNET_MODEL")
                    .Value.GetString()
                : null;

            // Effort Level
            if (envObj.TryGetProperty("CLAUDE_CODE_EFFORT_LEVEL", out var effortVal) && effortVal.ValueKind == System.Text.Json.JsonValueKind.String)
                config.EffortLevel = effortVal.GetString();

            return config;
        }
        catch (System.Text.Json.JsonException)
        {
            System.Diagnostics.Trace.WriteLine("DotEnvConfig: JSON parse failed");
            return null;
        }
    }

    /// <summary>
    /// 将配置写入真实配置文件（auth.json / settings.json），使其他组件也能读取
    /// </summary>
    public async Task ApplyToConfigAsync(JoinCode.Abstractions.Interfaces.IFileSystem fs)
    {
        if (ApiKey is not null && Provider is not null)
        {
            await ConfigLoader.SaveApiKeyToJccAsync(Provider, ApiKey, fs);
        }

        if (Provider is not null)
        {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("provider", Provider, fs);
        }

        if (Endpoint is not null)
        {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("endpoint", Endpoint, fs);
        }

        if (ModelId is not null)
        {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("modelId", ModelId, fs);
        }

        if (EffortLevel is not null)
        {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("effortLevel", EffortLevel, fs);
        }
    }

    /// <summary>
    /// 将配置应用到内存中的 WorkflowConfig
    /// </summary>
    public void ApplyToMemory(WorkflowConfig config)
    {
        if (ApiKey is not null)
            config.Provider.ApiKey = ApiKey;

        if (Provider is not null)
            config.Provider.Provider = Provider;

        if (Endpoint is not null)
            config.Provider.Endpoint = Endpoint;

        if (ModelId is not null)
            config.Provider.ModelId = ModelId;

        if (Provider is not null)
        {
            var definition = ProviderDefinitionRegistry.TryGet(Provider);
            if (definition is not null)
            {
                config.Provider.Definition = definition;
                config.Provider.ModelId ??= definition.DefaultModelId;
                config.Provider.Endpoint ??= definition.DefaultEndpoint;
            }
        }
    }
}
