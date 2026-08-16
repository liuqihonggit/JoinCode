namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 资源引用图管理 — 维护跨插件资源引用关系
/// <para>连带卸载:卸载插件A时,通过 GetConsumers 找到所有引用A资源的插件B,通知B放弃引用</para>
/// <para>引用计数:通过 GetReferenceCounts 检查A所有资源的引用是否归零,归零才安全卸载</para>
/// </summary>
public interface IResourceReferenceGraph
{
    /// <summary>记录引用 — 插件B 引用 插件A 的资源</summary>
    void AddReference(ResourceReference reference);

    /// <summary>移除引用 — 引用方放弃引用</summary>
    void RemoveReference(ObjectId consumerResourceId, ObjectId targetResourceId);

    /// <summary>获取引用某插件资源的所有引用方插件名 — 用于连带卸载</summary>
    IReadOnlyList<string> GetConsumers(string targetPluginName);

    /// <summary>获取某插件引用的所有外部资源 — 用于释放引用</summary>
    IReadOnlyList<ResourceReference> GetReferencesBy(string consumerPluginName);

    /// <summary>获取某插件所有资源的引用计数 — 用于卸载前检查是否归零</summary>
    IReadOnlyDictionary<ObjectId, int> GetReferenceCounts(string pluginName);

    /// <summary>移除某插件的所有引用关系 — 卸载完成后清理</summary>
    void RemoveAllForPlugin(string pluginName);
}
