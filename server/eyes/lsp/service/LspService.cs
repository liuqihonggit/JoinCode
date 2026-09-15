namespace Services.Lsp;

/// <summary>
/// LSP 服务 — 封装语言服务器协议操作，提供定义跳转、引用查找、悬停、补全等能力
/// </summary>
[Register(typeof(ILspService), ServiceLifetime.Singleton)]
public sealed partial class LspService : ServiceEntity, ILspService
{
    private const int MaxLspFileSizeBytes = 10_000_000;

    private readonly ILspManager _lspManager;
    private readonly ILspConfigLoader _configLoader;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly ILogger<LspService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly AsyncLock _initLock = new();
    private int _isInitialized;
    private int _asyncDisposed;

    /// <summary>
    /// 初始化 LspService
    /// </summary>
    /// <param name="engineContext">核心引擎依赖（LspManager + ConfigLoader）</param>
    /// <param name="deps">可选依赖（FileOperationService + FileSystem + TelemetryService）</param>
    /// <param name="logger">日志记录器</param>
    public LspService(
        LspEngineContext engineContext,
        LspServiceDeps? deps = null,
        ILogger<LspService>? logger = null)
        : base(nameof(LspService))
    {
        ArgumentNullException.ThrowIfNull(engineContext);
        ArgumentNullException.ThrowIfNull(engineContext.LspManager);
        ArgumentNullException.ThrowIfNull(engineContext.ConfigLoader);

        _lspManager = engineContext.LspManager;
        _configLoader = engineContext.ConfigLoader;
        _fileOperationService = deps?.FileOperationService ?? throw new ArgumentNullException(nameof(deps) + "." + nameof(LspServiceDeps.FileOperationService));
        _fs = deps?.FileSystem ?? throw new ArgumentNullException(nameof(deps) + "." + nameof(LspServiceDeps.FileSystem));
        _logger = logger;
        _telemetryService = deps?.TelemetryService;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _isInitialized) == 1 && _lspManager.IsInitialized) return;

        using var guard = await _initLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_initLock.Name}' 等待超时");

        if (Volatile.Read(ref _isInitialized) == 1 && _lspManager.IsInitialized) return;

        var configEntries = await _configLoader.LoadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var instanceConfigs = configEntries.Select(e => e.ToLspInstanceConfig()).ToList();

        await _lspManager.InitializeAsync(instanceConfigs, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _isInitialized, 1);

        _logger?.LogInformation("LSP Service initialized with {Count} server config(s)", instanceConfigs.Count);
    
    }

    private async Task EnsureFileOpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_lspManager.IsFileOpen(filePath)) return;

        if (!_fileOperationService.FileExists(filePath)) return;

        try
        {
            var fileSize = _fs.GetFileLength(filePath);
            if (fileSize > MaxLspFileSizeBytes)
            {
                _logger?.LogWarning("File too large for LSP analysis: {FilePath} ({Size}MB)", filePath, Math.Ceiling(fileSize / 1_000_000.0));
                RecordLspMetrics("file_too_large");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LSP 文件大小检查失败: {FilePath}", filePath);
        }

        var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (readResult.Success)
        {
            await _lspManager.OpenFileAsync(filePath, readResult.Content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 检查指定文件对应的 LSP 服务器是否可用（已安装且能启动）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器可用返回 true，否则 false</returns>
    public async Task<bool> IsServerAvailableAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var server = await _lspManager.EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        return server is not null;
    }

    /// <summary>
    /// 跳转到定义 — 对齐 LSP textDocument/definition
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>定义位置列表</returns>
    public async Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentDefinition.ToValue(), positionParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("goto_definition");
        return DeserializeLocations(result);
    }

    /// <summary>
    /// 查找引用 — 对齐 LSP textDocument/references
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>引用位置列表</returns>
    public async Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var referenceParams = new LspReferenceParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character },
            Context = new LspReferenceContext { IncludeDeclaration = true }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentReferences.ToValue(), referenceParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("find_references");
        return DeserializeLocations(result);
    }

    /// <summary>
    /// 悬停信息 — 对齐 LSP textDocument/hover
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>悬停结果；无信息返回 null</returns>
    public async Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentHover.ToValue(), positionParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("hover");
        return result != null ? RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspHoverResult) : null;
    }

    /// <summary>
    /// 获取补全项 — 对齐 LSP textDocument/completion
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>补全项列表</returns>
    public async Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentCompletion.ToValue(), positionParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("completions");
        return DeserializeCompletions(result);
    }

    /// <summary>
    /// 获取文档符号 — 对齐 LSP textDocument/documentSymbol
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文档符号列表</returns>
    public async Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var docParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition()
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentDocumentSymbol.ToValue(), docParams, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspDocumentSymbol) ?? [];
        }

        return [];
    }

    /// <summary>
    /// 工作区符号搜索 — 对齐 LSP workspace/symbol
    /// </summary>
    /// <param name="query">搜索查询字符串</param>
    /// <param name="workspacePath">工作区路径（可选，用于自动检测服务器）</param>
    /// <param name="serverName">指定服务器名称（可选，跳过自动检测）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的符号信息列表</returns>
    public async Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, string? workspacePath = null, string? serverName = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var allServers = _lspManager.GetAllServers();
        if (allServers.Count == 0)
        {
            return [];
        }

        ILspServerInstance? server;
        string? workspaceRoot = null;

        if (!string.IsNullOrEmpty(serverName))
        {
            if (!allServers.TryGetValue(serverName, out var namedServer))
            {
                var available = string.Join(", ", allServers.Keys);
                throw new InvalidOperationException($"LSP server '{serverName}' not found. Available servers: {available}");
            }
            server = namedServer;
            workspaceRoot = await GitWorkspaceResolver.FindWorkspaceRootAsync(workspacePath, _fs, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Workspace symbol search: server='{Name}' (explicit), workspace='{Root}'", server.Name, workspaceRoot ?? "(null)");
        }
        else if (!string.IsNullOrEmpty(workspacePath) && _fileOperationService.FileExists(workspacePath))
        {
            server = await _lspManager.EnsureServerStartedAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            if (server == null)
            {
                throw new InvalidOperationException($"No LSP server matches file extension: {Path.GetExtension(workspacePath)}. Available servers: {string.Join(", ", allServers.Keys)}");
            }
            _logger?.LogInformation("Workspace symbol search: server='{Name}' (auto-detected from file), workspace='{Root}'", server.Name, workspacePath);
        }
        else
        {
            server = allServers.Values.FirstOrDefault(s => s.State == LspServerState.Running)
                  ?? allServers.Values.FirstOrDefault();
            if (server == null)
            {
                return [];
            }
            workspaceRoot = await GitWorkspaceResolver.FindWorkspaceRootAsync(workspacePath, _fs, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Workspace symbol search: server='{Name}' (fallback), workspace='{Root}'", server.Name, workspaceRoot ?? "(null)");
        }

        if (server.State != LspServerState.Running)
        {
            return [];
        }

        var symbolParams = new LspWorkspaceSymbolParams { Query = query };
        var result = await server.SendRequestAsync(LspMethod.WorkspaceSymbol.ToValue(), symbolParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("workspace_symbol");
        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspSymbolInformation) ?? [];
        }

        return [];
    }

    /// <summary>
    /// 跳转到实现 — 对齐 LSP textDocument/implementation
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实现位置列表</returns>
    public async Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentImplementation.ToValue(), positionParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("goto_implementation");
        return DeserializeLocations(result);
    }

    /// <summary>
    /// 准备调用层次结构 — 对齐 LSP textDocument/prepareCallHierarchy
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用层次项列表</returns>
    public async Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        await EnsureFileOpenAsync(filePath, cancellationToken).ConfigureAwait(false);

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = LspUriHelper.PathToFileUrl(filePath) },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await _lspManager.SendRequestAsync(filePath, LspMethod.TextDocumentPrepareCallHierarchy.ToValue(), positionParams, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("prepare_call_hierarchy");
        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyItem) ?? [];
        }

        return [];
    }

    /// <summary>
    /// 调用层次 incoming calls — 对齐 LSP callHierarchy/incomingCalls
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>incoming call 列表</returns>
    public async Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var paramObj = new LspCallHierarchyItemParam { Item = item };

        var server = _lspManager.GetAllServers().Values.FirstOrDefault(s => s.IsHealthy);
        if (server == null) return [];

        var result = await server.SendRequestAsync(LspMethod.CallHierarchyIncomingCalls.ToValue(), paramObj, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("incoming_calls");
        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyIncomingCall) ?? [];
        }

        return [];
    }

    /// <summary>
    /// 调用层次 outgoing calls — 对齐 LSP callHierarchy/outgoingCalls
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>outgoing call 列表</returns>
    public async Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var paramObj = new LspCallHierarchyItemParam { Item = item };

        var server = _lspManager.GetAllServers().Values.FirstOrDefault(s => s.IsHealthy);
        if (server == null) return [];

        var result = await server.SendRequestAsync(LspMethod.CallHierarchyOutgoingCalls.ToValue(), paramObj, cancellationToken).ConfigureAwait(false);

        RecordLspMetrics("outgoing_calls");
        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyOutgoingCall) ?? [];
        }

        return [];
    }

    /// <summary>
    /// 关闭文件对应的 LSP 客户端
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task CloseClientAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _lspManager.CloseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放 LspService 资源
    /// </summary>
    /// <returns>表示异步释放操作的 ValueTask</returns>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _asyncDisposed, 1) == 1) return;

        await _lspManager.DisposeAsync().ConfigureAwait(false);
        Dispose();
    }

    /// <summary>
    /// 同步释放资源 — 释放初始化锁
    /// </summary>
    public override void Dispose()
    {
        if (_asyncDisposed == 1) return;
        _initLock.Dispose();
            base.Dispose();
    }

    #region Private Methods

    /// <summary>
    /// 将 LSP definition/references/implementation 响应反序列化为 LspLocation 列表。
    /// 处理格式：null、Location、Location[]、LocationLink、LocationLink[]。
    /// csharp-ls 返回 LocationLink 格式（含 targetUri/targetRange），需转换为 LspLocation。
    /// </summary>
    private static List<LspLocation> DeserializeLocations(JsonNode? result)
    {
        if (result is null) return [];

        if (result is JsonArray arr)
        {
            var locations = new List<LspLocation>();
            foreach (var item in arr)
            {
                var loc = DeserializeSingleLocation(item);
                if (loc != null) locations.Add(loc);
            }
            return locations;
        }

        var single = DeserializeSingleLocation(result);
        return single != null ? [single] : [];
    }

    /// <summary>
    /// 反序列化单个 Location 或 LocationLink 为 LspLocation。
    /// Location: { uri, range }
    /// LocationLink: { targetUri, targetRange, targetSelectionRange, originSelectionRange }
    /// </summary>
    private static LspLocation? DeserializeSingleLocation(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;

        if (obj.TryGetPropertyValue("uri", out var uriNode) && uriNode != null)
        {
            return RelaxedJsonSerializer.Deserialize(node!.ToJsonString(), LspJsonContext.Default.LspLocation);
        }

        if (obj.TryGetPropertyValue("targetUri", out var targetUriNode) && targetUriNode != null)
        {
            var targetUri = targetUriNode.GetValue<string>();
            var rangeNode = obj.TryGetPropertyValue("targetRange", out var tr) ? tr : null;
            if (rangeNode != null)
            {
                var range = RelaxedJsonSerializer.Deserialize(rangeNode.ToJsonString(), LspJsonContext.Default.LspRange);
                return new LspLocation { Uri = targetUri, Range = range ?? new LspRange { Start = new LspPosition(), End = new LspPosition() } };
            }
            return new LspLocation { Uri = targetUri, Range = new LspRange { Start = new LspPosition(), End = new LspPosition() } };
        }

        return null;
    }

    private static List<LspCompletionItem> DeserializeCompletions(JsonNode? result)
    {
        if (result is null) return [];

        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCompletionItem) ?? [];
        }

        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("items", out var itemsNode))
        {
            return RelaxedJsonSerializer.Deserialize(itemsNode?.ToJsonString() ?? "[]", LspJsonContext.Default.ListLspCompletionItem) ?? [];
        }

        return [];
    }

    private void RecordLspMetrics(string operation)
        => _telemetryService?.RecordCount("lsp.operation.count", new Dictionary<string, string> { ["operation"] = operation }, "count", "LSP operation count");

    #endregion
}
