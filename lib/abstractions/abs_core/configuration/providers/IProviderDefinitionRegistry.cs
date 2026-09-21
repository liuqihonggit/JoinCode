
namespace JoinCode.Abstractions.Configuration.Providers;

public interface IProviderDefinitionRegistry : IRegistry {
    /// <summary>按名称尝试获取供应商定义。</summary>
    /// <param name="providerName">供应商名称。</param>
    IProviderDefinition? TryGet(string providerName);
    /// <summary>获取已注册的供应商名称集合。</summary>
    IReadOnlyCollection<string> RegisteredProviders { get; }
}
