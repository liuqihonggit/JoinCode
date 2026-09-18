
namespace Core.Plugins;

/// <summary>
/// 插件管理器 — 基于 Actor 模型串行处理插件加载/卸载，管理工作流插件、外部进程插件和 native DLL 插件的生命周期
/// </summary>
[Register(typeof(IPluginManager), ServiceLifetime.Singleton)]
public partial class PluginManager : ActorBase<PluginManagerCommand, PluginManagerOutput>, IPluginManager
{
    private readonly ConcurrentDictionary<string, IPluginHost> _plugins = new();
    private readonly IChatClient? _kernel;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IFileOperationService? _fileOperationService;
    private readonly ICommandRegistry? _commandRegistry;
    private readonly ILogger<PluginManager>? _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private volatile bool _isDisposed;
    private IPluginHotReloader? _hotReloader;
    private IPluginHookInjector? _hookInjector;
    private IPluginCommandRegistry? _pluginCommandRegistry;
    private IPluginAgentLoader? _pluginAgentLoader;

    /// <summary>插件生命周期跟踪器 — 撤销链+加载顺序（Consumer 线程独占）</summary>
    private readonly PluginLifecycleTracker _lifecycleTracker;

    /// <summary>每个插件的资源 ObjectId 列表 — 卸载后用于扫描验证</summary>
    private readonly ConcurrentDictionary<string, List<ObjectId>> _pluginResourceIds = new();

    /// <summary>插件黑名单 — 卸载泄漏的插件加入,拒绝再次加载(方案B C4)</summary>
    private readonly ConcurrentDictionary<string, byte> _blacklistedPlugins = new();

    /// <summary>插件依赖图 — 动态拓扑解析(ADR 0098 维度11整合)</summary>
    private readonly PluginDependencyGraph _dependencyGraph = new();

    private IResourceReferenceGraph? _referenceGraph;
    private PluginResourceScanner? _resourceScanner;
    private IAppEventBus? _eventBus;

    private IResourceReferenceGraph? ReferenceGraph => _referenceGraph ??= _serviceProvider?.GetService<IResourceReferenceGraph>();
    private PluginResourceScanner ResourceScanner => _resourceScanner ??= new(_loggerFactory?.CreateLogger<PluginResourceScanner>());
    private IAppEventBus? EventBus => _eventBus ??= _serviceProvider?.GetService<IAppEventBus>();

    /// <summary>插件加载完成事件 — 参数为插件名称</summary>
    public event EventHandler<string>? PluginLoaded;
    /// <summary>插件卸载开始事件 — 参数为插件名称</summary>
    public event EventHandler<string>? PluginUnloading;

    /// <summary>插件诊断事件 — 撤销失败/ALC泄漏等(ADR 0098 维度11)</summary>
    public event EventHandler<PluginDiagnostic>? OnDiagnostic;

    /// <summary>诊断历史记录(Consumer 线程独占)</summary>
    private readonly List<PluginDiagnostic> _diagnostics = new();

    /// <summary>已加载的全部插件名称（工作流 + 外部 + native）</summary>
    public IReadOnlyCollection<string> LoadedPluginNames => (IReadOnlyCollection<string>)_plugins.Keys;

    /// <summary>已加载的工作流插件名称</summary>
    public IReadOnlyCollection<string> LoadedWorkflowPluginNames =>
        _plugins.Where(p => p.Value.PluginType == PluginKind.Workflow).Select(p => p.Key).ToList();
    /// <summary>已加载的外部进程插件名称</summary>
    public IReadOnlyCollection<string> LoadedExternalPluginNames =>
        _plugins.Where(p => p.Value.PluginType == PluginKind.External).Select(p => p.Key).ToList();
    /// <summary>已加载的 native DLL 插件名称</summary>
    public IReadOnlyCollection<string> LoadedNativePluginNames =>
        _plugins.Where(p => p.Value.PluginType == PluginKind.Native).Select(p => p.Key).ToList();

    /// <summary>
    /// 构造插件管理器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="kernel">可选聊天客户端</param>
    /// <param name="loggerFactory">可选日志工厂</param>
    /// <param name="fileOperationService">可选文件操作服务</param>
    /// <param name="commandRegistry">可选命令注册表</param>
    /// <param name="logger">可选日志器</param>
    /// <param name="serviceProvider">可选服务提供者（用于按需获取热重载、钩子注入器等）</param>
    /// <param name="telemetryService">可选遥测服务</param>
    public PluginManager(
        IFileSystem fs,
        IChatClient? kernel = null,
        ILoggerFactory? loggerFactory = null,
        IFileOperationService? fileOperationService = null,
        ICommandRegistry? commandRegistry = null,
        ILogger<PluginManager>? logger = null,
        IServiceProvider? serviceProvider = null,
        ITelemetryService? telemetryService = null)
        : base()
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _kernel = kernel;
        _loggerFactory = loggerFactory;
        _fileOperationService = fileOperationService;
        _commandRegistry = commandRegistry;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _telemetryService = telemetryService;
        _lifecycleTracker = new PluginLifecycleTracker(logger, ReportDiagnostic);
    }

    private IPluginHotReloader? HotReloader => _hotReloader ??= _serviceProvider?.GetService<IPluginHotReloader>();
    private IPluginHookInjector? HookInjector => _hookInjector ??= _serviceProvider?.GetService<IPluginHookInjector>();
    private IPluginCommandRegistry? CommandRegistry => _pluginCommandRegistry ??= _serviceProvider?.GetService<IPluginCommandRegistry>();
    private IPluginAgentLoader? PluginAgentLoader => _pluginAgentLoader ??= _serviceProvider?.GetService<IPluginAgentLoader>();

    #region Internal Workflow Plugin (AOT Compatible)

    /// <summary>
    /// 加载编译时已知的内部工作流插件（AOT兼容）— 通过 Actor mailbox 串行处理
    /// </summary>
    /// <typeparam name="TPlugin">工作流插件类型（须有无参构造）</typeparam>
    /// <param name="ct">取消令牌</param>
    /// <returns>工作流插件宿主</returns>
    public async Task<WorkflowPluginHost> LoadWorkflowPluginAsync<TPlugin>(CancellationToken ct = default) where TPlugin : class, IWorkflowPlugin, new()
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<WorkflowPluginHost>();
        await SendAsync(new LoadWorkflowCmd(() => new TPlugin(), tcs, ct), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    private async Task<WorkflowPluginHost> LoadWorkflowPluginCoreAsync(IWorkflowPlugin plugin, CancellationToken ct)
    {
        var pluginName = plugin.Name;

        await using var span = _telemetryService?.StartSpan("plugin.load.workflow", TelemetrySpanKind.Server);
        span?.SetTag("plugin", pluginName);
        try
        {
            if (_plugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
            }

            if (_blacklistedPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException(PluginErrors.Blacklisted(pluginName));
            }

            _logger?.LogInformation("正在加载内置工作流插件: {PluginName}", pluginName);

            if (plugin is WorkflowPluginBase wpbLoad)
            {
                wpbLoad.Fiber.TransitionTo(PluginFiberState.Activating);
            }

            var host = new WorkflowPluginHost(plugin, _kernel, _loggerFactory, _fileOperationService, _commandRegistry, _logger, _serviceProvider);

            var loadResult = await host.LoadAsync(ct).ConfigureAwait(false);
            if (!loadResult.Success)
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException(PluginErrors.LoadFailed(pluginName, loadResult.ErrorMessage ?? "未知"));
            }

            var initResult = await host.InitializeAsync(ct).ConfigureAwait(false);
            if (!initResult.Success)
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Unload();
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException(PluginErrors.InitializeFailed(pluginName, initResult.ErrorMessage ?? "未知"));
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
                        PluginErrors.ContractViolation(pluginName, contract.Reason ?? "未知"));
                }
            }

            if (!_plugins.TryAdd(pluginName, host))
            {
                if (plugin is WorkflowPluginBase wpbFail) wpbFail.Fiber.TransitionTo(PluginFiberState.Failed);
                host.Unload();
                host.Dispose();
                RecordPluginMetrics("workflow", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
            }

            var undoChain = new List<Action>();

            PluginLoaded?.Invoke(this, pluginName);

            if (plugin is IPluginAgentProvider agentProvider && PluginAgentLoader is not null)
            {
                var undo = PluginAgentLoader.LoadFromPlugin(pluginName, agentProvider);
                undoChain.Add(undo);
            }

            _lifecycleTracker.RegisterUndoChain(pluginName, undoChain, host.Context?.GetAsyncUndoChain().ToList());
            _lifecycleTracker.AddToLoadOrder(pluginName);

            if (plugin is WorkflowPluginBase pluginBase)
            {
                RecordPluginResourceIds(pluginName, pluginBase.Resources.Select(r => r.ObjectId));
                pluginBase.Fiber.TransitionTo(PluginFiberState.Active);
            }

            if (plugin is IPluginDependencies deps)
            {
                foreach (var dep in deps.Dependencies)
                {
                    _dependencyGraph.DeclarePluginDependency(pluginName, dep);
                }
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
    }

    /// <summary>
    /// 获取工作流插件宿主
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件宿主，未加载返回 null</returns>
    public WorkflowPluginHost? GetWorkflowPlugin(string pluginName)
    {
        ThrowIfDisposed();
        return _plugins.TryGetValue(pluginName, out var host) && host is WorkflowPluginHost workflowHost
            ? workflowHost
            : null;
    }

    /// <summary>
    /// 获取工作流插件实例（按指定类型转换）
    /// </summary>
    /// <typeparam name="T">目标插件类型</typeparam>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件实例，未加载或类型不匹配返回 null</returns>
    public T? GetWorkflowPlugin<T>(string pluginName) where T : class, IWorkflowPlugin
    {
        ThrowIfDisposed();
        return _plugins.TryGetValue(pluginName, out var host) && host is WorkflowPluginHost workflowHost
            ? workflowHost.Plugin as T
            : null;
    }

    #endregion

    #region External Process Plugin (AOT Compatible)

    /// <summary>
    /// 加载外部 exe 进程插件（AOT兼容，通过 stdio 通信）— 通过 Actor mailbox 串行处理
    /// </summary>
    /// <param name="exePath">可执行文件路径</param>
    /// <param name="pluginName">插件名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>外部插件宿主</returns>
    public async Task<ExternalPluginHost> LoadExternalPluginAsync(
        string exePath,
        string pluginName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<ExternalPluginHost>();
        await SendAsync(new LoadExternalCmd(exePath, pluginName, tcs, ct), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    #region Native DLL Plugin (AOT Compatible, ADR 0099)

    /// <summary>
    /// 加载 native DLL 插件 — 通过 Actor mailbox 串行处理
    /// </summary>
    public async Task<NativePluginHost> LoadNativePluginAsync(
        string dllPath,
        string pluginName,
        string? configJson = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<NativePluginHost>();
        await SendAsync(new LoadNativeCmd(dllPath, pluginName, configJson, tcs, ct), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    private async Task<NativePluginHost> LoadNativePluginCoreAsync(
        string dllPath,
        string pluginName,
        string? configJson,
        CancellationToken ct)
    {
        await using var span = _telemetryService?.StartSpan("plugin.load.native", TelemetrySpanKind.Server);
        span?.SetTag("plugin", pluginName);
        try
        {
            if (_plugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("native", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
            }

            if (_blacklistedPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("native", "load", false);
                throw new InvalidOperationException(PluginErrors.Blacklisted(pluginName));
            }

            var host = new NativePluginHost(dllPath, pluginName, _fs, _logger);
            var loadResult = host.Load(configJson);
            if (!loadResult.IsSuccess)
            {
                RecordPluginMetrics("native", "load", false);
                throw new InvalidOperationException($"[NATIVE-LOAD-FAIL] 插件 {pluginName} 加载失败: {loadResult.ErrorMessage}");
            }

            if (!_plugins.TryAdd(pluginName, host))
            {
                host.Unload();
                RecordPluginMetrics("native", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
            }

            _lifecycleTracker.AddToLoadOrder(pluginName);
            RecordPluginMetrics("native", "load", true);
            _logger?.LogInformation("Native 插件 {PluginName} 已加载", pluginName);
            PluginLoaded?.Invoke(this, pluginName);
            return host;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            RecordPluginMetrics("native", "load", false);
            _logger?.LogError(ex, "加载 native 插件 {PluginName} 失败", pluginName);
            throw;
        }
    }

    /// <summary>
    /// 获取 native DLL 插件宿主
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件宿主，未加载返回 null</returns>
    public NativePluginHost? GetNativePlugin(string pluginName)
    {
        return _plugins.TryGetValue(pluginName, out var host) && host is NativePluginHost nativeHost
            ? nativeHost
            : null;
    }

    #endregion

    private async Task<ExternalPluginHost> LoadExternalPluginCoreAsync(
        string exePath,
        string pluginName,
        CancellationToken ct)
    {
        await using var span = _telemetryService?.StartSpan("plugin.load.external", TelemetrySpanKind.Server);
        span?.SetTag("plugin", pluginName);
        try
        {
            if (_plugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
            }

            if (_blacklistedPlugins.ContainsKey(pluginName))
            {
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException(PluginErrors.Blacklisted(pluginName));
            }

            if (!_fs.FileExists(exePath))
            {
                RecordPluginMetrics("external", "load", false);
                throw new FileNotFoundException(PluginErrors.ExternalExeNotFound(exePath), exePath);
            }

            _logger?.LogInformation("正在加载外部插件: {PluginName} 从 {ExePath}", pluginName, exePath);

            var utf8NoBom = new UTF8Encoding(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = utf8NoBom,
                StandardErrorEncoding = utf8NoBom,
                StandardInputEncoding = utf8NoBom,
            };

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
                throw new InvalidOperationException(PluginErrors.ExternalProcessStartFailed(exePath));
            }

            process.BeginErrorReadLine();

            var host = new ExternalPluginHost(pluginName, process, exePath, _logger);

            if (!_plugins.TryAdd(pluginName, host))
            {
                host.Dispose();
                RecordPluginMetrics("external", "load", false);
                throw new InvalidOperationException(PluginErrors.AlreadyLoaded(pluginName));
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
    }

    /// <summary>
    /// 获取外部插件宿主
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件宿主，未加载返回 null</returns>
    public ExternalPluginHost? GetExternalPlugin(string pluginName)
    {
        ThrowIfDisposed();
        return _plugins.TryGetValue(pluginName, out var host) && host is ExternalPluginHost externalHost
            ? externalHost
            : null;
    }

    #endregion

    #region Unload

    /// <summary>
    /// 卸载指定插件 — 使用给定的卸载选项（超时、是否强制卸载 ALC）
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="options">卸载选项，null 使用默认</param>
    /// <returns>卸载结果</returns>
    public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, PluginUnloadOptions? options = null)
    {
        var opts = options ?? PluginUnloadOptions.Default;
        using var cts = new CancellationTokenSource(opts.CooperativeTimeout);
        return await UnloadPluginAsync(pluginName, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 卸载指定插件 — 使用给定的取消令牌控制协作卸载
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>卸载结果</returns>
    public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken ct)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<PluginUnloadResult>();
        await SendAsync(new UnloadCmd(pluginName, tcs, ct), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    private async Task<PluginUnloadResult> UnloadPluginCoreAsync(string pluginName, CancellationToken ct)
    {

        if (!_plugins.TryGetValue(pluginName, out var host))
        {
            await PrepareUnloadAsync(pluginName, ct).ConfigureAwait(false);
            return PluginUnloadResult.AlreadyUnloaded(pluginName);
        }

        // workflow 需要前置: cascade 连带卸载依赖方 + prepare 等待引用计数归零
        // external/native 无需前置,直接卸载
        if (host.PluginType == PluginKind.Workflow)
        {
            await CascadeUnloadDependentsAsync(pluginName, ct).ConfigureAwait(false);
            var prepareResult = await PrepareUnloadAsync(pluginName, ct).ConfigureAwait(false);
            if (!prepareResult) return PluginUnloadResult.CooperativeTimeout(pluginName, TimeSpan.Zero);
        }

        // 重新 TryRemove (cascade 不会卸载 pluginName 本身,但保持与原逻辑一致: 先 TryGetValue 判断类型,再 TryRemove)
        if (!_plugins.TryRemove(pluginName, out host))
        {
            return PluginUnloadResult.AlreadyUnloaded(pluginName);
        }

        switch (host.PluginType)
        {
            case PluginKind.External:
            {
                var externalHost = (ExternalPluginHost)host;
                await CleanupPluginServicesAsync(pluginName, ct).ConfigureAwait(false);
                var result = externalHost.Unload();
                externalHost.Dispose();
                if (externalHost.WasForceKilled)
                {
                    _blacklistedPlugins.TryAdd(pluginName, 0);
                    _logger?.LogError("外部插件 {PluginName} 卸载时被强制终止,已加入黑名单,拒绝再次加载", pluginName);
                }
                RecordPluginMetrics("external", "unload", result.IsSuccess);
                return result;
            }
            case PluginKind.Native:
            {
                var nativeHost = (NativePluginHost)host;
                await CleanupPluginServicesAsync(pluginName, ct).ConfigureAwait(false);
                nativeHost.Unload();
                nativeHost.Dispose();
                RecordPluginMetrics("native", "unload", true);
                return PluginUnloadResult.Success(pluginName, TimeSpan.Zero);
            }
            case PluginKind.Workflow:
            {
                var workflowHost = (WorkflowPluginHost)host;
                await ExecutePluginAsyncUndoChainAsync(pluginName, ct).ConfigureAwait(false);
                ExecutePluginUndoChain(pluginName);
                await CleanupPluginServicesAsync(pluginName, ct).ConfigureAwait(false);
                var result = UnloadWorkflowPlugin(workflowHost);
                RecordPluginMetrics("workflow", "unload", result.IsSuccess);

                ScanAfterUnload(pluginName);
                await BroadcastUiResourceChangeAsync(pluginName, workflowHost).ConfigureAwait(false);
                _dependencyGraph.RemovePlugin(pluginName);

                return result;
            }
            default:
                return PluginUnloadResult.AlreadyUnloaded(pluginName);
        }
    }

    /// <summary>
    /// 卸载全部已加载插件 — 通过 Actor mailbox 串行处理
    /// </summary>
    /// <param name="options">卸载选项，null 使用默认</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>每个插件的卸载结果列表</returns>
    public async Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsAsync(PluginUnloadOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<IReadOnlyList<PluginUnloadResult>>();
        await SendAsync(new UnloadAllCmd(tcs, ct), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    private async Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsCoreAsync(CancellationToken ct)
    {
        var results = new List<PluginUnloadResult>();

        // 先卸载 external
        var externalPluginNames = _plugins.Where(p => p.Value.PluginType == PluginKind.External).Select(p => p.Key).ToList();
        foreach (var pluginName in externalPluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is ExternalPluginHost externalHost)
            {
                results.Add(externalHost.Unload());
                externalHost.Dispose();
            }
        }

        // 再卸载 native
        var nativePluginNames = _plugins.Where(p => p.Value.PluginType == PluginKind.Native).Select(p => p.Key).ToList();
        foreach (var pluginName in nativePluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is NativePluginHost nativeHost)
            {
                nativeHost.Unload();
                nativeHost.Dispose();
                results.Add(PluginUnloadResult.Success(pluginName, TimeSpan.Zero));
            }
        }

        // 最后按加载顺序逆序卸载 workflow
        List<string> workflowPluginNames = _lifecycleTracker.GetLoadOrderReversed();

        foreach (var pluginName in workflowPluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is WorkflowPluginHost workflowHost)
            {
                await ExecutePluginAsyncUndoChainAsync(pluginName, ct).ConfigureAwait(false);
                ExecutePluginUndoChain(pluginName);
                await CleanupPluginServicesAsync(pluginName, ct).ConfigureAwait(false);
                results.Add(UnloadWorkflowPlugin(workflowHost));
            }
        }

        return results;
    }

    /// <summary>执行插件撤销链 — 委托给生命周期跟踪器</summary>
    private void ExecutePluginUndoChain(string pluginName)
        => _lifecycleTracker.ExecuteUndoChain(pluginName);

    /// <summary>执行插件异步撤销链 — 委托给生命周期跟踪器</summary>
    private Task ExecutePluginAsyncUndoChainAsync(string pluginName, CancellationToken ct)
        => _lifecycleTracker.ExecuteAsyncUndoChainAsync(pluginName, ct);

    /// <summary>
    /// 连带卸载依赖方 — 对齐 Cordis Theorem 63:
    /// 卸载插件 A 时,先找到所有声明依赖 A 的插件 B,递归卸载 B,最后卸载 A
    /// </summary>
    private async Task CascadeUnloadDependentsAsync(string pluginName, CancellationToken ct)
    {
        var dependents = _dependencyGraph.GetDependents(pluginName);
        foreach (var dependent in dependents)
        {
            if (_plugins.TryGetValue(dependent, out var depHost) && depHost.PluginType == PluginKind.Workflow)
            {
                _logger?.LogInformation("连带卸载依赖插件: {Dependent} (依赖 {Plugin})", dependent, pluginName);
                await UnloadPluginCoreAsync(dependent, ct).ConfigureAwait(false);
            }
        }
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
    private async Task<bool> PrepareUnloadAsync(string pluginName, CancellationToken ct)
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
            await Task.Delay(100, ct).ConfigureAwait(false);
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
            ReportDiagnostic(new PluginDiagnostic
            {
                PluginId = pluginName,
                Kind = PluginDiagnosticKind.AlcLeak,
                Message = $"卸载后有 {report.LeakedResourceIds.Count} 个资源泄漏,已加入黑名单",
                Suggestion = PluginErrors.AlcLeak(pluginName)
            });
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

    /// <summary>测试用: 手动将插件加入黑名单</summary>
    internal void AddToBlacklistForTest(string pluginName) => _blacklistedPlugins.TryAdd(pluginName, 0);

    /// <summary>测试用: 检查插件是否在黑名单中</summary>
    internal bool IsBlacklistedForTest(string pluginName) => _blacklistedPlugins.ContainsKey(pluginName);

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

    /// <summary>
    /// 启动插件热重载监控 — 若热重载服务可用且未在监控则启动
    /// </summary>
    /// <param name="pluginDirectory">插件目录</param>
    /// <param name="ct">取消令牌</param>
    public async Task StartHotReloadAsync(string pluginDirectory, CancellationToken ct = default)
    {
        if (HotReloader != null && !HotReloader.IsWatching)
        {
            await HotReloader.StartWatchingAsync(pluginDirectory, ct).ConfigureAwait(false);
        }
    }

    #endregion

    #region Query

    /// <summary>查询指定名称的插件是否已加载（任意类型）</summary>
    public bool IsPluginLoaded(string pluginName)
    {
        ThrowIfDisposed();
        return _plugins.ContainsKey(pluginName);
    }

    /// <summary>查询指定名称的工作流插件是否已加载</summary>
    public bool IsWorkflowPluginLoaded(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var host) && host.PluginType == PluginKind.Workflow;

    /// <summary>查询指定名称的外部进程插件是否已加载</summary>
    public bool IsExternalPluginLoaded(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var host) && host.PluginType == PluginKind.External;
    /// <summary>查询指定名称的 native DLL 插件是否已加载</summary>
    public bool IsNativePluginLoaded(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var host) && host.PluginType == PluginKind.Native;

    #endregion

    #region Actor Mailbox

    /// <summary>
    /// Actor 命令处理 — Consumer 线程独占,所有可变状态无需锁(ADR 0098)
    /// </summary>
    protected override async ValueTask HandleAsync(PluginManagerCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case LoadWorkflowCmd loadCmd:
                await HandleLoadWorkflowAsync(loadCmd).ConfigureAwait(false);
                break;
            case LoadExternalCmd loadExtCmd:
                await HandleLoadExternalAsync(loadExtCmd).ConfigureAwait(false);
                break;
            case LoadNativeCmd loadNativeCmd:
                await HandleLoadNativeAsync(loadNativeCmd).ConfigureAwait(false);
                break;
            case UnloadCmd unloadCmd:
                await HandleUnloadAsync(unloadCmd).ConfigureAwait(false);
                break;
            case UnloadAllCmd unloadAllCmd:
                await HandleUnloadAllAsync(unloadAllCmd).ConfigureAwait(false);
                break;
            default:
                Console.WriteLine($"[PluginManager] 未知命令类型: {command?.GetType().Name}");
                break;
        }
    }

    private async Task HandleLoadWorkflowAsync(LoadWorkflowCmd cmd)
    {
        try
        {
            var plugin = cmd.PluginFactory();
            var result = await LoadWorkflowPluginCoreAsync(plugin, cmd.CancellationToken).ConfigureAwait(false);
            cmd.Reply.SetResult(result);
        }
        catch (Exception ex) { cmd.Reply.SetException(ex); }
    }

    private async Task HandleLoadExternalAsync(LoadExternalCmd cmd)
    {
        try
        {
            var result = await LoadExternalPluginCoreAsync(cmd.ExePath, cmd.PluginName, cmd.CancellationToken).ConfigureAwait(false);
            cmd.Reply.SetResult(result);
        }
        catch (Exception ex) { cmd.Reply.SetException(ex); }
    }

    private async Task HandleLoadNativeAsync(LoadNativeCmd cmd)
    {
        try
        {
            var result = await LoadNativePluginCoreAsync(cmd.DllPath, cmd.PluginName, cmd.ConfigJson, cmd.CancellationToken).ConfigureAwait(false);
            cmd.Reply.SetResult(result);
        }
        catch (Exception ex) { cmd.Reply.SetException(ex); }
    }

    private async Task HandleUnloadAsync(UnloadCmd cmd)
    {
        try
        {
            var result = await UnloadPluginCoreAsync(cmd.PluginName, cmd.CancellationToken).ConfigureAwait(false);
            cmd.Reply.SetResult(result);
        }
        catch (Exception ex) { cmd.Reply.SetException(ex); }
    }

    private async Task HandleUnloadAllAsync(UnloadAllCmd cmd)
    {
        try
        {
            var result = await UnloadAllPluginsCoreAsync(cmd.CancellationToken).ConfigureAwait(false);
            cmd.Reply.SetResult(result);
        }
        catch (Exception ex) { cmd.Reply.SetException(ex); }
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(PluginManager));
    }

    /// <summary>获取诊断历史记录(线程安全快照)</summary>
    public IReadOnlyList<PluginDiagnostic> GetDiagnostics()
    {
        lock (_diagnostics) return _diagnostics.ToList();
    }

    /// <summary>上报诊断事件(Consumer 线程调用)</summary>
    private void ReportDiagnostic(PluginDiagnostic diagnostic)
    {
        lock (_diagnostics) _diagnostics.Add(diagnostic);
        OnDiagnostic?.Invoke(this, diagnostic);
    }

    private void RecordPluginMetrics(string kind, string operation, bool isSuccess) =>
        _telemetryService?.RecordCount("plugin.operation.count", new Dictionary<string, string> { ["kind"] = kind, ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Plugin operation count");

    /// <summary>
    /// 异步释放 — 标记已释放，调用基类释放并清理全部插件
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        if (_isDisposed) return ValueTask.CompletedTask;
        _isDisposed = true;

        CleanupAllPlugins();
        return base.DisposeAsync();
    }

    private void CleanupAllPlugins()
    {
        // 先清理 external
        var externalPluginNames = _plugins.Where(p => p.Value.PluginType == PluginKind.External).Select(p => p.Key).ToList();
        foreach (var pluginName in externalPluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is ExternalPluginHost externalHost)
            {
                try { externalHost.Unload(); externalHost.Dispose(); }
                catch (Exception ex) { _logger?.LogError(ex, "释放外部插件时出错: {PluginName}", pluginName); }
            }
        }

        // 再清理 native
        var nativePluginNames = _plugins.Where(p => p.Value.PluginType == PluginKind.Native).Select(p => p.Key).ToList();
        foreach (var pluginName in nativePluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is NativePluginHost nativeHost)
            {
                try { nativeHost.Unload(); nativeHost.Dispose(); }
                catch (Exception ex) { _logger?.LogError(ex, "释放 native 插件时出错: {PluginName}", pluginName); }
            }
        }

        // 最后按加载顺序逆序清理 workflow
        var workflowPluginNames = _lifecycleTracker.GetLoadOrderReversed();
        foreach (var pluginName in workflowPluginNames)
        {
            if (_plugins.TryRemove(pluginName, out var host) && host is WorkflowPluginHost workflowHost)
            {
                try { ExecutePluginUndoChain(pluginName); workflowHost.Unload(); workflowHost.Dispose(); }
                catch (Exception ex) { _logger?.LogError(ex, "释放工作流插件时出错: {PluginName}", pluginName); }
            }
        }

        _plugins.Clear();
    }

    /// <summary>
    /// Actor Consumer 异常回调 — 记录日志
    /// </summary>
    /// <param name="ex">Consumer 抛出的异常</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "PluginManager Actor Consumer 异常");
    }
}
#pragma warning restore JCC9102
