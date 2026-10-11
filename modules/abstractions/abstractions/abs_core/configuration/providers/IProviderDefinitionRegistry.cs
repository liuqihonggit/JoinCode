
namespace JoinCode.Abstractions.Configuration.Providers;

public interface IProviderDefinitionRegistry : IRegistry {
    /// <summary>按名称尝试获取供应商定义。</summary>
    /// <param name="providerName">供应商名称。</param>
    IProviderDefinition? TryGet(string providerName);
    /// <summary>
    /// 供应商是否已注册 — O(1) 查询
    /// </summary>
    /// <param name="providerName">供应商名称</param>
    /// <returns>已注册返回 true</returns>
    bool Contains(string providerName);
    /// <summary>
    /// 获取已注册供应商名称的快照拷贝 — 用于枚举/显示
    /// </summary>
    /// <returns>供应商名称数组快照</returns>
    string[] GetRegisteredProviders();
}
