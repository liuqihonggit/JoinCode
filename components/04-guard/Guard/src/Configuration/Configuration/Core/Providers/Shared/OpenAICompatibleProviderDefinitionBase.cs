
namespace Core.Configuration.Providers;

/// <summary>
/// OpenAI 兼容协议的 Provider 定义基类 — 提取 OpenAI / Azure 共享的模型列表、别名映射和能力判断
/// 子类只需覆写 Provider 专属属性（Kind / ProviderName / DisplayName / 环境变量 / URL 构建等）
/// </summary>
public abstract class OpenAICompatibleProviderDefinitionBase : IProviderDefinition
{
    public abstract ProviderKind Kind { get; }
    public abstract string ProviderName { get; }
    public abstract string DisplayName { get; }
    public abstract string DefaultModelId { get; }
    public abstract string DefaultFastModelId { get; }
    public abstract string? DefaultEndpoint { get; }
    public abstract string? ApiKeyEnvironmentVariable { get; }
    public abstract string? EndpointEnvironmentVariable { get; }

    public abstract string GetBaseUrl(ProviderConfig config);
    public abstract string GetChatEndpoint(ProviderConfig config);
    public abstract void ConfigureHttpClient(HttpClient client, ProviderConfig config);
    public abstract string? ResolveApiKeyFromEnv();
    public abstract bool IsValid(ProviderConfig config);

    public virtual IReadOnlyList<ModelEntry> AvailableModels => SharedOpenAiModels;

    public virtual string? ResolveAlias(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "4o" => CanonicalModel.Gpt4o.ToValue(),
            "4o-mini" => CanonicalModel.Gpt4oMini.ToValue(),
            "4.1" => CanonicalModel.Gpt41.ToValue(),
            "4.1-mini" => CanonicalModel.Gpt41Mini.ToValue(),
            "4.1-nano" => CanonicalModel.Gpt41Nano.ToValue(),
            "o3" => CanonicalModel.O3.ToValue(),
            "o4-mini" => CanonicalModel.O4Mini.ToValue(),
            _ => null
        };
    }

    public virtual bool SupportsFastMode(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        return !modelId.ToLowerInvariant().StartsWith("o3");
    }

    public virtual bool SupportsEffort(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        var lower = modelId.ToLowerInvariant();
        return lower.StartsWith("o3") || lower.StartsWith("o4");
    }

    public virtual bool SupportsMaxEffort(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        var lower = modelId.ToLowerInvariant();
        return lower.StartsWith("o3") && !lower.Contains("mini");
    }

    public virtual string? DefaultApiVersion => null;
    public virtual string? ResolveEndpointFromEnv() => null;
    public virtual bool IsCompoundAuthFormat(string apiKey) => false;
    public virtual string? ExtractApiKeyFromCompound(string apiKey) => null;
    public virtual bool SupportsOAuth => false;
    public virtual OAuthConfig? GetOAuthConfig() => null;
    public virtual bool SupportsWebSearch => false;
    public virtual bool RequiresInteractiveEndpoint => false;
    public virtual string SerializeAuthCredentials(string apiKey, string? endpoint) => apiKey;
    public virtual string? EndpointPromptText => null;
    public virtual string? EndpointRequiredMessage => null;

    private static readonly ModelEntry[] SharedOpenAiModels =
    [
        new("gpt-4o", "GPT-4o", 128_000, "旗舰多模态模型"),
        new("gpt-4o-mini", "GPT-4o Mini", 128_000, "快速低成本模型"),
        new("gpt-4.1", "GPT-4.1", 1_047_576, "最新旗舰，1M 上下文"),
        new("gpt-4.1-mini", "GPT-4.1 Mini", 1_047_576, "高效平衡，1M 上下文"),
        new("gpt-4.1-nano", "GPT-4.1 Nano", 1_047_576, "最快最便宜，1M 上下文"),
        new("o3", "O3", 200_000, "深度推理模型"),
        new("o4-mini", "O4 Mini", 200_000, "高效推理模型"),
    ];
}
