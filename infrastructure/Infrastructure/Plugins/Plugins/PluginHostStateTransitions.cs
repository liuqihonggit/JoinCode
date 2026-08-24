namespace Core.Plugins;

/// <summary>
/// 外部插件宿主状态 — 归纳 _isDisposed/_isUnloaded/_wasForceKilled 三 bool 的合法组合
/// </summary>
public enum PluginHostState
{
    /// <summary>已加载运行中 — 三 bool 均为 false</summary>
    [EnumValue("loaded")] Loaded,

    /// <summary>已卸载（正常退出）— _isUnloaded=true, _wasForceKilled=false</summary>
    [EnumValue("unloaded")] Unloaded,

    /// <summary>已强制终止 — _isUnloaded=true, _wasForceKilled=true</summary>
    [EnumValue("force_killed")] ForceKilled,

    /// <summary>已释放 — _isDisposed=true（终态）</summary>
    [EnumValue("disposed")] Disposed
}

/// <summary>
/// 外部插件宿主状态转换规则 — 集中定义 PluginHostState 所有合法转换
/// <para>Loaded→Unloaded/ForceKilled, Unloaded/ForceKilled→Disposed, Disposed 为终态</para>
/// </summary>
public static class PluginHostStateTransitions
{
    private static readonly FrozenDictionary<PluginHostState, FrozenSet<PluginHostState>> Transitions =
        new Dictionary<PluginHostState, FrozenSet<PluginHostState>>
        {
            [PluginHostState.Loaded] = new HashSet<PluginHostState>
            {
                PluginHostState.Unloaded,
                PluginHostState.ForceKilled
            }.ToFrozenSet(),

            [PluginHostState.Unloaded] = new HashSet<PluginHostState>
            {
                PluginHostState.Disposed
            }.ToFrozenSet(),

            [PluginHostState.ForceKilled] = new HashSet<PluginHostState>
            {
                PluginHostState.Disposed
            }.ToFrozenSet(),

            [PluginHostState.Disposed] = FrozenSet<PluginHostState>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PluginHostState current, PluginHostState target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Disposed 为终态
    /// </summary>
    public static bool IsTerminal(PluginHostState state) => state == PluginHostState.Disposed;
}
