namespace Services.Lsp.Internal;

/// <summary>
/// LSP 服务器注册表 — 成对管理服务器实例表与扩展名→服务器名映射，
/// 提供按服务器名 / 文件扩展名的查找入口。
/// </summary>
internal sealed class LspServerRegistry {
    private readonly ConcurrentDictionary<string, LspServerInstance> _servers = new();
    private readonly ConcurrentDictionary<string, List<string>> _extensionMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已注册服务器数量。
    /// </summary>
    public int Count => _servers.Count;

    /// <summary>
    /// 所有服务器实例视图 — 用于 Shutdown/Dispose 遍历（不要在此视图上做写操作）。
    /// </summary>
    public IEnumerable<LspServerInstance> Servers => _servers.Values;

    /// <summary>
    /// 注册服务器实例并建立扩展名映射。
    /// </summary>
    public void Register(string name, LspServerInstance instance, Dictionary<string, string> extensionToLanguage) {
        _servers[name] = instance;
        foreach (var kvp in extensionToLanguage) {
            var serverNames = _extensionMap.GetOrAdd(kvp.Key, _ => []);
            serverNames.Add(name);
        }
    }

    /// <summary>
    /// 按服务器名查找实例。
    /// </summary>
    public bool TryGetByName(string name, [MaybeNullWhen(false)] out LspServerInstance instance)
        => _servers.TryGetValue(name, out instance);

    /// <summary>
    /// 按文件扩展名查找对应的服务器实例（取扩展名映射中的第一个服务器）。
    /// </summary>
    public bool TryGetByExtension(string ext, [MaybeNullWhen(false)] out LspServerInstance instance) {
        instance = null!;
        if (!_extensionMap.TryGetValue(ext, out var serverNames) || serverNames.Count == 0)
            return false;
        return _servers.TryGetValue(serverNames[0], out instance);
    }

    /// <summary>
    /// 所有服务器快照 — 用于 GetAllServers 接口方法。
    /// </summary>
    public IReadOnlyDictionary<string, ILspServerInstance> Snapshot()
        => _servers.ToDictionary(kvp => kvp.Key, kvp => (ILspServerInstance)kvp.Value);

    /// <summary>
    /// 清空所有服务器和扩展名映射 — 用于 Shutdown/Dispose。
    /// </summary>
    public void Clear() {
        _servers.Clear();
        _extensionMap.Clear();
    }
}