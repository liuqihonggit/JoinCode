
namespace JoinCode.Abstractions.Configuration.Providers;

public interface IProviderDefinitionRegistry
{
    IProviderDefinition? TryGet(string providerName);
    IReadOnlyCollection<string> RegisteredProviders { get; }
}
