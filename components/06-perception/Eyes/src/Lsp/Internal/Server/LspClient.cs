namespace Services.Lsp;

#region LSP Server Config (JSON-RPC 连接配置，非 Contracts 模型)

public sealed record LspServerConfig
{
    public required string LanguageId { get; init; }
    public required string Command { get; init; }
    public List<string> Arguments { get; init; } = new();
    public Dictionary<string, JsonElement> InitializationOptions { get; init; } = new();
}

#endregion

#region LSP JSON-RPC Message Types

public sealed partial class LspJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Params { get; init; }
}

public sealed partial class LspJsonRpcNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Params { get; init; }
}

#endregion

#region LSP Request Params Types

public sealed partial class LspInitializeParams
{
    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("rootUri")]
    public required string RootUri { get; init; }

    [JsonPropertyName("capabilities")]
    public required JsonNode Capabilities { get; init; }
}

public sealed partial class LspTextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

public sealed partial class LspTextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; init; }
}

public sealed partial class LspReferenceParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; init; }

    [JsonPropertyName("context")]
    public required LspReferenceContext Context { get; init; }
}

public sealed partial class LspReferenceContext
{
    [JsonPropertyName("includeDeclaration")]
    public required bool IncludeDeclaration { get; init; }
}

public sealed partial class LspDidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentItem TextDocument { get; init; }
}

public sealed partial class LspTextDocumentItem
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed partial class LspWorkspaceSymbolParams
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }
}

public sealed record LspCallHierarchyItemParam
{
    [JsonPropertyName("item")]
    public required LspCallHierarchyItem Item { get; init; }
}

public sealed partial class LspJsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

#endregion

public interface ILspClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(LspServerConfig config, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<bool> OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default);

    Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default);

    Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);

    Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);
}

public sealed partial class LspClient : ILspClient
{
    [Inject] private readonly ILogger<LspClient>? _logger;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private IInteractiveProcess? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private int _requestId;
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private readonly Dictionary<string, Func<JsonNode?, CancellationToken, ValueTask>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>>> _requestHandlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock;
    private CancellationTokenSource? _readCts;
    private int _isDisposed;

    public bool IsConnected => _process != null && !_process.HasExited;

    public event EventHandler<(string Method, JsonNode? Params)>? NotificationReceived;

    public LspClient(IFileSystem fs, IProcessService processService, ILogger<LspClient>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _lock = new SemaphoreSlim(1, 1);
        _logger = logger;
    }

    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler)
    {
        _notificationHandlers[method] = handler;
    }

    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler)
    {
        _requestHandlers[method] = handler;
    }

    public async Task<bool> ConnectAsync(LspServerConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new InteractiveProcessOptions
            {
                FileName = config.Command,
                Arguments = string.Join(" ", config.Arguments),
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                StandardInputEncoding = System.Text.Encoding.UTF8
            };

            _process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);

            _writer = new StreamWriter(_process.StandardInput.BaseStream, Encoding.UTF8);
            _reader = new StreamReader(_process.StandardOutput.BaseStream, Encoding.UTF8);

            _readCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(_readCts.Token), CancellationToken.None);
            _process.ErrorDataReceived += (_, line) =>
            {
                if (line != null) _logger?.LogDebug("LSP stderr: {Line}", line);
            };

            var initParams = new LspInitializeParams
            {
                ProcessId = Environment.ProcessId,
                RootUri = new Uri(_fs.GetCurrentDirectory()).ToString(),
                Capabilities = JsonSerializer.SerializeToNode(new Dictionary<string, JsonElement>(), LspJsonContext.Default.DictionaryStringJsonElement)!
            };
            var initResult = await SendRequestCoreAsync(LspMethod.Initialize.ToValue(), JsonSerializer.SerializeToNode(initParams, LspJsonContext.Default.LspInitializeParams), cancellationToken).ConfigureAwait(false);

            if (initResult is null)
            {
                _logger?.LogError("LSP服务器初始化失败");
                return false;
            }

            await SendNotificationAsync(LspMethod.Initialized.ToValue(), null).ConfigureAwait(false);

            _logger?.LogInformation("LSP客户端已连接到 {Command}", config.Command);
            return true;
        }
        catch (Exception ex)
        {
            if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
            _process = null;
            _logger?.LogError(ex, "连接LSP服务器失败");
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _readCts?.Cancel();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _ = SendNotificationAsync(LspMethod.Shutdown.ToValue(), null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                _process.Kill();
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"LSP client shutdown notification failed: {ex.Message}"); }
        }

        if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
        _writer?.Dispose();
        _reader?.Dispose();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingRequests.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return false;

        var uri = new Uri(filePath).ToString();

        var didOpenParams = new LspDidOpenTextDocumentParams
        {
            TextDocument = new LspTextDocumentItem
            {
                Uri = uri,
                LanguageId = languageId,
                Version = 1,
                Text = content
            }
        };

        await SendNotificationAsync(LspMethod.TextDocumentDidOpen.ToValue(), JsonSerializer.SerializeToNode(didOpenParams, LspJsonContext.Default.LspDidOpenTextDocumentParams)).ConfigureAwait(false);

        return true;
    }

    public async Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspLocation>();

        var uri = new Uri(filePath).ToString();

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentDefinition.ToValue(), JsonSerializer.SerializeToNode(positionParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is null)
            return new List<LspLocation>();

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspLocation) ?? new List<LspLocation>();
        }

        var single = JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspLocation);
        return single != null ? new List<LspLocation> { single } : new List<LspLocation>();
    }

    public async Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspLocation>();

        var uri = new Uri(filePath).ToString();

        var referenceParams = new LspReferenceParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character },
            Context = new LspReferenceContext { IncludeDeclaration = true }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentReferences.ToValue(), JsonSerializer.SerializeToNode(referenceParams, LspJsonContext.Default.LspReferenceParams), cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspLocation) ?? new List<LspLocation>();
        }

        return new List<LspLocation>();
    }

    public async Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        var uri = new Uri(filePath).ToString();

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentHover.ToValue(), JsonSerializer.SerializeToNode(positionParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is null)
            return null;

        return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspHoverResult);
    }

    public async Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCompletionItem>();

        var uri = new Uri(filePath).ToString();

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentCompletion.ToValue(), JsonSerializer.SerializeToNode(positionParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is null)
            return new List<LspCompletionItem>();

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCompletionItem) ?? new List<LspCompletionItem>();
        }

        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("items", out var itemsNode))
        {
            return JsonSerializer.Deserialize(itemsNode?.ToJsonString() ?? "[]", LspJsonContext.Default.ListLspCompletionItem) ?? new List<LspCompletionItem>();
        }

        return new List<LspCompletionItem>();
    }

    public async Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspDocumentSymbol>();

        var uri = new Uri(filePath).ToString();

        var docParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition()
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentDocumentSymbol.ToValue(), JsonSerializer.SerializeToNode(docParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspDocumentSymbol) ?? new List<LspDocumentSymbol>();
        }

        return new List<LspDocumentSymbol>();
    }

    public async Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspSymbolInformation>();

        var symbolParams = new LspWorkspaceSymbolParams { Query = query };

        var result = await SendRequestCoreAsync(LspMethod.WorkspaceSymbol.ToValue(), JsonSerializer.SerializeToNode(symbolParams, LspJsonContext.Default.LspWorkspaceSymbolParams), cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspSymbolInformation) ?? new List<LspSymbolInformation>();
        }

        return new List<LspSymbolInformation>();
    }

    public async Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspLocation>();

        var uri = new Uri(filePath).ToString();

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentImplementation.ToValue(), JsonSerializer.SerializeToNode(positionParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is null)
            return new List<LspLocation>();

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspLocation) ?? new List<LspLocation>();
        }

        var single = JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspLocation);
        return single != null ? new List<LspLocation> { single } : new List<LspLocation>();
    }

    public async Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCallHierarchyItem>();

        var uri = new Uri(filePath).ToString();

        var positionParams = new LspTextDocumentPositionParams
        {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentPrepareCallHierarchy.ToValue(), JsonSerializer.SerializeToNode(positionParams, LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyItem) ?? new List<LspCallHierarchyItem>();
        }

        return new List<LspCallHierarchyItem>();
    }

    public async Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCallHierarchyIncomingCall>();

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyIncomingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyIncomingCall) ?? new List<LspCallHierarchyIncomingCall>();
        }

        return new List<LspCallHierarchyIncomingCall>();
    }

    public async Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCallHierarchyOutgoingCall>();

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyOutgoingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return JsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyOutgoingCall) ?? new List<LspCallHierarchyOutgoingCall>();
        }

        return new List<LspCallHierarchyOutgoingCall>();
    }

    #region Private Methods

    internal async Task<JsonNode?> SendRequestCoreAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonNode?>();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingRequests[id] = tcs;
        }
        finally
        {
            _lock.Release();
        }

        var request = new LspJsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(request, LspJsonContext.Default.LspJsonRpcRequest);
        await SendMessageAsync(json).ConfigureAwait(false);

        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(WorkflowConstants.Timeouts.DefaultTimeoutSeconds));

        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingRequests.Remove(id);
            }
            finally
            {
                _lock.Release();
            }
            throw;
        }
    }

    internal async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken = default)
    {
        var notification = new LspJsonRpcNotification
        {
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(notification, LspJsonContext.Default.LspJsonRpcNotification);
        await SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        if (_writer == null) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";

        await _writer.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                var headerLine = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (headerLine == null) break;

                if (!headerLine.StartsWith("Content-Length: "))
                    continue;

                var contentLength = int.Parse(headerLine["Content-Length: ".Length..]);

                await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                var buffer = new char[contentLength];
                var read = 0;
                while (read < contentLength)
                {
                    var n = await _reader.ReadAsync(buffer, read, contentLength - read).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }

                var json = new string(buffer);
                await ProcessMessageAsync(json, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LSP读取循环错误");
        }
    }

    private async Task ProcessMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                return;

            if (obj.TryGetPropertyValue("id", out var idNode) && idNode is not null)
            {
                if (obj.TryGetPropertyValue("method", out var methodNode) && methodNode is not null)
                {
                    var id = idNode.GetValue<string>();
                    var method = methodNode.GetValue<string>();
                    var @params = obj.TryGetPropertyValue("params", out var p) ? p : null;

                    if (_requestHandlers.TryGetValue(method, out var handler))
                    {
                        try
                        {
                            var result = await handler(id, @params, cancellationToken).ConfigureAwait(false);
                            await SendResponseAsync(id, result, null, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await SendResponseAsync(id, null, new LspJsonRpcError { Code = -32603, Message = ex.Message }, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await SendResponseAsync(id, null, new LspJsonRpcError { Code = -32601, Message = $"Method not found: {method}" }, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }

                {
                    var id = idNode.GetValue<string>();

                    await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (_pendingRequests.TryGetValue(id, out var tcs))
                        {
                            if (obj.TryGetPropertyValue("result", out var resultNode))
                            {
                                tcs.TrySetResult(resultNode);
                            }
                            else if (obj.TryGetPropertyValue("error", out var errorNode))
                            {
                                tcs.TrySetException(new InvalidOperationException($"LSP错误: {errorNode?.ToJsonString()}"));
                            }
                            else
                            {
                                tcs.TrySetResult(null);
                            }

                            _pendingRequests.Remove(id);
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
            }
            else if (obj.TryGetPropertyValue("method", out var notifMethodNode) && notifMethodNode is not null)
            {
                var method = notifMethodNode.GetValue<string>();
                var @params = obj.TryGetPropertyValue("params", out var p) ? p : null;

                NotificationReceived?.Invoke(this, (method, @params));

                if (_notificationHandlers.TryGetValue(method, out var handler))
                {
                    await handler(@params, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理LSP消息失败: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    private async Task SendResponseAsync(string id, JsonNode? result, LspJsonRpcError? error, CancellationToken cancellationToken)
    {
        var response = new Dictionary<string, JsonElement>
        {
            ["jsonrpc"] = JsonElementHelper.FromString("2.0"),
            ["id"] = JsonElementHelper.FromString(id)
        };
        if (error != null)
        {
            response["error"] = JsonElementHelper.FromObject(error, LspJsonContext.Default.LspJsonRpcError);
        }
        else
        {
            response["result"] = result is null
                ? JsonElementHelper.NullElement()
                : JsonNodeToElement(result);
        }

        var json = JsonSerializer.Serialize(response, LspJsonContext.Default.DictionaryStringJsonElement);
        await SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement JsonNodeToElement(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    #endregion
}
