
namespace Core.Plugins;

[Register]
public sealed partial class PluginManager : ServiceEntity, IPluginManager
{
    private readonly ConcurrentDictionary<string, WorkflowPluginHost> _workflowPlugins = new();
    private readonly ConcurrentDictionary<string, ExternalPluginHost> _externalPlugins = new();
    private readonly IChatClient? _kernel;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IFileOperationService? _fileOperationService;
    private readonly ICommandRegistry? _commandRegistry;
    [Inject] private readonly ILogger<PluginManager>? _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private bool _isDisposed;
    private IPluginHotReloader? _hotReloader;
    private IPluginHookInjector? _hookInjector;
    private IPluginCommandRegistry? _pluginCommandRegistry;
    private IPluginAgentLoader? _pluginAgentLoader;

    /// <summary>每个插件的撤销链 — 按注册顺序记录,卸载时按逆序执行(Cordis Effect 系统)</summary>
    private readonly ConcurrentDictionary<string, List<Action>> _pluginUndoChain = new();

    /// <summary>每个插件的异步撤销链 — IAsyncDisposable,卸载时先于同步撤销链逆序 await 执行</summary>
    private readonly ConcurrentDictionary<string, List<IAsyncDisposable>> _pluginAsyncUndoChain = new();

    /// <summary>插件加载顺序 — 用于 UnloadAllPluginsAsync 按注册逆序卸载</summary>
    private readonly List<string> _loadOrder = new();
    private readonly object _loadOrderLock = new();

    /// <summary>每个插件的资源 ObjectId 列表 — 卸载后用于扫描验证</summary>
    private readonly ConcurrentDictionary<string, List<ObjectId>> _pluginResourceIds = new();

    /// <summary>插件黑名单 — 卸载泄漏的插件加入,拒绝再次加载(方案B C4)</summary>
    private readonly ConcurrentDictionary<string, byte> _blacklistedPlugins = new();

    private IResourceReferenceGraph? _referenceGraph;
    private PluginResourceScanner? _resourceScanner;
    private IAppEventBus? _eventBus;

    private IResourceReferenceGraph? ReferenceGraph => _referenceGraph ??= _serviceProvider?.GetService<IResourceReferenceGraph>();
    private PluginResourceScanner ResourceScanner => _resourceScanner ??= new(_loggerFactory?.CreateLogger<PluginResourceScanner>());
    private IAppEventBus? EventBus => _eventBus ??= _serviceProvider?.GetService<IAppEventBus>();

    public event EventHandler<string>? PluginLoaded;
    public event EventHandler<string>? PluginUnloading;

    public IReadOnlyCollection<string> LoadedPluginNames =>
        _workflowPlugins.Keys.Concat(_externalPlugins.Keys).ToList();

    public IReadOnlyCollection<string> LoadedWorkflowPluginNames => _workflowPlugins.Keys.ToList();
    public IReadOnlyCollection<string> LoadedExternalPluginNames => _externalPlugins.Keys.ToList();

    public PluginManager(
        IFileSystem fs,
        IChatClient? kernel = null,
        ILoggerFactory? loggerFactory = null,
        IFileOperationService? fileOperationService = null,
        ICommandRegistry? commandRegistry = null,
        ILogger<PluginManager>? logger = null,
        IServiceProvider? serviceProvider = null,
        ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _kernel = kernel;
        _loggerFactory = loggerFactory;
        _fileOperationService = fileOperationService;
        _commandRegistry = commandRegistry;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _telemetryService = telemetryService;
    }

    private IPluginHotReloader? HotReloader => _hotReloader ??= _serviceProvider?.GetService<IPluginHotReloader>();
    private IPluginHookInjector? HookInjector => _hookInjector ??= _serviceProvider?.GetService<IPluginHookInjector>();
    private IPluginCommandRegistry? CommandRegistry => _pluginCommandRegistry ??= _serviceProvider?.GetService<IPluginCommandRegistry>();
    private IPluginAgentLoader? PluginAgentLoader => _pluginAgentLoader ??= _serviceProvider?.GetService<IPluginAgentLoader>();

    #region Internal Workflow Plugin (AOT Compatible)

    public async Task<WorkflowPluginHost> LoadWorkflowPluginAsync<TPlugin>(CancellationToken cancellationToken = default) where TPlugin : class, IWorkflowPlugin, new()
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        var plugin = new TPlugin();
        var pluginName = plugin.Name;

        var span = _telemetryService?.StartSpan("plugin.load.workflow", TelemetrySpanKind.Server);
        span?.SetTag("plugin", pluginName);
        try
        {
            if (_workflowPlugins.ContainsKey(pluginName) || _externalPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException($"[INF031] 插件 '{pluginName}' 已经加载");
            }

            if (_blacklistedPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException($"[INF-PLUGIN-BL] 插件 '{pluginName}' 已被加入黑名单(此前卸载泄漏),拒绝加载");
            }

            _logger?.LogInformation("正在加载内置工作流插件: {PluginName}", pluginName);

            if (plugin is WorkflowPluginBase wpbLoad)
            {
                wpbLoad.Fiber.TransitionTo(PluginFiberState.Loading);
            }

            var host = new WorkflowPluginHost(plugin, _kernel, _loggerFactory, _fileOperationService, _commandRegistry, _logger);

            var loadResult = await host.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!loadResult.Success)
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException($"[INF032] 插件 '{pluginName}' Load 失败: {loadResult.ErrorMessage}");
            }

            var initResult = await host.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (!initResult.Success)
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Unload();
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException($"[INF033] 插件 '{pluginName}' Initialize 失败: {initResult.ErrorMessage}");
            }

            if (plugin is WorkflowPluginBase contractPlugin)
            {
                var contract = contractPlugin.ValidateUnloadContract();
                if (!contract.IsValid)
                {
                    contractPlugin.Fiber.TransitionTo(PluginFiberState.Failed);
                    host.Unload();
                    host.Dispose();
                    RecordPluginMetrics("workflow", "load", false);
                    throw new InvalidOperationException(
                        $"[INF-PLUGIN-CONTRACT] 插件 '{pluginName}' 拒绝加载: {contract.Reason}");
                }
            }

            if (!_workflowPlugins.TryAdd(pluginName, host))
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Unload();
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException($"[INF034] 插件 '{pluginName}' 已经加载");
            }

            var undoChain = new List<Action>();

            PluginLoaded?.Invoke(this, pluginName);

            if (plugin is IPluginAgentProvider agentProvider && PluginAgentLoader is not null)
            {
                var undo = PluginAgentLoader.LoadFromPlugin(pluginName, agentProvider);
                undoChain.Add(undo);
            }

            _pluginUndoChain[pluginName] = undoChain;
            if (host.Context is not null)
            {
                _pluginAsyncUndoChain[pluginName] = host.Context.GetAsyncUndoChain().ToList();
            }
            AddToLoadOrder(pluginName);

            if (plugin is WorkflowPluginBase pluginBase)
            {
                RecordPluginResourceIds(pluginName, pluginBase.Resources.Select(r => r.ObjectId));
                pluginBase.Fiber.TransitionTo(PluginFiberState.Active);
            }

            _logger?.LogInformation("内置工作流插件加载成功: {PluginName}", pluginName);
            RecordPluginMetrics("workflow", "load", true);
            return host;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            if (plugin is WorkflowPluginBase wpbEx)
            {
                wpbEx.Fiber.TryTransitionTo(PluginFiberState.Failed);
            }
            RecordPluginMetrics("workflow", "load", false);
            throw;
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    public WorkflowPluginHost? GetWorkflowPlugin(string pluginName)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        return _workflowPlugins.TryGetValue(pluginName, out var host) ? host : null;
    }

    public T? GetWorkflowPlugin<T>(string pluginName) where T : class, IWorkflowPlugin
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        return _workflowPlugins.TryGetValue(pluginName, out var host) ? host.Plugin as T : null;
    }

    #endregion

    #region External Process Plugin (AOT Compatible)

    public async Task<ExternalPluginHost> LoadExternalPluginAsync(
        string exePath,
        string pluginName,
        CancellationToken cancellationToken = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        var span = _telemetryService?.StartSpan("plugin.load.external", TelemetrySpanKind.Server);
        span?.SetTag("plugin", pluginName);
        try
        {
            if (_workflowPlugins.ContainsKey(pluginName) || _externalPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException($"[INF035] 插件 '{pluginName}' 已经加载");
            }

            if (!_fs.FileExists(exePath))
            {
                RecordPluginMetrics("external", "load", false);
                throw new FileNotFoundException($"[INF036] 外部插件可执行文件不存在: {exePath}", exePath);
            }

            _logger?.LogInformation("正在加载外部插件: {PluginName} 从 {ExePath}", pluginName, exePath);

            var builder = new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider());
            var startInfo = builder.BuildInteractive(new InteractiveProcessOptions
            {
                FileName = exePath,
                RedirectStandardError = true,
            });

            var process = new Process { StartInfo = startInfo };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger?.LogWarning("外部插件 {PluginName} stderr: {Data}", pluginName, e.Data);
                }
            };

            bool started;
            try { started = process.Start(); }
            catch (Exception) { process.Dispose(); throw; }

            if (!started)
            {
                process.Dispose();
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException($"[INF037] 无法启动外部插件进程: {exePath}");
            }

            process.BeginErrorReadLine();

            var host = new ExternalPluginHost(pluginName, process, exePath, _logger);

            if (!_externalPlugins.TryAdd(pluginName, host))
            {
                host.Dispose();
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException($"[INF038] 插件 '{pluginName}' 已经加载");
            }

            _logger?.LogInformation("外部插件加载成功: {PluginName} (PID: {ProcessId})", pluginName, process.Id);
            RecordPluginMetrics("external", "load", true);
            return host;
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not FileNotFoundException)
        {
            RecordPluginMetrics("external", "load", false);
            throw;
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ExternalPluginHost? GetExternalPlugin(string pluginName)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        return _externalPlugins.TryGetValue(pluginName, out var host) ? host : null;
    }

    #endregion

    #region Unload

    public Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, PluginUnloadOptions? options = null)
    {
        var opts = options ?? PluginUnloadOptions.Default;
        var cts = new CancellationTokenSource(opts.CooperativeTimeout);
        return UnloadPluginAsync(pluginName, cts.Token);
    }

    public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken cancellationToken)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_externalPlugins.TryRemove(pluginName, out var externalHost))
        {
            await CleanupPluginServicesAsync(pluginName, cancellationToken).ConfigureAwait(false);
            var result = externalHost.Unload();
            externalHost.Dispose();
            RecordPluginMetrics("external", "unload", result.IsSuccess);
            return result;
        }

        await CascadeUnloadDependentsAsync(pluginName, cancellationToken).ConfigureAwait(false);

        var prepareResult = await PrepareUnloadAsync(pluginName, cancellationToken).ConfigureAwait(false);
        if (!prepareResult) return PluginUnloadResult.CooperativeTimeout(pluginName, TimeSpan.Zero);

        if (_workflowPlugins.TryRemove(pluginName, out var workflowHost))
        {
            await ExecutePluginAsyncUndoChainAsync(pluginName, cancellationToken).ConfigureAwait(false);
            ExecutePluginUndoChain(pluginName);
            await CleanupPluginServicesAsync(pluginName, cancellationToken).ConfigureAwait(false);
            var result = UnloadWorkflowPlugin(workflowHost);
            RecordPluginMetrics("workflow", "unload", result.IsSuccess);

            ScanAfterUnload(pluginName);
            await BroadcastUiResourceChangeAsync(pluginName, workflowHost).ConfigureAwait(false);

            return result;
        }

        return PluginUnloadResult.AlreadyUnloaded(pluginName);
    }

    public async Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsAsync(PluginUnloadOptions? options = null, CancellationToken cancellationToken = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        var results = new List<PluginUnloadResult>();

        var externalPluginNames = _externalPlugins.Keys.ToList();
        foreach (var pluginName in externalPluginNames)
        {
            if (_externalPlugins.TryRemove(pluginName, out var host))
            {
                results.Add(host.Unload());
                host.Dispose();
            }
        }

        List<string> workflowPluginNames = GetLoadOrderReversed();

        foreach (var pluginName in workflowPluginNames)
        {
            if (_workflowPlugins.TryRemove(pluginName, out var host))
            {
                await ExecutePluginAsyncUndoChainAsync(pluginName, cancellationToken).ConfigureAwait(false);
                ExecutePluginUndoChain(pluginName);
                await CleanupPluginServicesAsync(pluginName, cancellationToken).ConfigureAwait(false);
                results.Add(UnloadWorkflowPlugin(host));
            }
        }

        return results;
    }

    /// <summary>记录插件加载顺序</summary>
    private void AddToLoadOrder(string pluginName)
    {
        lock (_loadOrderLock)
        {
            _loadOrder.Add(pluginName);
        }
    }

    /// <summary>从加载顺序中移除插件</summary>
    private void RemoveFromLoadOrder(string pluginName)
    {
        lock (_loadOrderLock)
        {
            _loadOrder.Remove(pluginName);
        }
    }

    /// <summary>获取加载顺序的逆序副本 — 用于按注册逆序卸载(Cordis)</summary>
    private List<string> GetLoadOrderReversed()
    {
        lock (_loadOrderLock)
        {
            var list = _loadOrder.ToList();
            list.Reverse();
            return list;
        }
    }

    /// <summary>执行插件撤销链 — 按逆序执行所有撤销函数(Cordis Effect 系统)</summary>
    private void ExecutePluginUndoChain(string pluginName)
    {
        if (_pluginUndoChain.TryRemove(pluginName, out var undoChain))
        {
            for (int i = undoChain.Count - 1; i >= 0; i--)
            {
                try { undoChain[i](); }
                catch (Exception ex) { _logger?.LogWarning(ex, "插件 {PluginName} 撤销链第 {Index} 项执行失败", pluginName, i); }
            }
        }

        RemoveFromLoadOrder(pluginName);
    }

    /// <summary>执行插件异步撤销链 — 按逆序 await DisposeAsync(在同步撤销链之前执行)</summary>
    private async Task ExecutePluginAsyncUndoChainAsync(string pluginName, CancellationToken ct)
    {
        if (_pluginAsyncUndoChain.TryRemove(pluginName, out var chain))
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                try { await chain[i].DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger?.LogWarning(ex, "插件 {PluginName} async 撤销链第 {Index} 项失败", pluginName, i); }
            }
        }
    }

    /// <summary>
    /// 连带卸载依赖方 — 对齐 Cordis Theorem 63:
    /// 卸载插件 A 时,先找到所有声明依赖 A 的插件 B,递归卸载 B,最后卸载 A
    /// </summary>
    private async Task CascadeUnloadDependentsAsync(string pluginName, CancellationToken cancellationToken)
    {
        var dependents = FindDependentPlugins(pluginName);
        foreach (var dependent in dependents)
        {
            if (_workflowPlugins.ContainsKey(dependent))
            {
                _logger?.LogInformation("连带卸载依赖插件: {Dependent} (依赖 {Plugin})", dependent, pluginName);
                await UnloadPluginAsync(dependent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>找到所有声明依赖指定插件的插件名</summary>
    private List<string> FindDependentPlugins(string pluginName)
    {
        var dependents = new List<string>();
        foreach (var kv in _workflowPlugins)
        {
            if (kv.Value.Plugin is IPluginDependencies deps)
            {
                foreach (var dep in deps.Dependencies)
                {
                    if (string.Equals(dep, pluginName, StringComparison.OrdinalIgnoreCase))
                    {
                        dependents.Add(kv.Key);
                        break;
                    }
                }
            }
        }
        return dependents;
    }

    private PluginUnloadResult UnloadWorkflowPlugin(WorkflowPluginHost host)
    {
        try
        {
            var result = host.Unload();
            host.Dispose();

            if (result.IsSuccess)
            {
                _logger?.LogInformation("工作流插件卸载成功: {PluginName}", host.PluginName);
                return PluginUnloadResult.Success(host.PluginName, TimeSpan.Zero);
            }

            _logger?.LogWarning("工作流插件卸载返回失败: {PluginName}, {Error}", host.PluginName, result.ErrorMessage);
            return PluginUnloadResult.AlcUnloadFailed(host.PluginName, TimeSpan.Zero, result.ErrorMessage ?? "未知错误");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "卸载工作流插件时发生异常: {PluginName}", host.PluginName);
            return PluginUnloadResult.AlcUnloadFailed(host.PluginName, TimeSpan.Zero, ex.Message);
        }
    }

    /// <summary>
    /// 阶段1 — Prepare: 让引用方放弃引用,等引用计数归零
    /// <para>通过 IResourceReferenceGraph.GetConsumers 找引用方,通知放弃引用</para>
    /// <para>等待引用计数归零(带超时),超时则返回 false 回退强制卸载</para>
    /// </summary>
    private async Task<bool> PrepareUnloadAsync(string pluginName, CancellationToken cancellationToken)
    {
        var graph = ReferenceGraph;
        if (graph is null) return true;

        var consumers = graph.GetConsumers(pluginName);
        if (consumers.Count == 0) return true;

        foreach (var consumer in consumers)
        {
            var refs = graph.GetReferencesBy(consumer)
                .Where(r => string.Equals(r.TargetPluginName, pluginName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var r in refs)
            {
                graph.RemoveReference(r.ConsumerResourceId, r.TargetResourceId);
            }
            _logger?.LogDebug("通知插件 {Consumer} 放弃对 {Plugin} 的资源引用", consumer, pluginName);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var refCounts = graph.GetReferenceCounts(pluginName);
            if (refCounts.Values.All(c => c == 0)) return true;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        _logger?.LogWarning("插件 {Plugin} Prepare 阶段超时,引用计数未归零,回退强制卸载", pluginName);
        return true;
    }

    /// <summary>
    /// 阶段3 — Verify: 卸载后扫描检查资源是否正确注销
    /// </summary>
    private void ScanAfterUnload(string pluginName)
    {
        if (!_pluginResourceIds.TryRemove(pluginName, out var resourceIds)) return;
        var report = ResourceScanner.ScanPluginResources(pluginName, resourceIds);
        if (report.HasLeaks)
        {
            _blacklistedPlugins.TryAdd(pluginName, 0);
            _logger?.LogError("插件 {Plugin} 卸载后有 {Count} 个资源泄漏,已加入黑名单,拒绝再次加载",
                pluginName, report.LeakedResourceIds.Count);
        }
    }

    /// <summary>
    /// 广播 UI 资源变更事件 — 通知前端刷新界面
    /// </summary>
    private async Task BroadcastUiResourceChangeAsync(string pluginName, WorkflowPluginHost host)
    {
        var bus = EventBus;
        if (bus is null) return;
        if (host.Plugin is not WorkflowPluginBase pluginBase) return;
        if (pluginBase.UiResources.Count == 0) return;

        var evt = pluginBase.UiResources.ClearAndEmitEvent(pluginName);
        var appEvent = AppEvent.Create(
            ServiceMessageType.Notification,
            "UiResourceChanged",
            evt,
            pluginName);
        await bus.PublishAsync(appEvent).ConfigureAwait(false);
    }

    /// <summary>记录插件资源 ObjectId — 加载时调用,用于卸载后扫描验证</summary>
    internal void RecordPluginResourceIds(string pluginName, IEnumerable<ObjectId> resourceIds)
    {
        _pluginResourceIds[pluginName] = resourceIds.ToList();
    }

    #endregion

    #region Plugin Services Cleanup

    private async Task CleanupPluginServicesAsync(string pluginName, CancellationToken ct)
    {
        PluginUnloading?.Invoke(this, pluginName);

        if (HookInjector != null)
        {
            await HookInjector.RemoveHooksAsync(pluginName, ct).ConfigureAwait(false);
        }

        if (CommandRegistry != null)
        {
            var commands = CommandRegistry.GetRegisteredCommands().Where(c => c.PluginName == pluginName).ToList();
            await Task.WhenAll(commands.Select(cmd => CommandRegistry.UnregisterCommandAsync(cmd.CommandName, ct))).ConfigureAwait(false);
        }
    }

    public async Task StartHotReloadAsync(string pluginDirectory, CancellationToken ct = default)
    {
        if (HotReloader != null && !HotReloader.IsWatching)
        {
            await HotReloader.StartWatchingAsync(pluginDirectory, ct).ConfigureAwait(false);
        }
    }

    #endregion

    #region Query

    public bool IsPluginLoaded(string pluginName)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        return _workflowPlugins.ContainsKey(pluginName) || _externalPlugins.ContainsKey(pluginName);
    }

    public bool IsWorkflowPluginLoaded(string pluginName) => _workflowPlugins.ContainsKey(pluginName);
    public bool IsExternalPluginLoaded(string pluginName) => _externalPlugins.ContainsKey(pluginName);

    #endregion

    private void RecordPluginMetrics(string kind, string operation, bool isSuccess) =>
        _telemetryService?.RecordCount("plugin.operation.count", new Dictionary<string, string> { ["kind"] = kind, ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Plugin operation count");

    protected override void OnDispose()
    {
        var externalPluginNames = _externalPlugins.Keys.ToList();
        foreach (var pluginName in externalPluginNames)
        {
            if (_externalPlugins.TryRemove(pluginName, out var host))
            {
                try
                {
                    host.Unload();
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "释放外部插件时出错: {PluginName}", pluginName);
                }
            }
        }

        _externalPlugins.Clear();

        var workflowPluginNames = GetLoadOrderReversed();

        foreach (var pluginName in workflowPluginNames)
        {
            if (_workflowPlugins.TryRemove(pluginName, out var host))
            {
                try
                {
                    ExecutePluginUndoChain(pluginName);
                    host.Unload();
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "释放工作流插件时出错: {PluginName}", pluginName);
                }
            }
        }

        _workflowPlugins.Clear();
    }
}
