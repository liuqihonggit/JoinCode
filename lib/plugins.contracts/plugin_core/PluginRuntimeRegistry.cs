namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件运行时状态 — 合并 Package + RunningInstance + State 为单一不可变记录
/// </summary>
internal sealed record PluginRuntimeState {
    /// <summary>插件包定义（DefinePlugin 时设置）</summary>
    public required PluginPackage Package { get; init; }

    /// <summary>运行中实例（RunPlugin 时设置，StopPlugin 时移除）</summary>
    public RunningPluginInstance? RunningInstance { get; init; }

    /// <summary>插件状态</summary>
    public DynamicPluginState State { get; init; } = DynamicPluginState.Defined;
}

/// <summary>
/// 插件运行时注册表 — 管理插件的包定义、运行实例、状态
/// 持有以插件名为 key 的合并字典，提供定义、运行、停止、卸载、查询操作
/// </summary>
internal sealed class PluginRuntimeRegistry {
    private readonly ConcurrentDictionary<string, PluginRuntimeState> _entries = new();

    // ── 查询 ──

    /// <summary>获取插件包定义（未定义返回 null）</summary>
    public PluginPackage? GetPackage(string name)
        => _entries.GetValueOrDefault(name)?.Package;

    /// <summary>尝试获取插件包定义</summary>
    public bool TryGetPackage(string name, out PluginPackage package) {
        var state = _entries.GetValueOrDefault(name);
        if (state is not null) {
            package = state.Package;
            return true;
        }
        package = null!;
        return false;
    }

    /// <summary>尝试获取运行实例</summary>
    public bool TryGetRunning(string name, out RunningPluginInstance instance) {
        var state = _entries.GetValueOrDefault(name);
        if (state?.RunningInstance is not null) {
            instance = state.RunningInstance;
            return true;
        }
        instance = null!;
        return false;
    }

    /// <summary>获取插件状态（未定义返回 Undefined）</summary>
    public DynamicPluginState GetState(string name)
        => _entries.GetValueOrDefault(name)?.State ?? DynamicPluginState.Undefined;

    /// <summary>列出所有已定义插件名</summary>
    public IReadOnlyList<string> GetPackageNames()
        => _entries.Keys.ToArray();

    // ── 修改 ──

    /// <summary>定义插件（TryAdd，已存在返回 false）</summary>
    public bool TryDefine(string name, PluginPackage package)
        => _entries.TryAdd(name, new PluginRuntimeState { Package = package });

    /// <summary>设置运行实例并标记为 Running</summary>
    public void SetRunning(string name, RunningPluginInstance instance) {
        while (_entries.TryGetValue(name, out var state)) {
            if (_entries.TryUpdate(name, state with { RunningInstance = instance, State = DynamicPluginState.Running }, state))
                return;
        }
    }

    /// <summary>移除运行实例（返回是否找到并移除）</summary>
    public bool TryRemoveRunning(string name, out RunningPluginInstance instance) {
        while (_entries.TryGetValue(name, out var state)) {
            if (state.RunningInstance is null) {
                instance = null!;
                return false;
            }
            instance = state.RunningInstance;
            if (_entries.TryUpdate(name, state with { RunningInstance = null }, state))
                return true;
        }
        instance = null!;
        return false;
    }

    /// <summary>更新插件包定义</summary>
    public void UpdatePackage(string name, PluginPackage package) {
        while (_entries.TryGetValue(name, out var state)) {
            if (_entries.TryUpdate(name, state with { Package = package }, state))
                return;
        }
    }

    /// <summary>设置插件状态</summary>
    public void SetState(string name, DynamicPluginState state) {
        while (_entries.TryGetValue(name, out var old)) {
            if (_entries.TryUpdate(name, old with { State = state }, old))
                return;
        }
    }

    /// <summary>永久移除插件（返回是否找到）</summary>
    public bool Undefine(string name)
        => _entries.TryRemove(name, out _);
}