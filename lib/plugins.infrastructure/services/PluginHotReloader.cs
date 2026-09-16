namespace Core.Plugins;

/// <summary>
/// 插件热重载服务接口 — 监控插件目录变更，自动卸载并重新加载插件
/// </summary>
public interface IPluginHotReloader : IAsyncDisposable
{
    /// <summary>启动对指定插件目录的监控</summary>
    Task StartWatchingAsync(string pluginDirectory, CancellationToken ct = default);
    /// <summary>停止监控</summary>
    Task StopWatchingAsync(CancellationToken ct = default);
    /// <summary>是否正在监控</summary>
    bool IsWatching { get; }
    /// <summary>插件重载开始事件</summary>
    event EventHandler<PluginReloadEventArgs>? PluginReloading;
    /// <summary>插件重载完成事件</summary>
    event EventHandler<PluginReloadEventArgs>? PluginReloaded;
}

/// <summary>
/// 插件重载事件参数 — 包含插件名称、路径和重载原因
/// </summary>
public sealed partial class PluginReloadEventArgs : EventArgs
{
    /// <summary>插件名称</summary>
    public required string PluginName { get; init; }
    /// <summary>插件文件路径</summary>
    public required string PluginPath { get; init; }
    /// <summary>重载原因</summary>
    public required ReloadReason Reason { get; init; }
}

/// <summary>
/// 插件重载原因 — 文件变更、创建、删除或手动触发
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReloadReason>))]
public enum ReloadReason {
    /// <summary>文件内容变更</summary>
    [EnumValue("fileChanged")]
    FileChanged,
    /// <summary>文件创建</summary>
    [EnumValue("fileCreated")]
    FileCreated,
    /// <summary>文件删除</summary>
    [EnumValue("fileDeleted")]
    FileDeleted,
    /// <summary>手动触发</summary>
    [EnumValue("manual")]
    Manual,
}

/// <summary>
/// PluginHotReloader Actor 命令 — Channel 中的消息类型
/// </summary>
public interface IPluginReloadCommand;

internal sealed record ReloadPluginCmd(string PluginName, string FilePath, ReloadReason Reason) : IPluginReloadCommand;
internal sealed record ReloadPluginAndWaitCmd(string PluginName, string FilePath, ReloadReason Reason, TaskCompletionSource Tcs) : IPluginReloadCommand;
internal sealed record StartWatchingCmd(string PluginDirectory, CancellationToken Ct, TaskCompletionSource Tcs) : IPluginReloadCommand;
internal sealed record StopWatchingCmd(CancellationToken Ct, TaskCompletionSource Tcs) : IPluginReloadCommand;

/// <summary>
/// 插件热重载服务 — Actor 化：继承 ActorBase，Consumer 线程独占 _watcher 和重载逻辑，
/// 消除 AsyncLock。插件加载/卸载（可能 >5s）由 Consumer 串行执行，不再阻塞文件 watcher 事件。
/// 文件 watcher 事件通过 TrySend fire-and-forget 投递，不阻塞 watcher 线程。
/// </summary>
[Register(typeof(IPluginHotReloader), ServiceLifetime.Singleton)]
public sealed partial class PluginHotReloader : ActorBase<IPluginReloadCommand, Unit>, IPluginHotReloader
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginHotReloader>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private IFileSystemWatcher? _watcher;
    private volatile int _isWatchingInt;

    /// <summary>
    /// 构造插件热重载服务
    /// </summary>
    /// <param name="pluginManager">插件管理器</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志器</param>
    /// <param name="telemetryService">可选遥测服务</param>
    public PluginHotReloader(
        IPluginManager pluginManager,
        IFileSystem fs,
        ILogger<PluginHotReloader>? logger = null,
        ITelemetryService? telemetryService = null)
        : base()
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <summary>是否正在监控</summary>
    public bool IsWatching => _isWatchingInt != 0;

    /// <summary>插件重载开始事件</summary>
    public event EventHandler<PluginReloadEventArgs>? PluginReloading;
    /// <summary>插件重载完成事件</summary>
    public event EventHandler<PluginReloadEventArgs>? PluginReloaded;

    /// <summary>
    /// 启动监控 — 发命令到 Consumer，由 Consumer 线程设置 _watcher 和 _isWatching。
    /// </summary>
    public async Task StartWatchingAsync(string pluginDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        if (IsWatching)
        {
            _logger?.LogWarning("[PluginHotReloader] 已在监控中，忽略重复启动请求");
            return;
        }

        if (!_fs.DirectoryExists(pluginDirectory))
        {
            throw new DirectoryNotFoundException(PluginErrors.DirectoryNotFound(pluginDirectory));
        }

        var tcs = CreateTcs();
        await SendAsync(new StartWatchingCmd(pluginDirectory, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 停止监控 — 发命令到 Consumer，由 Consumer 线程释放 _watcher 和清除 _isWatching。
    /// </summary>
    public async Task StopWatchingAsync(CancellationToken ct = default)
    {
        if (!IsWatching)
        {
            return;
        }

        var tcs = CreateTcs();
        await SendAsync(new StopWatchingCmd(ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        TrySend(new ReloadPluginCmd(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileChanged));
    }

    private void OnFileCreated(object? sender, FileChangedEventArgs e)
    {
        TrySend(new ReloadPluginCmd(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileCreated));
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
        TrySend(new ReloadPluginCmd(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileDeleted));
    }

    /// <summary>
    /// 手动触发重载 — 发命令到 Consumer，等待处理完成。
    /// </summary>
    internal async Task ReloadPluginAsync(string pluginName, string filePath, ReloadReason reason)
    {
        var tcs = CreateTcs();
        await SendAsync(new ReloadPluginAndWaitCmd(pluginName, filePath, reason, tcs), CancellationToken.None).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Actor Consumer — 线程独占 _watcher 和重载逻辑，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(IPluginReloadCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case StartWatchingCmd cmd:
            {
                if (_isWatchingInt != 0)
                {
                    cmd.Tcs.TrySetResult();
                    break;
                }

                _watcher = _fs.Watch(cmd.PluginDirectory);
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                _watcher.Filter = "*.*";

                _watcher.DebouncedChanged += OnFileChanged;
                _watcher.DebouncedCreated += OnFileCreated;
                _watcher.DebouncedDeleted += OnFileDeleted;
                _watcher.EnableRaisingEvents = true;

                _isWatchingInt = 1;

                _logger?.LogInformation("[PluginHotReloader] 开始监控插件目录: {Directory}", cmd.PluginDirectory);
                cmd.Tcs.TrySetResult();
                break;
            }

            case StopWatchingCmd cmd:
            {
                if (_isWatchingInt == 0)
                {
                    cmd.Tcs.TrySetResult();
                    break;
                }

                StopWatcherCore();
                cmd.Tcs.TrySetResult();
                break;
            }

            case ReloadPluginCmd cmd:
            {
                await ReloadPluginCoreAsync(cmd.PluginName, cmd.FilePath, cmd.Reason).ConfigureAwait(false);
                break;
            }

            case ReloadPluginAndWaitCmd cmd:
            {
                await ReloadPluginCoreAsync(cmd.PluginName, cmd.FilePath, cmd.Reason).ConfigureAwait(false);
                cmd.Tcs.TrySetResult();
                break;
            }
        }
    }

    /// <summary>
    /// 重载插件核心逻辑 — 由 Consumer 线程独占调用，无需锁。
    /// </summary>
    private async Task ReloadPluginCoreAsync(string pluginName, string filePath, ReloadReason reason)
    {
        var args = new PluginReloadEventArgs
        {
            PluginName = pluginName,
            PluginPath = filePath,
            Reason = reason
        };

        NotifyReloading(args);

        _logger?.LogInformation("[PluginHotReloader] 重载插件: {Plugin}, 原因: {Reason}", pluginName, reason);

        if (_pluginManager.IsPluginLoaded(pluginName))
        {
            await _pluginManager.UnloadPluginAsync(pluginName, CancellationToken.None).ConfigureAwait(false);
        }

        if (reason != ReloadReason.FileDeleted && _fs.FileExists(filePath))
        {
            try
            {
                await _pluginManager.LoadExternalPluginAsync(filePath, pluginName, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PluginHotReloader] 重载插件 '{Plugin}' 失败", pluginName);
            }
        }

        NotifyReloaded(args);

        _telemetryService?.RecordCount("plugin.hotreload.count", new Dictionary<string, string> { ["reason"] = reason.ToString(), ["success"] = true.ToString() }, "count", "Plugin hot reload count");
    }

    private void StopWatcherCore()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _isWatchingInt = 0;
        _logger?.LogInformation("[PluginHotReloader] 已停止监控");
    }

    /// <summary>
    /// 触发 PluginReloading 事件 — 逐订阅者隔离异常，单个订阅者失败不中断重载链
    /// </summary>
    private void NotifyReloading(PluginReloadEventArgs args)
    {
        RaiseEventIsolated(PluginReloading, "PluginReloading", args);
    }

    /// <summary>
    /// 触发 PluginReloaded 事件 — 逐订阅者隔离异常，单个订阅者失败不影响其他订阅者
    /// </summary>
    private void NotifyReloaded(PluginReloadEventArgs args)
    {
        RaiseEventIsolated(PluginReloaded, "PluginReloaded", args);
    }

    /// <summary>
    /// 快照订阅者并逐个调用，隔离每个订阅者的异常（对齐 ThreadSafeListenerList.Notify 约定）
    /// </summary>
    private void RaiseEventIsolated(EventHandler<PluginReloadEventArgs>? handler, string eventName, PluginReloadEventArgs args)
    {
        if (handler is null) return;

        foreach (EventHandler<PluginReloadEventArgs> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PluginHotReloader] {EventName} 订阅者抛异常，已隔离: {Plugin}", eventName, args.PluginName);
            }
        }
    }

    /// <summary>
    /// Actor Consumer 异常回调 — 记录日志
    /// </summary>
    /// <param name="ex">Consumer 抛出的异常</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[PluginHotReloader] Actor Consumer 异常");
    }

    /// <summary>
    /// 异步释放 — 停止监控并释放资源
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        var baseTask = base.DisposeAsync();
        StopWatcherCore();
        return baseTask;
    }
}
