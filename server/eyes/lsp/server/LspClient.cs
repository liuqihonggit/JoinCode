namespace Services.Lsp;

#region LSP Server Config (JSON-RPC 连接配置，非 Contracts 模型)

/// <summary>
/// LSP 服务器配置 — 描述如何启动并初始化一个 LSP 服务器进程
/// </summary>
public sealed record LspServerConfig
{
    /// <summary>语言标识符（如 "csharp"、"python"）</summary>
    public required string LanguageId { get; init; }
    /// <summary>启动 LSP 服务器的命令行可执行文件路径</summary>
    public required string Command { get; init; }
    /// <summary>传给 LSP 服务器的命令行参数列表</summary>
    public List<string> Arguments { get; init; } = new();
    /// <summary>initialize 请求的 initializationOptions 字段</summary>
    public Dictionary<string, JsonElement> InitializationOptions { get; init; } = new();
    /// <summary>
    /// 工作目录 — LSP 服务器进程的 CWD，同时用于 initialize 的 rootUri。
    /// 为 null 时回退到当前进程的 CWD。
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

#endregion

#region LSP JSON-RPC Message Types

/// <summary>
/// LSP JSON-RPC 请求消息结构
/// </summary>
public sealed partial class LspJsonRpcRequest
{
    /// <summary>JSON-RPC 协议版本，固定为 "2.0"</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>请求 ID，用于匹配响应</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>请求方法名</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>请求参数，可为 null</summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Params { get; init; }
}

/// <summary>
/// LSP JSON-RPC 通知消息结构（无 ID，无响应）
/// </summary>
public sealed partial class LspJsonRpcNotification
{
    /// <summary>JSON-RPC 协议版本，固定为 "2.0"</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>通知方法名</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>通知参数，可为 null</summary>
    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Params { get; init; }
}

#endregion

#region LSP Request Params Types

/// <summary>
/// LSP initialize 请求参数
/// </summary>
public sealed partial class LspInitializeParams
{
    /// <summary>客户端进程 ID</summary>
    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }

    /// <summary>工作区根 URI</summary>
    [JsonPropertyName("rootUri")]
    public required string RootUri { get; init; }

    /// <summary>客户端能力声明</summary>
    [JsonPropertyName("capabilities")]
    public required JsonNode Capabilities { get; init; }
}

/// <summary>
/// LSP 文本文档标识符（仅含 uri）
/// </summary>
public sealed partial class LspTextDocumentIdentifier
{
    /// <summary>文档 URI</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// LSP 文本文档位置参数（文档 + 光标位置）
/// </summary>
public sealed partial class LspTextDocumentPositionParams
{
    /// <summary>文本文档标识符</summary>
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    /// <summary>光标位置</summary>
    [JsonPropertyName("position")]
    public required LspPosition Position { get; init; }
}

/// <summary>
/// LSP references 请求参数
/// </summary>
public sealed partial class LspReferenceParams
{
    /// <summary>文本文档标识符</summary>
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentIdentifier TextDocument { get; init; }

    /// <summary>光标位置</summary>
    [JsonPropertyName("position")]
    public required LspPosition Position { get; init; }

    /// <summary>引用查询上下文</summary>
    [JsonPropertyName("context")]
    public required LspReferenceContext Context { get; init; }
}

/// <summary>
/// LSP references 上下文 — 控制是否包含声明
/// </summary>
public sealed partial class LspReferenceContext
{
    /// <summary>是否在结果中包含声明位置</summary>
    [JsonPropertyName("includeDeclaration")]
    public required bool IncludeDeclaration { get; init; }
}

/// <summary>
/// LSP textDocument/didOpen 通知参数
/// </summary>
public sealed partial class LspDidOpenTextDocumentParams
{
    /// <summary>已打开的文本文档项</summary>
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentItem TextDocument { get; init; }
}

/// <summary>
/// LSP 文本文档项（含 uri、languageId、version、text）
/// </summary>
public sealed partial class LspTextDocumentItem
{
    /// <summary>文档 URI</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>语言标识符</summary>
    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }

    /// <summary>文档版本号</summary>
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    /// <summary>文档全文内容</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// LSP workspace/symbol 请求参数
/// </summary>
public sealed partial class LspWorkspaceSymbolParams
{
    /// <summary>符号查询字符串</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }
}

/// <summary>
/// LSP 调用层次项参数
/// </summary>
public sealed record LspCallHierarchyItemParam
{
    /// <summary>调用层次项</summary>
    [JsonPropertyName("item")]
    public required LspCallHierarchyItem Item { get; init; }
}

/// <summary>
/// LSP JSON-RPC 错误对象
/// </summary>
public sealed partial class LspJsonRpcError
{
    /// <summary>错误代码</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>错误消息</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

#endregion

/// <summary>
/// LSP 客户端接口 — 提供与 LSP 服务器交互的标准方法
/// </summary>
public interface ILspClient : IAsyncDisposable
{
    /// <summary>是否已连接到 LSP 服务器</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接并初始化 LSP 服务器
    /// </summary>
    /// <param name="config">LSP 服务器配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接成功返回 true，否则 false</returns>
    Task<bool> ConnectAsync(LspServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开与 LSP 服务器的连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知 LSP 服务器打开了一个文档
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="languageId">语言标识符</param>
    /// <param name="content">文档全文内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>通知成功返回 true，否则 false</returns>
    Task<bool> OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 跳转到定义
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>定义位置列表</returns>
    Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找引用
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>引用位置列表</returns>
    Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询悬停信息
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>悬停结果，无结果时返回 null</returns>
    Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取自动补全项
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>补全项列表</returns>
    Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档符号列表
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文档符号列表</returns>
    Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索工作区符号
    /// </summary>
    /// <param name="query">符号查询字符串</param>
    /// <param name="workspacePath">工作区路径（可选）</param>
    /// <param name="serverName">服务器名称（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>符号信息列表</returns>
    Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, string? workspacePath = null, string? serverName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 跳转到实现
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实现位置列表</returns>
    Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 准备调用层次（获取调用层次入口项）
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用层次项列表</returns>
    Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询调用层次的入边（谁调用了此项）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>入边调用列表</returns>
    Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询调用层次的出边（此项调用了谁）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>出边调用列表</returns>
    Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);
}

/// <summary>
/// LSP 客户端实现 — 通过 JSON-RPC 与 LSP 服务器进程交互
/// </summary>
public sealed partial class LspClient : ILspClient
{
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private IInteractiveProcess? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private int _requestId;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private readonly Dictionary<string, Func<JsonNode?, CancellationToken, ValueTask>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>>> _requestHandlers = new(StringComparer.Ordinal);

    private CancellationTokenSource? _readCts;
    private int _isDisposed;

    /// <summary>是否已连接到 LSP 服务器</summary>
    public bool IsConnected => _process != null && !_process.HasExited;

    /// <summary>收到通知时触发，参数为 (方法名, 参数)</summary>
    public event EventHandler<(string Method, JsonNode? Params)>? NotificationReceived;

    /// <summary>
    /// 构造 LspClient
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程服务抽象</param>
    /// <param name="logger">日志记录器（可选）</param>
    public LspClient(IFileSystem fs, IProcessService processService, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));

        _logger = logger;
    }

    /// <summary>
    /// 注册通知处理程序
    /// </summary>
    /// <param name="method">通知方法名</param>
    /// <param name="handler">通知处理委托</param>
    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler)
    {
        _notificationHandlers[method] = handler;
    }

    /// <summary>
    /// 注册请求处理程序（服务器向客户端发起的请求）
    /// </summary>
    /// <param name="method">请求方法名</param>
    /// <param name="handler">请求处理委托，返回响应 JsonNode</param>
    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler)
    {
        _requestHandlers[method] = handler;
    }

    /// <summary>
    /// 连接并初始化 LSP 服务器
    /// </summary>
    /// <param name="config">LSP 服务器配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接成功返回 true，否则 false</returns>
    public async Task<bool> ConnectAsync(LspServerConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new InteractiveProcessOptions
            {
                FileName = config.Command,
                ArgumentList = config.Arguments,
                WorkingDirectory = config.WorkingDirectory,
            };

            _process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);

            _writer = _process.StandardInput;
            _reader = _process.StandardOutput;

            _readCts = new CancellationTokenSource();
            var readToken = _readCts.Token;
            _ = Task.Run(async () =>
            {
                try { await ReadLoopAsync(readToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger?.LogDebug(ex, "LSP read loop terminated with exception"); }
            }, readToken);
            _process.ErrorDataReceived += (_, line) =>
            {
                if (line != null) _logger?.LogDebug("LSP stderr: {Line}", line);
            };

            var rootDir = config.WorkingDirectory ?? _fs.GetCurrentDirectory();
            var initParams = new LspInitializeParams
            {
                ProcessId = Environment.ProcessId,
                RootUri = new Uri(rootDir + Path.DirectorySeparatorChar).ToString(),
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

    /// <summary>
    /// 断开与 LSP 服务器的连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
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
            catch (Exception ex) { _logger?.LogWarning(ex, "LSP 客户端关闭通知发送失败"); }
        }

        if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
        _writer?.Dispose();
        _reader?.Dispose();

        _pendingRequests.Clear();
    }

    /// <summary>
    /// 通知 LSP 服务器打开了一个文档
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="languageId">语言标识符</param>
    /// <param name="content">文档全文内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>通知成功返回 true，否则 false</returns>
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

    /// <summary>
    /// 跳转到定义
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>定义位置列表</returns>
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

        return DeserializeLocations(result);
    }

    /// <summary>
    /// 将 LSP definition/implementation 响应反序列化为 LspLocation 列表。
    /// 处理三种格式：null、Location、Location[]、LocationLink、LocationLink[]。
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

    /// <summary>
    /// 查找引用
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>引用位置列表</returns>
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
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspLocation) ?? new List<LspLocation>();
        }

        return new List<LspLocation>();
    }

    /// <summary>
    /// 查询悬停信息
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>悬停结果，无结果时返回 null</returns>
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

        return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.LspHoverResult);
    }

    /// <summary>
    /// 获取自动补全项
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>补全项列表</returns>
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
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCompletionItem) ?? new List<LspCompletionItem>();
        }

        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("items", out var itemsNode))
        {
            return RelaxedJsonSerializer.Deserialize(itemsNode?.ToJsonString() ?? "[]", LspJsonContext.Default.ListLspCompletionItem) ?? new List<LspCompletionItem>();
        }

        return new List<LspCompletionItem>();
    }

    /// <summary>
    /// 获取文档符号列表
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文档符号列表</returns>
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
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspDocumentSymbol) ?? new List<LspDocumentSymbol>();
        }

        return new List<LspDocumentSymbol>();
    }

    /// <summary>
    /// 搜索工作区符号
    /// </summary>
    /// <param name="query">符号查询字符串</param>
    /// <param name="workspacePath">工作区路径（可选）</param>
    /// <param name="serverName">服务器名称（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>符号信息列表</returns>
    public async Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, string? workspacePath = null, string? serverName = null, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspSymbolInformation>();

        var symbolParams = new LspWorkspaceSymbolParams { Query = query };

        var result = await SendRequestCoreAsync(LspMethod.WorkspaceSymbol.ToValue(), JsonSerializer.SerializeToNode(symbolParams, LspJsonContext.Default.LspWorkspaceSymbolParams), cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspSymbolInformation) ?? new List<LspSymbolInformation>();
        }

        return new List<LspSymbolInformation>();
    }

    /// <summary>
    /// 跳转到实现
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实现位置列表</returns>
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

        return DeserializeLocations(result);
    }

    /// <summary>
    /// 准备调用层次（获取调用层次入口项）
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用层次项列表</returns>
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
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyItem) ?? new List<LspCallHierarchyItem>();
        }

        return new List<LspCallHierarchyItem>();
    }

    /// <summary>
    /// 查询调用层次的入边（谁调用了此项）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>入边调用列表</returns>
    public async Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCallHierarchyIncomingCall>();

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyIncomingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyIncomingCall) ?? new List<LspCallHierarchyIncomingCall>();
        }

        return new List<LspCallHierarchyIncomingCall>();
    }

    /// <summary>
    /// 查询调用层次的出边（此项调用了谁）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>出边调用列表</returns>
    public async Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return new List<LspCallHierarchyOutgoingCall>();

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyOutgoingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        if (result is JsonArray)
        {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCallHierarchyOutgoingCall) ?? new List<LspCallHierarchyOutgoingCall>();
        }

        return new List<LspCallHierarchyOutgoingCall>();
    }

    #region Private Methods

    /// <summary>
    /// 发送 JSON-RPC 请求并等待响应（核心方法）
    /// </summary>
    /// <param name="method">请求方法名</param>
    /// <param name="params">请求参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 JsonNode，超时或出错时抛异常</returns>
    internal async Task<JsonNode?> SendRequestCoreAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonNode?>();
        _pendingRequests[id] = tcs;

        var request = new LspJsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(request, LspJsonContext.Default.LspJsonRpcRequest);

        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(WorkflowConstants.Timeouts.DefaultTimeoutSeconds));

        try
        {
            await SendMessageAsync(json).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
    }


    /// <summary>
    /// 发送 JSON-RPC 通知（无响应）
    /// </summary>
    /// <param name="method">通知方法名</param>
    /// <param name="params">通知参数</param>
    /// <param name="cancellationToken">取消令牌</param>
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
        if (_process == null) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        var stream = _process.StandardInput.BaseStream;
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

                        _pendingRequests.TryRemove(id, out _);
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

    /// <summary>
    /// 异步释放 LSP 客户端，断开连接
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
    }

    #endregion
}
