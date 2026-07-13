
namespace Core.Configuration.Providers;

public sealed class ProviderDefinitionRegistry : IProviderDefinitionRegistry
{
    private readonly FrozenDictionary<string, IProviderDefinition> _definitions;

    public ProviderDefinitionRegistry()
    {
        _definitions = new IProviderDefinition[]
        {
            new OpenAIProviderDefinition(),
            new AzureProviderDefinition(),
            new AnthropicProviderDefinition(),
            new AgnesProviderDefinition(),
            new DeepSeekProviderDefinition(),
        }.ToFrozenDictionary(d => d.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    public IProviderDefinition? TryGet(string providerName)
    {
        return _definitions.GetValueOrDefault(providerName);
    }

    public IReadOnlyCollection<string> RegisteredProviders => _definitions.Keys;
}
