namespace Core.Plugins;

/// <summary>
/// 插件生命周期跟踪器 — 管理插件的撤销链（同步+异步）和加载顺序
/// 从 PluginManager 提取,Consumer 线程独占,无需并发容器
/// </summary>
internal sealed class PluginLifecycleTracker {
    private readonly Dictionary<string, List<Action>> _undoChain = new();
    private readonly Dictionary<string, List<IAsyncDisposable>> _asyncUndoChain = new();
    private readonly List<string> _loadOrder = new();
    private readonly ILogger? _logger;
    private readonly Action<PluginDiagnostic>? _reportDiagnostic;

    /// <summary>初始化 <see cref="PluginLifecycleTracker"/> 实例</summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="reportDiagnostic">诊断上报回调（撤销失败时调用）</param>
    public PluginLifecycleTracker(ILogger? logger, Action<PluginDiagnostic>? reportDiagnostic) {
        _logger = logger;
        _reportDiagnostic = reportDiagnostic;
    }

    /// <summary>添加插件到加载顺序</summary>
    public void AddToLoadOrder(string pluginName) => _loadOrder.Add(pluginName);

    /// <summary>从加载顺序中移除插件</summary>
    public void RemoveFromLoadOrder(string pluginName) => _loadOrder.Remove(pluginName);

    /// <summary>获取加载顺序的逆序副本</summary>
    public List<string> GetLoadOrderReversed() {
        var list = _loadOrder.ToList();
        list.Reverse();
        return list;
    }

    /// <summary>注册插件的撤销链（同步+异步）</summary>
    public void RegisterUndoChain(string pluginName, List<Action> undoChain, List<IAsyncDisposable>? asyncUndoChain) {
        _undoChain[pluginName] = undoChain;
        if (asyncUndoChain is not null)
            _asyncUndoChain[pluginName] = asyncUndoChain;
    }

    /// <summary>执行插件同步撤销链 — 按逆序执行所有撤销函数,完成后从加载顺序移除</summary>
    public void ExecuteUndoChain(string pluginName) {
        if (_undoChain.Remove(pluginName, out var undoChain)) {
            for (var i = undoChain.Count - 1; i >= 0; i--) {
                try { undoChain[i](); } catch (Exception ex) {
                    _logger?.LogWarning(ex, "插件 {PluginName} 撤销链第 {Index} 项执行失败", pluginName, i);
                    _reportDiagnostic?.Invoke(new PluginDiagnostic {
                        PluginId = pluginName,
                        Kind = PluginDiagnosticKind.RevertFailed,
                        Message = $"撤销链第 {i} 项执行失败: {ex.Message}",
                        Suggestion = "检查副作用撤销操作是否正确处理了已释放的资源"
                    });
                }
            }
        }

        RemoveFromLoadOrder(pluginName);
    }

    /// <summary>执行插件异步撤销链 — 按逆序 await DisposeAsync</summary>
    public async Task ExecuteAsyncUndoChainAsync(string pluginName, CancellationToken ct) {
        if (_asyncUndoChain.Remove(pluginName, out var chain)) {
            for (var i = chain.Count - 1; i >= 0; i--) {
                try { await chain[i].DisposeAsync().ConfigureAwait(false); } catch (Exception ex) {
                    _logger?.LogWarning(ex, "插件 {PluginName} async 撤销链第 {Index} 项失败", pluginName, i);
                    _reportDiagnostic?.Invoke(new PluginDiagnostic {
                        PluginId = pluginName,
                        Kind = PluginDiagnosticKind.RevertFailed,
                        Message = $"异步撤销链第 {i} 项失败: {ex.Message}",
                        Suggestion = "检查 IAsyncDisposable.DisposeAsync 是否正确处理了已释放的资源"
                    });
                }
            }
        }
    }

    /// <summary>清空所有撤销链和加载顺序</summary>
    public void Clear() {
        _undoChain.Clear();
        _asyncUndoChain.Clear();
        _loadOrder.Clear();
    }
}