
namespace Core.Plugins;

/// <summary>
/// 工作流插件宿主 - 管理 IWorkflowPlugin 的生命周期和服务容器
/// </summary>
public sealed class WorkflowPluginHost : IDisposable
{
    private readonly IServiceCollection _pluginServices;
    private ServiceProvider? _pluginServiceProvider;
    private readonly IWorkflowPlugin _plugin;
    private readonly ILogger? _logger;
    private bool _isDisposed;

    public string PluginName => _plugin.Name;
    public string Version => _plugin.Version;
    public IWorkflowPlugin Plugin => _plugin;

    public WorkflowPluginHost(
        IWorkflowPlugin plugin,
        IChatClient? kernel = null,
        ILoggerFactory? loggerFactory = null,
        IFileOperationService? fileOperationService = null,
        ICommandRegistry? commandRegistry = null,
        ILogger? logger = null)
    {
        _plugin = plugin;
        _logger = logger;

        _pluginServices = new ServiceCollection();

        if (kernel is not null)
            _pluginServices.AddSingleton(kernel);
        if (loggerFactory is not null)
            _pluginServices.AddSingleton(loggerFactory);
        if (fileOperationService is not null)
            _pluginServices.AddSingleton(fileOperationService);
        if (commandRegistry is not null)
            _pluginServices.AddSingleton(commandRegistry);
    }

    /// <summary>
    /// 加载插件 - 调用插件的 Load 方法注册服务
    /// </summary>
    public OperationResult Load()
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        try
        {
            _logger?.LogInformation("正在加载工作流插件: {PluginName} v{Version}", _plugin.Name, _plugin.Version);

            var result = _plugin.Load(_pluginServices);

            if (!result.Success)
            {
                _logger?.LogError("插件 {PluginName} Load 失败: {Error}", _plugin.Name, result.ErrorMessage);
                return result;
            }

            // 构建插件服务容器
            _pluginServiceProvider = _pluginServices.BuildServiceProvider();

            _logger?.LogInformation("工作流插件 {PluginName} 服务注册完成", _plugin.Name);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "加载工作流插件 {PluginName} 时发生异常", _plugin.Name);
            return OperationResult.Fail($"加载异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 初始化插件 - 调用插件的 InitializeAsync 方法
    /// </summary>
    public async Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        if (_pluginServiceProvider == null)
        {
            return OperationResult.Fail("插件服务容器未构建，请先调用 Load()");
        }

        try
        {
            _logger?.LogInformation("正在初始化工作流插件: {PluginName}", _plugin.Name);

            var result = await _plugin.InitializeAsync(_pluginServiceProvider, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger?.LogError("插件 {PluginName} Initialize 失败: {Error}", _plugin.Name, result.ErrorMessage);
                return result;
            }

            // 注册命令钩子
            if (_plugin is ICommandRegistrationHook hook)
            {
                var registry = _pluginServiceProvider.GetService<ICommandRegistry>();
                if (registry != null)
                {
                    hook.RegisterCommands(registry, _pluginServiceProvider);
                    _logger?.LogInformation("插件 {PluginName} 已注册命令钩子", _plugin.Name);
                }
            }

            _logger?.LogInformation("工作流插件 {PluginName} 初始化完成", _plugin.Name);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化工作流插件 {PluginName} 时发生异常", _plugin.Name);
            return OperationResult.Fail($"初始化异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 卸载插件
    /// </summary>
    public PluginUnloadResult Unload()
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        try
        {
            _logger?.LogInformation("正在卸载工作流插件: {PluginName}", _plugin.Name);

            var result = _plugin.Unload();

            if (_pluginServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
                _pluginServiceProvider = null;
            }

            _logger?.LogInformation("工作流插件 {PluginName} 卸载完成", _plugin.Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "卸载工作流插件 {PluginName} 时发生异常", _plugin.Name);
            return PluginUnloadResult.AlcUnloadFailed(_plugin.Name, TimeSpan.Zero, $"卸载异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取插件服务
    /// </summary>
    public T? GetService<T>() where T : class
    {
        return _pluginServiceProvider?.GetService<T>();
    }

    /// <summary>
    /// 获取插件服务（必需）
    /// </summary>
    public T GetRequiredService<T>() where T : class
    {
        if (_pluginServiceProvider == null)
        {
            throw new InvalidOperationException("插件服务容器未构建");
        }
        return _pluginServiceProvider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed)) return;

        if (_plugin is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_pluginServiceProvider is IDisposable spDisposable)
        {
            spDisposable.Dispose();
        }
    }
}
