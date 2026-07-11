
namespace Core.Configuration.Providers;

/// <summary>
/// Provider 定义注册表 — 通过字典查找替代 if-else 链
/// </summary>
public static class ProviderDefinitionRegistry
{
    private static readonly FrozenDictionary<string, IProviderDefinition> Definitions =
        new IProviderDefinition[]
        {
            new OpenAIProviderDefinition(),
            new AzureProviderDefinition(),
            new AnthropicProviderDefinition(),
            new AgnesProviderDefinition(),
            new DeepSeekProviderDefinition(),
        }.ToFrozenDictionary(d => d.ProviderName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取指定 Provider 的定义，找不到返回 null
    /// </summary>
    public static IProviderDefinition? TryGet(string providerName)
    {
        return Definitions.GetValueOrDefault(providerName);
    }

    /// <summary>
    /// 获取所有已注册的 Provider 名称
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredProviders => Definitions.Keys;
}
