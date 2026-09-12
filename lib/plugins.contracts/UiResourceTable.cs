namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源表 — 插件持有的界面资源(图标、菜单项、工具栏按钮等)
/// <para>可逆操作时用户卸载了 UI,但需要刷新界面(重新排列图标)</para>
/// <para>卸载时 ClearAndEmitEvent 生成变更事件,通过 IAppEventBus 广播</para>
/// </summary>
public sealed class UiResourceTable
{
    private readonly ConcurrentDictionary<string, UiResourceEntry> _resources = new();

    /// <summary>登记 UI 资源</summary>
    public void Register(string key, UiResourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entry);
        _resources[key] = entry;
    }

    /// <summary>移除 UI 资源</summary>
    public bool Unregister(string key) => _resources.TryRemove(key, out _);

    /// <summary>获取所有已登记的 UI 资源</summary>
    public IReadOnlyCollection<UiResourceEntry> GetAll() => _resources.Values.ToList();

    /// <summary>获取指定资源</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out UiResourceEntry? entry) => _resources.TryGetValue(key, out entry);

    /// <summary>清空并返回变更事件 — 卸载时调用</summary>
    public UiResourceChangedEvent ClearAndEmitEvent(string pluginName)
    {
        var removed = _resources.Values.ToList();
        _resources.Clear();
        return new UiResourceChangedEvent(pluginName, removed, DateTime.UtcNow);
    }

    /// <summary>当前资源数量</summary>
    public int Count => _resources.Count;
}
