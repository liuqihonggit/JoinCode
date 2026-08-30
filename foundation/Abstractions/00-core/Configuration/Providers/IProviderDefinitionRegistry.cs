
namespace JoinCode.Abstractions.Configuration.Providers;

public interface IProviderDefinitionRegistry : IRegistry
{
    IProviderDefinition? TryGet(string providerName);
    IReadOnlyCollection<string> RegisteredProviders { get; }
}
