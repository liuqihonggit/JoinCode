namespace Services.Lsp;

[Register]
public sealed partial class LspService : ILspService
{
    private const int MaxLspFileSizeBytes = 10_000_000;

    private readonly ILspManager _lspManager;
    private readonly ILspConfigLoader _configLoader;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<LspService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _isInitialized;
    private int _isDisposed;

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

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _isInitialized) == 1 && _lspManager.IsInitialized) return;

            var configEntries = await _configLoader.LoadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var instanceConfigs = configEntries.Select(e => e.ToLspInstanceConfig()).ToList();

            await _lspManager.InitializeAsync(instanceConfigs, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _isInitialized, 1);

            _logger?.LogInformation("LSP Service initialized with {Count} server config(s)", instanceConfigs.Count);
        }
        finally
        {
            _initLock.Release();
        }
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
            System.Diagnostics.Trace.WriteLine($"LSP file size check failed: {ex.Message}");
        }

        var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (readResult.Success)
        {
            await _lspManager.OpenFileAsync(filePath, readResult.Content, cancellationToken).ConfigureAwait(false);
        }
    }

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
        return result != null ? JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspHoverResult) : null;
    }

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
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspDocumentSymbol) ?? [];
        }

        return [];
    }

    public async Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var symbolParams = new LspWorkspaceSymbolParams { Query = query };

        var server = _lspManager.GetAllServers().Values.FirstOrDefault();
        if (server == null)
        {
            _logger?.LogWarning("No LSP servers available for workspace symbol search");
            return [];
        }

        var result = await server.SendRequestAsync(LspMethod.WorkspaceSymbol.ToValue(), symbolParams, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspSymbolInformation) ?? [];
        }

        return [];
    }

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
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyItem) ?? [];
        }

        return [];
    }

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
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyIncomingCall) ?? [];
        }

        return [];
    }

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
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyOutgoingCall) ?? [];
        }

        return [];
    }

    public async Task CloseClientAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _lspManager.CloseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        await _lspManager.DisposeAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }

    #region Private Methods

    private static List<LspLocation> DeserializeLocations(JsonNode? result)
    {
        if (result is null) return [];

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspLocation) ?? [];
        }

        var single = JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspLocation);
        return single != null ? [single] : [];
    }

    private static List<LspCompletionItem> DeserializeCompletions(JsonNode? result)
    {
        if (result is null) return [];

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCompletionItem) ?? [];
        }

        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("items", out var itemsNode))
        {
            return JsonSerializer.Deserialize(itemsNode?.ToJsonString() ?? "[]", LspJsonContext.Default.ListLspCompletionItem) ?? [];
        }

        return [];
    }

    private void RecordLspMetrics(string operation)
        => _telemetryService?.RecordCount("lsp.operation.count", new Dictionary<string, string> { ["operation"] = operation }, "count", "LSP operation count");

    #endregion
}
