// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// UI 资源表 — 插件持有的界面资源(图标、菜单项、工具栏按钮等)
/// <para>可逆操作时用户卸载了 UI,但需要刷新界面(重新排列图标)</para>
/// <para>卸载时 ClearAndEmitEvent 生成变更事件,通过 IAppEventBus 广播</para>
/// </summary>
public sealed class UiResourceTable {
    private volatile ImmutableHamT<string, UiResourceEntry> _resources = ImmutableHamT<string, UiResourceEntry>.Empty;

    /// <summary>登记 UI 资源</summary>
    public void Register(string key, UiResourceEntry entry) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entry);
        while (true) {
            var current = _resources;
            if (Interlocked.CompareExchange(ref _resources, current.SetItem(key, entry), current) == current) break;
        }
    }

    /// <summary>移除 UI 资源</summary>
    public bool Unregister(string key) {
        var removed = false;
        while (true) {
            var current = _resources;
            ImmutableHamT<string, UiResourceEntry> updated;
            if (current.ContainsKey(key)) {
                removed = true;
                updated = current.Remove(key);
            } else {
                updated = current;
            }
            if (Interlocked.CompareExchange(ref _resources, updated, current) == current) break;
        }
        return removed;
    }

    /// <summary>获取所有已登记的 UI 资源</summary>
    public IReadOnlyCollection<UiResourceEntry> GetAll() => _resources.Values.ToList();

    /// <summary>获取指定资源</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out UiResourceEntry? entry) => _resources.TryGetValue(key, out entry);

    /// <summary>清空并返回变更事件 — 卸载时调用</summary>
    public UiResourceChangedEvent ClearAndEmitEvent(string pluginName) {
        ImmutableHamT<string, UiResourceEntry> snapshot = default!;
        while (true) {
            var current = _resources;
            if (Interlocked.CompareExchange(ref _resources, ImmutableHamT<string, UiResourceEntry>.Empty, current) == current) { snapshot = current; break; }
        }
        return new UiResourceChangedEvent(pluginName, snapshot.Values.ToList(), DateTime.UtcNow);
    }

    /// <summary>当前资源数量</summary>
    public int Count => _resources.Count;
}