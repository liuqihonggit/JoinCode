namespace Services.Lsp.Internal;

/// <summary>
/// LSP 管理器接口 — 统一管理多个 LSP 服务器实例的生命周期和文件操作
/// </summary>
public interface ILspManager : IAsyncDisposable
{
    /// <summary>获取管理器是否已初始化</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 初始化 LSP 管理器 — 注册所有配置的 LSP 服务器实例
    /// </summary>
    /// <param name="configs">LSP 实例配置列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InitializeAsync(IEnumerable<LspInstanceConfig> configs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭所有 LSP 服务器并清理状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task ShutdownAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据文件路径获取对应的 LSP 服务器实例
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>匹配的服务器实例，无匹配时返回 null</returns>
    ILspServerInstance? GetServerForFile(string filePath);

    /// <summary>
    /// 确保文件对应的服务器已启动，未启动则启动之
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已启动的服务器实例，无匹配或启动失败时返回 null</returns>
    Task<ILspServerInstance?> EnsureServerStartedAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向文件对应的服务器发送 LSP 请求
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="method">LSP 方法名</param>
    /// <param name="params">请求参数对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 JSON 节点，失败时返回 null</returns>
    Task<JsonNode?> SendRequestAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向文件对应的服务器发送 LSP 通知
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="method">LSP 方法名</param>
    /// <param name="params">通知参数对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendNotificationAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知服务器文件已打开 — 发送 textDocument/didOpen
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">文件内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OpenFileAsync(string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知服务器文件内容已变更 — 发送 textDocument/didChange
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">变更后的完整内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ChangeFileAsync(string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知服务器文件已保存 — 发送 textDocument/didSave
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知服务器文件已关闭 — 发送 textDocument/didClose
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task CloseFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件是否处于打开状态
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>已打开返回 true，否则 false</returns>
    bool IsFileOpen(string filePath);

    /// <summary>
    /// 获取所有已注册的 LSP 服务器实例快照
    /// </summary>
    /// <returns>服务器名到实例的只读字典</returns>
    IReadOnlyDictionary<string, ILspServerInstance> GetAllServers();
}

/// <summary>
/// LSP 管理器实现 — 管理多个 LSP 服务器实例的注册、启动、文件追踪和消息分发
/// </summary>
[Register(typeof(ILspManager), ServiceLifetime.Singleton)]
public sealed partial class LspManager : ServiceEntity, ILspManager
{

    /// <summary>
    /// 构造 LSP 管理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程服务抽象</param>
    /// <param name="fileOperationService">可选的文件操作服务</param>
    /// <param name="passiveFeedback">可选的被动反馈处理器</param>
    public LspManager(ILogger<LspManager> logger, IFileSystem fs, IProcessService processService, IFileOperationService? fileOperationService = null, ILspPassiveFeedback? passiveFeedback = null)
        : base(nameof(LspManager))
    {
        _logger = logger;
        _fs = fs;
        _processService = processService;
        _fileOperationService = fileOperationService;
        _passiveFeedback = passiveFeedback;
        _initActor = new LspInitActor(this, _logger);
    }
    private const int MaxLspFileSizeBytes = 10_000_000;

    private readonly LspServerRegistry _registry = new();
    private readonly ConcurrentDictionary<string, string> _openedFiles = new();
    private readonly ILogger<LspManager> _logger;
    private readonly IFileOperationService? _fileOperationService;
    private readonly ILspPassiveFeedback? _passiveFeedback;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly LspInitActor _initActor;
    private int _isInitialized;
    private int _asyncDisposed;

    /// <summary>获取管理器是否已初始化</summary>
    public bool IsInitialized => Volatile.Read(ref _isInitialized) == 1;

    /// <summary>
    /// 初始化 LSP 管理器 — 通过 Actor 序列化 Initialize/Shutdown 调用
    /// </summary>
    /// <param name="configs">LSP 实例配置列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InitializeAsync(IEnumerable<LspInstanceConfig> configs, CancellationToken cancellationToken = default)
    {
        await _initActor.InitializeAsync(configs.ToList(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 初始化核心逻辑 — 注册所有配置的 LSP 服务器实例并建立扩展名映射
    /// </summary>
    /// <param name="configs">LSP 实例配置列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task InitializeCoreAsync(List<LspInstanceConfig> configs, CancellationToken cancellationToken)
    {
        if (IsInitialized) return;

        foreach (var config in configs)
        {
            var instance = new LspServerInstance(config, _fs, _processService, _logger);

            _registry.Register(config.Name, instance, config.ExtensionToLanguage);

            _logger.LogInformation("Registered LSP server: {Name} ({LanguageId}) for extensions: {Extensions}",
                config.Name, config.LanguageId, string.Join(", ", config.ExtensionToLanguage.Keys));
        }

        Volatile.Write(ref _isInitialized, 1);
        _logger.LogInformation("LSP Manager initialized with {Count} server(s)", _registry.Count);

        if (_passiveFeedback != null)
        {
            _passiveFeedback.RegisterNotificationHandlers(this);
        }
    }

    /// <summary>
    /// 关闭所有 LSP 服务器 — 通过 Actor 序列化调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _initActor.ShutdownAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭核心逻辑 — 停止所有 LSP 服务器并清空注册表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsInitialized) return;

        var tasks = _registry.Servers.Select(s => s.StopAsync(cancellationToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        _registry.Clear();
        _openedFiles.Clear();
        Volatile.Write(ref _isInitialized, 0);
    }

    /// <summary>
    /// 根据文件扩展名获取对应的 LSP 服务器实例
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>匹配的服务器实例，无匹配时返回 null</returns>
    public ILspServerInstance? GetServerForFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return _registry.TryGetByExtension(ext, out var instance) ? instance : null;
    }

    /// <summary>
    /// 确保文件对应的服务器已启动 — 未启动则解析工作区根并启动
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已启动的服务器实例，无匹配或启动失败时返回 null</returns>
    public async Task<ILspServerInstance?> EnsureServerStartedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null) return null;

        if (server.IsHealthy) return server;

        try
        {
            var workspaceRoot = await GitWorkspaceResolver.FindWorkspaceRootAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
            await server.StartAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            return server;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start LSP server '{Name}' for file: {FilePath}", server.Name, filePath);
            return null;
        }
    }

    /// <summary>
    /// 向文件对应的服务器发送 LSP 请求
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="method">LSP 方法名</param>
    /// <param name="params">请求参数对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 JSON 节点，失败时返回 null</returns>
    public async Task<JsonNode?> SendRequestAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (server == null) return null;

        return await server.SendRequestAsync(method, @params, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 向文件对应的服务器发送 LSP 通知
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="method">LSP 方法名</param>
    /// <param name="params">通知参数对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SendNotificationAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (server == null) return;

        await server.SendNotificationAsync(method, @params, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 通知服务器文件已打开 — 发送 textDocument/didOpen
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">文件内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task OpenFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (server == null) return;

        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        if (_openedFiles.TryGetValue(fileUri, out var existingServer) && existingServer == server.Name)
        {
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var languageId = server.Config.ExtensionToLanguage.TryGetValue(ext, out var lang) ? lang : "plaintext";

        var didOpenParams = new LspDidOpenTextDocumentParams
        {
            TextDocument = new LspTextDocumentItem
            {
                Uri = fileUri,
                LanguageId = languageId,
                Version = 1,
                Text = content
            }
        };

        await server.SendNotificationAsync("textDocument/didOpen", didOpenParams, cancellationToken).ConfigureAwait(false);
        _openedFiles[fileUri] = server.Name;
    }

    /// <summary>
    /// 通知服务器文件内容已变更 — 发送 textDocument/didChange，未打开则自动调用 OpenFileAsync
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">变更后的完整内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ChangeFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null || !server.IsHealthy)
        {
            await OpenFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        if (_openedFiles.TryGetValue(fileUri, out var existingServer) && existingServer != server.Name)
        {
            await OpenFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_openedFiles.ContainsKey(fileUri))
        {
            await OpenFileAsync(filePath, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        var changeParams = new Dictionary<string, JsonElement>
        {
            ["textDocument"] = JsonElementHelper.FromObject(
                new Dictionary<string, JsonElement>
                {
                    ["uri"] = JsonElementHelper.FromString(fileUri),
                    ["version"] = JsonElementHelper.FromInt32(1)
                },
                LspJsonContext.Default.DictionaryStringJsonElement),
            ["contentChanges"] = JsonElementHelper.FromObject(
                new List<Dictionary<string, JsonElement>> { new() { ["text"] = JsonElementHelper.FromString(content) } },
                LspJsonContext.Default.ListDictionaryStringJsonElement)
        };

        await server.SendNotificationAsync("textDocument/didChange", changeParams, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 通知服务器文件已保存 — 发送 textDocument/didSave
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SaveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null || !server.IsHealthy) return;

        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        if (!_openedFiles.ContainsKey(fileUri)) return;

        var saveParams = new Dictionary<string, JsonElement>
        {
            ["textDocument"] = JsonElementHelper.FromObject(
                new Dictionary<string, JsonElement> { ["uri"] = JsonElementHelper.FromString(fileUri) },
                LspJsonContext.Default.DictionaryStringJsonElement)
        };

        await server.SendNotificationAsync("textDocument/didSave", saveParams, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 通知服务器文件已关闭 — 发送 textDocument/didClose 并从打开文件表移除
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task CloseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        if (!_openedFiles.TryRemove(fileUri, out var serverName)) return;

        if (!_registry.TryGetByName(serverName, out var server)) return;
        if (!server.IsHealthy) return;

        var closeParams = new Dictionary<string, JsonElement>
        {
            ["textDocument"] = JsonElementHelper.FromObject(
                new Dictionary<string, JsonElement> { ["uri"] = JsonElementHelper.FromString(fileUri) },
                LspJsonContext.Default.DictionaryStringJsonElement)
        };

        try
        {
            await server.SendNotificationAsync("textDocument/didClose", closeParams, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send didClose for: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// 检查文件是否处于打开状态
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>已打开返回 true，否则 false</returns>
    public bool IsFileOpen(string filePath)
    {
        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        return _openedFiles.ContainsKey(fileUri);
    }

    /// <summary>
    /// 获取所有已注册的 LSP 服务器实例快照
    /// </summary>
    /// <returns>服务器名到实例的只读字典</returns>
    public IReadOnlyDictionary<string, ILspServerInstance> GetAllServers()
    {
        return _registry.Snapshot();
    }

    /// <summary>
    /// 异步释放资源 — 关闭所有 LSP 服务器并清空注册表
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _asyncDisposed, 1) != 0) return;

        var servers = _registry.Servers.ToArray();
        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var server in servers)
            await server.DisposeAsync().ConfigureAwait(false);
        await _initActor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
