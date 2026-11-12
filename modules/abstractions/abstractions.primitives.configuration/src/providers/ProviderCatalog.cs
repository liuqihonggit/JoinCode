namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 供应商管理器 — 注册表，每次从 map 中获取（热更新安全）。
/// <para>访问语法：catalog["供应商名"].CurrentModel</para>
/// <para>热更新通过原子替换内部 map 实现，无锁编程。</para>
/// <para>消费点通过 catalog.Current.CurrentModel 解引用出 endpoint/protocol/modelId 等连接参数。</para>
/// </summary>
public sealed class ProviderCatalog {
    private volatile FrozenDictionary<string, ProviderEntry> _providers = FrozenDictionary<string, ProviderEntry>.Empty;
    private volatile string _currentProviderName = string.Empty;

    /// <summary>当前激活的供应商 — 每次从 map 中获取最新引用</summary>
    public ProviderEntry Current =>
        string.IsNullOrEmpty(_currentProviderName)
            ? throw new InvalidOperationException("未设置当前供应商")
            : _providers.TryGetValue(_currentProviderName, out var entry)
                ? entry
                : throw new InvalidOperationException($"当前供应商 '{_currentProviderName}' 未注册");

    /// <summary>按名称获取供应商 — 每次从 map 中获取最新引用</summary>
    /// <param name="name">供应商名称</param>
    /// <returns>供应商条目</returns>
    /// <exception cref="KeyNotFoundException">供应商未注册</exception>
    public ProviderEntry this[string name] =>
        _providers.TryGetValue(name, out var entry)
            ? entry
            : throw new KeyNotFoundException($"供应商 '{name}' 未注册");

    /// <summary>设置当前激活的供应商</summary>
    /// <param name="name">供应商名称</param>
    public void SetCurrent(string name) {
        _currentProviderName = name;
    }

    /// <summary>原子替换全部供应商 + 当前供应商名 — 热更新入口</summary>
    /// <param name="providers">新的供应商字典</param>
    /// <param name="currentProviderName">当前供应商名</param>
    public void UpdateProviders(Dictionary<string, ProviderEntry> providers, string currentProviderName) {
        _providers = providers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _currentProviderName = currentProviderName;
    }

    /// <summary>获取所有已注册供应商名称</summary>
    public IReadOnlyList<string> GetProviderNames() => _providers.Keys.ToList();

    /// <summary>尝试获取供应商</summary>
    /// <param name="name">供应商名称</param>
    /// <param name="entry">命中时为供应商条目</param>
    /// <returns>命中返回 true</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out ProviderEntry? entry) =>
        _providers.TryGetValue(name, out entry);
}
