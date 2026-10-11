namespace Core.Plugins;

/// <summary>
/// 插件注册表 — 基于 MapRegistry 不可变字典 + 按 PluginKind 次级索引，O(1) 检索
/// </summary>
internal sealed class PluginRegistry : MapRegistry<string, IPluginHost> {
    /// <summary>按插件类型建立的次级索引 — O(1) 获取指定类型的全部插件名</summary>
    public SecondaryIndex<string, IPluginHost, PluginKind> ByKind { get; }

    /// <summary>构造插件注册表 — 初始化按 PluginKind 的次级索引</summary>
    public PluginRegistry() : base(StringComparer.Ordinal) {
        ByKind = CreateIndex(host => host.PluginType);
    }
}
