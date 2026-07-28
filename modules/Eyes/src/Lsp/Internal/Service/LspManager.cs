namespace Services.Lsp.Internal;

public interface ILspManager : IAsyncDisposable
{
    bool IsInitialized { get; }

    Task InitializeAsync(IEnumerable<LspInstanceConfig> configs, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);

    ILspServerInstance? GetServerForFile(string filePath);
    Task<ILspServerInstance?> EnsureServerStartedAsync(string filePath, CancellationToken cancellationToken = default);
    Task<JsonNode?> SendRequestAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default);
    Task SendNotificationAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default);

    Task OpenFileAsync(string filePath, string content, CancellationToken cancellationToken = default);
    Task ChangeFileAsync(string filePath, string content, CancellationToken cancellationToken = default);
    Task SaveFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task CloseFileAsync(string filePath, CancellationToken cancellationToken = default);
    bool IsFileOpen(string filePath);

    IReadOnlyDictionary<string, ILspServerInstance> GetAllServers();
}

[Register]
public sealed partial class LspManager : ILspManager
{
    private const int MaxLspFileSizeBytes = 10_000_000;

    private readonly ConcurrentDictionary<string, LspServerInstance> _servers = new();
    private readonly Dictionary<string, List<string>> _extensionMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _openedFiles = new();
    [Inject] private readonly ILogger<LspManager> _logger;
    [Inject] private readonly IFileOperationService? _fileOperationService;
    [Inject] private readonly ILspPassiveFeedback? _passiveFeedback;
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly IProcessService _processService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _isInitialized;
    private int _isDisposed;

    public bool IsInitialized => Volatile.Read(ref _isInitialized) == 1;

    public async Task InitializeAsync(IEnumerable<LspInstanceConfig> configs, CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInitialized) return;

            foreach (var config in configs)
            {
                var instance = new LspServerInstance(config, _fs, _processService, _logger != null
                    ? new LoggerFactory().CreateLogger<LspServerInstance>()
                    : throw new InvalidOperationException("Logger required"));

                _servers[config.Name] = instance;

                foreach (var kvp in config.ExtensionToLanguage)
                {
                    if (!_extensionMap.TryGetValue(kvp.Key, out var serverNames))
                    {
                        serverNames = [];
                        _extensionMap[kvp.Key] = serverNames;
                    }
                    serverNames.Add(config.Name);
                }

                _logger.LogInformation("Registered LSP server: {Name} ({LanguageId}) for extensions: {Extensions}",
                    config.Name, config.LanguageId, string.Join(", ", config.ExtensionToLanguage.Keys));
            }

            Volatile.Write(ref _isInitialized, 1);
            _logger.LogInformation("LSP Manager initialized with {Count} server(s)", _servers.Count);

            if (_passiveFeedback != null)
            {
                _passiveFeedback.RegisterNotificationHandlers(this);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsInitialized) return;

            var tasks = _servers.Values.Select(s => s.StopAsync(cancellationToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            _servers.Clear();
            _extensionMap.Clear();
            _openedFiles.Clear();
            Volatile.Write(ref _isInitialized, 0);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public ILspServerInstance? GetServerForFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (!_extensionMap.TryGetValue(ext, out var serverNames) || serverNames.Count == 0)
        {
            return null;
        }

        return _servers.TryGetValue(serverNames[0], out var instance) ? instance : null;
    }

    public async Task<ILspServerInstance?> EnsureServerStartedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null) return null;

        if (server.IsHealthy) return server;

        try
        {
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            return server;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start LSP server '{Name}' for file: {FilePath}", server.Name, filePath);
            return null;
        }
    }

    public async Task<JsonNode?> SendRequestAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (server == null) return null;

        return await server.SendRequestAsync(method, @params, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendNotificationAsync(string filePath, string method, object? @params, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (server == null) return;

        await server.SendNotificationAsync(method, @params, cancellationToken).ConfigureAwait(false);
    }

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

    public async Task CloseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        if (!_openedFiles.TryRemove(fileUri, out var serverName)) return;

        if (!_servers.TryGetValue(serverName, out var server)) return;
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

    public bool IsFileOpen(string filePath)
    {
        var fileUri = LspUriHelper.PathToFileUrl(filePath);
        return _openedFiles.ContainsKey(fileUri);
    }

    public IReadOnlyDictionary<string, ILspServerInstance> GetAllServers()
    {
        return _servers.ToDictionary(kvp => kvp.Key, kvp => (ILspServerInstance)kvp.Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        await ShutdownAsync().ConfigureAwait(false);

        var tasks = _servers.Values.Select(s => s.DisposeAsync().AsTask());
        await Task.WhenAll(tasks).ConfigureAwait(false);

        _servers.Clear();
        _initLock.Dispose();
    }
}
