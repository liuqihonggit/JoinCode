namespace JoinCode.Entry;

/// <summary>
/// .env/api.json 本地开发配置 — Debug/Release 均可使用
/// 解析 JoinCode 格式的 JSON 配置，映射到 JoinCode 配置系统
/// </summary>
internal sealed record DotEnvConfig {
    /// <summary>API Key</summary>
    public string? ApiKey { get; init; }
    /// <summary>供应商名称</summary>
    public string? Vendor { get; init; }
    /// <summary>API 端点地址</summary>
    public string? Endpoint { get; init; }
    /// <summary>模型 ID</summary>
    public string? ModelId { get; init; }
    /// <summary>推理努力级别</summary>
    public string? EffortLevel { get; init; }

    /// <summary>
    /// 从 .env/api.json 文件解析配置
    /// </summary>
    public static DotEnvConfig? LoadFrom(string filePath, ILogger? logger = null, IProviderDefinitionRegistry? registry = null) {
        if (!System.IO.File.Exists(filePath)) return null;

        try {
            var content = SyncFileReader.RunValueTask(SafeFileIO.ReadAllText(filePath));
            var dto = System.Text.Json.JsonSerializer.Deserialize(content, DotEnvJsonContext.Default.DotEnvFileDto);
            if (dto?.Env is null)
                return null;

            var env = dto.Env;
            registry ??= Core.Configuration.Providers.ProviderDefinitionRegistry.Create(new ModelConfigLoader());

            string? vendor = null;
            string? apiKey = null;

            // 多态：遍历 ProviderDefinitionRegistry 注册表匹配环境变量，替代 if-else 链硬编码
            // 新增供应商时无需修改此文件，只需在 ProviderDefinitionRegistry 注册即可
            // 动态 key（def.ApiKeyEnvironmentVariable）保留字典查找，不做成 DTO 固定属性
            foreach (var providerName in registry.GetRegisteredProviders()) {
                var def = registry.TryGet(providerName);
                if (def?.ApiKeyEnvironmentVariable is not null && env.TryGetValue(def.ApiKeyEnvironmentVariable, out var keyVal)) {
                    vendor = providerName;
                    apiKey = keyVal;
                    break;
                }
            }

            // ANTHROPIC_AUTH_TOKEN 兼容（Anthropic 旧版环境变量名，不在 ApiKeyEnvironmentVariable 中）— 委托 VendorKind 枚举 — P1-⑤
            if (vendor is null && env.TryGetValue("ANTHROPIC_AUTH_TOKEN", out var authTokenVal)) {
                vendor = VendorKind.Anthropic.ToValue();
                apiKey = authTokenVal;
            }

            // JCC_VENDOR 显式指定 — 委托 JccEnvVar 枚举（唯一数据源）— P0-④
            if (env.TryGetValue(JccEnvVar.Vendor.ToValue(), out var providerVal))
                vendor = providerVal;

            // 多态：遍历注册表匹配 Endpoint 环境变量，替代硬编码 ANTHROPIC_BASE_URL — 委托 JccEnvVar 枚举（唯一数据源）— P0-④
            var jccEndpointName = JccEnvVar.Endpoint.ToValue();
            var rawEndpoint = env.TryGetValue(jccEndpointName, out var ep0) ? ep0 : null;

            // 各 Provider 的 Endpoint 环境变量匹配（动态 key 保留字典查找）
            if (rawEndpoint is null && vendor is not null) {
                var def = registry.TryGet(vendor);
                if (def?.EndpointEnvironmentVariable is not null && env.TryGetValue(def.EndpointEnvironmentVariable, out var epVal)) {
                    rawEndpoint = epVal;
                }
            }

            // ANTHROPIC_BASE_URL 兼容（旧版环境变量名）
            if (rawEndpoint is null && env.TryGetValue("ANTHROPIC_BASE_URL", out var anthropicBaseVal)) {
                rawEndpoint = anthropicBaseVal;
            }

            string? endpoint = null;
            if (rawEndpoint is not null) {
                // 去掉末尾的 /v1 或 /v1/，因为 GetChatEndpoint 会追加 "v1/messages"
                var trimmed = rawEndpoint.TrimEnd('/');
                if (trimmed.EndsWith("/v1", System.StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[..^3];
                endpoint = trimmed + "/";
            }

            // Model: JCC_MODEL_ID（通用，委托 JccEnvVar 枚举），ANTHROPIC_DEFAULT_SONNET_MODEL（兼容旧版）— P0-④
            var jccModelIdName = JccEnvVar.ModelId.ToValue();
            var modelId = env.TryGetValue(jccModelIdName, out var mid1) ? mid1
                : env.TryGetValue("ANTHROPIC_DEFAULT_SONNET_MODEL", out var mid2) ? mid2
                : null;

            // Effort Level — 委托 JccEnvVar 枚举（唯一数据源）— P0-④
            var effortLevel = env.TryGetValue(JccEnvVar.EffortLevel.ToValue(), out var effortVal) ? effortVal : null;

            return new DotEnvConfig {
                ApiKey = apiKey,
                Vendor = vendor,
                Endpoint = endpoint,
                ModelId = modelId,
                EffortLevel = effortLevel
            };
        } catch (System.Text.Json.JsonException ex) {
            logger?.LogWarning(ex, "DotEnvConfig: JSON 解析失败");
            return null;
        }
    }

    /// <summary>
    /// 将配置写入真实配置文件（auth.json / settings.json），使其他组件也能读取
    /// </summary>
    public async Task ApplyToConfigAsync(JoinCode.Abstractions.Interfaces.IFileSystem fs) {
        if (ApiKey is not null && Vendor is not null) {
            await ConfigLoader.SaveApiKeyToJccAsync(Vendor, ApiKey, fs).ConfigureAwait(false);
        }

        if (Vendor is not null) {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("provider", Vendor, fs).ConfigureAwait(false);
        }

        if (Endpoint is not null) {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("endpoint", Endpoint, fs).ConfigureAwait(false);
        }

        if (ModelId is not null) {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("modelId", ModelId, fs).ConfigureAwait(false);
        }

        if (EffortLevel is not null) {
            await ConfigLoader.SaveSettingToSettingsJsonAsync("effortLevel", EffortLevel, fs).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 将配置应用到内存中的 WorkflowConfig
    /// </summary>
    public WorkflowConfig ApplyToMemory(WorkflowConfig config) {
        return ApplyToMemory(config, Core.Configuration.Providers.ProviderDefinitionRegistry.Create(new ModelConfigLoader()));
    }

    /// <summary>
    /// 将配置应用到内存中的 WorkflowConfig — 使用指定供应商注册表解析定义
    /// </summary>
    /// <param name="config">目标 WorkflowConfig 实例</param>
    /// <param name="registry">供应商定义注册表</param>
    public WorkflowConfig ApplyToMemory(WorkflowConfig config, IProviderDefinitionRegistry registry) {
        var p = config.Provider;
        var newApiKey = ApiKey ?? p.ApiKey;
        var newVendor = Vendor ?? p.Vendor;
        var newEndpoint = Endpoint ?? p.Endpoint;
        var newModelId = ModelId ?? p.ModelId;
        var newDefinition = p.Definition;
        var newProtocol = p.Protocol;

        if (Vendor is not null) {
            var definition = registry.TryGet(Vendor);
            if (definition is not null) {
                newDefinition = definition;
                newProtocol = definition.Protocol.ToValue();
                newModelId ??= definition.DefaultModelId;
                newEndpoint ??= definition.DefaultEndpoint;
            }
        }

        return config with {
            Provider = p with {
                ApiKey = newApiKey,
                Vendor = newVendor,
                Endpoint = newEndpoint,
                ModelId = newModelId,
                Definition = newDefinition,
                Protocol = newProtocol,
            },
        };
    }
}

/// <summary>.env/api.json 外层包装 DTO — env 为环境变量键值字典（key 动态，不做成固定属性）</summary>
public sealed class DotEnvFileDto {
    /// <summary>环境变量键值表（key 为环境变量名，动态；值统一为字符串）</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];
}

/// <summary>DotEnvConfig JSON 反序列化上下文 — AOT 兼容（源码生成），宽容注释/尾逗号</summary>
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DotEnvFileDto))]
public partial class DotEnvJsonContext : JsonSerializerContext;