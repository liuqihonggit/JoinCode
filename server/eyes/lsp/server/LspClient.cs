namespace Services.Lsp;

#region LSP Server Config (JSON-RPC 连接配置，非 Contracts 模型)

/// <summary>
/// LSP 服务器配置 — 描述如何启动并初始化一个 LSP 服务器进程
/// </summary>
public sealed record LspServerConfig {
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
public sealed partial class LspJsonRpcRequest {
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
public sealed partial class LspJsonRpcNotification {
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
public sealed partial class LspInitializeParams {
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
public sealed partial class LspTextDocumentIdentifier {
    /// <summary>文档 URI</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// LSP 文本文档位置参数（文档 + 光标位置）
/// </summary>
public sealed partial class LspTextDocumentPositionParams {
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
public sealed partial class LspReferenceParams {
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
public sealed partial class LspReferenceContext {
    /// <summary>是否在结果中包含声明位置</summary>
    [JsonPropertyName("includeDeclaration")]
    public required bool IncludeDeclaration { get; init; }
}

/// <summary>
/// LSP textDocument/didOpen 通知参数
/// </summary>
public sealed partial class LspDidOpenTextDocumentParams {
    /// <summary>已打开的文本文档项</summary>
    [JsonPropertyName("textDocument")]
    public required LspTextDocumentItem TextDocument { get; init; }
}

/// <summary>
/// LSP 文本文档项（含 uri、languageId、version、text）
/// </summary>
public sealed partial class LspTextDocumentItem {
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
public sealed partial class LspWorkspaceSymbolParams {
    /// <summary>符号查询字符串</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }
}

/// <summary>
/// LSP 调用层次项参数
/// </summary>
public sealed record LspCallHierarchyItemParam {
    /// <summary>调用层次项</summary>
    [JsonPropertyName("item")]
    public required LspCallHierarchyItem Item { get; init; }
}

/// <summary>
/// LSP JSON-RPC 错误对象
/// </summary>
public sealed partial class LspJsonRpcError {
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
public interface ILspClient : IAsyncDisposable {
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
public sealed partial class LspClient : ILspClient {
    private readonly LspProcessChannel _channel;
    private readonly LspMessageRouter _router;
    private readonly ILogger? _logger;
    private int _isDisposed;

    /// <summary>是否已连接到 LSP 服务器</summary>
    public bool IsConnected => _channel.IsConnected;

    /// <summary>收到通知时触发，参数为 (方法名, 参数)</summary>
    public event EventHandler<(string Method, JsonNode? Params)>? NotificationReceived;

    /// <summary>
    /// 构造 LspClient — 组合根:持有进程通道 + 消息路由
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程服务抽象</param>
    /// <param name="logger">日志记录器（可选）</param>
    public LspClient(IFileSystem fs, IProcessService processService, ILogger? logger = null) {
        _logger = logger;
        _channel = new LspProcessChannel(fs, processService, logger);
        _router = new LspMessageRouter();
        _router.NotificationReceived += (_, e) => NotificationReceived?.Invoke(this, e);
    }

    /// <summary>注册通知处理程序</summary>
    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler) {
        _router.OnNotification(method, handler);
    }

    /// <summary>注册请求处理程序（服务器向客户端发起的请求）</summary>
    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler) {
        _router.OnRequest(method, handler);
    }

    /// <summary>
    /// 连接并初始化 LSP 服务器
    /// </summary>
    /// <param name="config">LSP 服务器配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接成功返回 true，否则 false</returns>
    public async Task<bool> ConnectAsync(LspServerConfig config, CancellationToken cancellationToken = default) {
        try {
            await _channel.StartAsync(config, cancellationToken).ConfigureAwait(false);

            _ = Task.Run(async () => {
                try { await _channel.ReadLoopAsync(OnMessageReceived, _channel.ReadToken).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (Exception ex) { _logger?.LogDebug(ex, "LSP read loop terminated with exception"); }
            }, _channel.ReadToken);

            var rootDir = _channel.GetRootDir(config.WorkingDirectory);
            var initParams = new LspInitializeParams {
                ProcessId = Environment.ProcessId,
                RootUri = new Uri(rootDir + Path.DirectorySeparatorChar).ToString(),
                Capabilities = JsonSerializer.SerializeToNode(new Dictionary<string, JsonElement>(), LspJsonContext.Default.DictionaryStringJsonElement)!
            };
            var initResult = await SendRequestCoreAsync(LspMethod.Initialize.ToValue(), JsonSerializer.SerializeToNode(initParams, LspJsonContext.Default.LspInitializeParams), cancellationToken).ConfigureAwait(false);

            if (initResult is null) {
                _logger?.LogError("LSP服务器初始化失败");
                return false;
            }

            await SendNotificationAsync(LspMethod.Initialized.ToValue(), null).ConfigureAwait(false);

            _logger?.LogInformation("LSP客户端已连接到 {Command}", config.Command);
            return true;
        } catch (Exception ex) {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _logger?.LogError(ex, "连接LSP服务器失败");
            return false;
        }
    }

    /// <summary>
    /// 断开与 LSP 服务器的连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default) {
        await _channel.DisconnectAsync(
            sendShutdownNotification: () => SendNotificationAsync(LspMethod.Shutdown.ToValue(), null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)),
            cancellationToken).ConfigureAwait(false);

        _router.Clear();
    }

    /// <summary>
    /// 通知 LSP 服务器打开了一个文档
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="languageId">语言标识符</param>
    /// <param name="content">文档全文内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>通知成功返回 true，否则 false</returns>
    public async Task<bool> OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default) {
        if (!IsConnected) return false;

        var uri = new Uri(filePath).ToString();

        var didOpenParams = new LspDidOpenTextDocumentParams {
            TextDocument = new LspTextDocumentItem {
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
    public async Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentDefinition.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, line, character), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        return DeserializeLocations(result);
    }

    /// <summary>
    /// 将 LSP definition/implementation 响应反序列化为 LspLocation 列表。
    /// 处理三种格式：null、Location、Location[]、LocationLink、LocationLink[]。
    /// csharp-ls 返回 LocationLink 格式（含 targetUri/targetRange），需转换为 LspLocation。
    /// </summary>
    private static List<LspLocation> DeserializeLocations(JsonNode? result) {
        if (result is null) return [];

        if (result is JsonArray arr) {
            var locations = new List<LspLocation>();
            foreach (var item in arr) {
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
    private static LspLocation? DeserializeSingleLocation(JsonNode? node) {
        if (node is not JsonObject obj) return null;

        if (obj.TryGetPropertyValue("uri", out var uriNode) && uriNode != null) {
            return RelaxedJsonSerializer.Deserialize(node!.ToJsonString(), LspJsonContext.Default.LspLocation);
        }

        if (obj.TryGetPropertyValue("targetUri", out var targetUriNode) && targetUriNode != null) {
            var targetUri = targetUriNode.GetValue<string>();
            var rangeNode = obj.TryGetPropertyValue("targetRange", out var tr) ? tr : null;
            if (rangeNode != null) {
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
    public async Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var uri = new Uri(filePath).ToString();
        var referenceParams = new LspReferenceParams {
            TextDocument = new LspTextDocumentIdentifier { Uri = uri },
            Position = new LspPosition { Line = line, Character = character },
            Context = new LspReferenceContext { IncludeDeclaration = true }
        };

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentReferences.ToValue(), JsonSerializer.SerializeToNode(referenceParams, LspJsonContext.Default.LspReferenceParams), cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspLocation);
    }

    /// <summary>
    /// 查询悬停信息
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>悬停结果，无结果时返回 null</returns>
    public async Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return null;

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentHover.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, line, character), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

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
    public async Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentCompletion.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, line, character), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        if (result is null)
            return new List<LspCompletionItem>();

        if (result is JsonArray) {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), LspJsonContext.Default.ListLspCompletionItem) ?? new List<LspCompletionItem>();
        }

        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("items", out var itemsNode)) {
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
    public async Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentDocumentSymbol.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, 0, 0), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspDocumentSymbol);
    }

    /// <summary>
    /// 搜索工作区符号
    /// </summary>
    /// <param name="query">符号查询字符串</param>
    /// <param name="workspacePath">工作区路径（可选）</param>
    /// <param name="serverName">服务器名称（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>符号信息列表</returns>
    public async Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, string? workspacePath = null, string? serverName = null, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var symbolParams = new LspWorkspaceSymbolParams { Query = query };

        var result = await SendRequestCoreAsync(LspMethod.WorkspaceSymbol.ToValue(), JsonSerializer.SerializeToNode(symbolParams, LspJsonContext.Default.LspWorkspaceSymbolParams), cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspSymbolInformation);
    }

    /// <summary>
    /// 跳转到实现
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="line">行号（0-based）</param>
    /// <param name="character">列号（0-based）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实现位置列表</returns>
    public async Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentImplementation.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, line, character), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

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
    public async Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var result = await SendRequestCoreAsync(LspMethod.TextDocumentPrepareCallHierarchy.ToValue(), JsonSerializer.SerializeToNode(CreatePositionParams(filePath, line, character), LspJsonContext.Default.LspTextDocumentPositionParams), cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspCallHierarchyItem);
    }

    /// <summary>
    /// 查询调用层次的入边（谁调用了此项）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>入边调用列表</returns>
    public async Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyIncomingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspCallHierarchyIncomingCall);
    }

    /// <summary>
    /// 查询调用层次的出边（此项调用了谁）
    /// </summary>
    /// <param name="item">调用层次项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>出边调用列表</returns>
    public async Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default) {
        if (!IsConnected) return [];

        var paramObj = new LspCallHierarchyItemParam { Item = item };
        var paramNode = JsonSerializer.SerializeToNode(paramObj, LspJsonContext.Default.LspCallHierarchyItemParam);
        var result = await SendRequestCoreAsync(LspMethod.CallHierarchyOutgoingCalls.ToValue(), paramNode, cancellationToken).ConfigureAwait(false);

        return DeserializeListResult(result, LspJsonContext.Default.ListLspCallHierarchyOutgoingCall);
    }

    #region Private Methods

    /// <summary>构建文本位置参数 — 消除6处重复的 Uri+Position 构造</summary>
    private static LspTextDocumentPositionParams CreatePositionParams(string filePath, int line, int character) {
        return new LspTextDocumentPositionParams {
            TextDocument = new LspTextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new LspPosition { Line = line, Character = character }
        };
    }

    /// <summary>反序列化 JsonArray 结果为列表 — 消除7处重复的 if(result is JsonArray) return Deserialize ?? new() 模式</summary>
    private static List<T> DeserializeListResult<T>(JsonNode? result, JsonTypeInfo<List<T>> jsonInfo) {
        if (result is JsonArray) {
            return RelaxedJsonSerializer.Deserialize(result.ToJsonString(), jsonInfo) ?? new List<T>();
        }
        return new List<T>();
    }

    /// <summary>
    /// 发送 JSON-RPC 请求并等待响应 — 委托路由器创建请求 + 通道发送
    /// </summary>
    internal async Task<JsonNode?> SendRequestCoreAsync(string method, JsonNode? @params, CancellationToken cancellationToken) {
        var (id, tcs, json) = _router.CreateRequest(method, @params);

        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(WorkflowConstants.Timeouts.DefaultTimeoutSeconds));

        try {
            await _channel.SendMessageAsync(json).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            _router.RemovePending(id);
            throw;
        } catch {
            _router.RemovePending(id);
            throw;
        }
    }

    /// <summary>
    /// 发送 JSON-RPC 通知（无响应）— 序列化 + 通道发送
    /// </summary>
    internal async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken = default) {
        var notification = new LspJsonRpcNotification {
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(notification, LspJsonContext.Default.LspJsonRpcNotification);
        await _channel.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>消息接收回调 — 委托路由器处理,通过通道发送响应</summary>
    private Task OnMessageReceived(string json, CancellationToken cancellationToken) {
        return _router.ProcessMessageAsync(json, cancellationToken, (msg, ct) => _channel.SendMessageAsync(msg, ct), _logger);
    }

    /// <summary>异步释放 LSP 客户端，断开连接</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(DisconnectAsync());
    }

    #endregion
}