namespace Services.Lsp;

/// <summary>
/// LSP 消息路由 — 封装请求ID生成、待响应请求管理、通知/请求处理器分发
/// 从 LspClient 提取,纯消息路由无进程IO依赖,通过 sendJsonAsync 回调发送响应
/// </summary>
internal sealed class LspMessageRouter {
    private int _requestId;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private readonly Dictionary<string, LspMethodHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>收到通知时触发，参数为 (方法名, 参数)</summary>
    public event EventHandler<(string Method, JsonNode? Params)>? NotificationReceived;

    /// <summary>注册通知处理程序</summary>
    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler) {
        _handlers[method] = new LspMethodHandler(Notification: handler);
    }

    /// <summary>注册请求处理程序（服务器向客户端发起的请求）</summary>
    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler) {
        _handlers[method] = new LspMethodHandler(Request: handler);
    }

    /// <summary>创建 JSON-RPC 请求 — 生成ID + 注册 pending + 序列化</summary>
    public (string Id, TaskCompletionSource<JsonNode?> Tcs, string Json) CreateRequest(string method, JsonNode? @params) {
        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonNode?>();
        _pendingRequests[id] = tcs;

        var request = new LspJsonRpcRequest {
            Id = id,
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(request, LspJsonContext.Default.LspJsonRpcRequest);
        return (id, tcs, json);
    }

    /// <summary>移除待响应请求</summary>
    public void RemovePending(string id) => _pendingRequests.TryRemove(id, out _);

    /// <summary>清空所有待响应请求</summary>
    public void Clear() => _pendingRequests.Clear();

    /// <summary>
    /// 处理收到的 JSON-RPC 消息 — 路由到请求处理器/通知处理器/匹配 pending 响应
    /// </summary>
    /// <param name="json">JSON-RPC 消息字符串</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="sendJsonAsync">发送 JSON 字符串的回调（用于发送响应）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public async Task ProcessMessageAsync(string json, CancellationToken cancellationToken, Func<string, CancellationToken, Task> sendJsonAsync, ILogger? logger = null) {
        try {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                return;

            if (obj.TryGetPropertyValue("id", out var idNode) && idNode is not null) {
                if (obj.TryGetPropertyValue("method", out var methodNode) && methodNode is not null) {
                    var id = idNode.GetValue<string>();
                    var method = methodNode.GetValue<string>();
                    var @params = obj.TryGetPropertyValue("params", out var p) ? p : null;

                    if (_handlers.TryGetValue(method, out var entry) && entry.Request is not null) {
                        try {
                            var result = await entry.Request(id, @params, cancellationToken).ConfigureAwait(false);
                            await SendResponseAsync(id, result, null, cancellationToken, sendJsonAsync).ConfigureAwait(false);
                        } catch (Exception ex) {
                            await SendResponseAsync(id, null, new LspJsonRpcError { Code = -32603, Message = ex.Message }, cancellationToken, sendJsonAsync).ConfigureAwait(false);
                        }
                    } else {
                        await SendResponseAsync(id, null, new LspJsonRpcError { Code = -32601, Message = $"Method not found: {method}" }, cancellationToken, sendJsonAsync).ConfigureAwait(false);
                    }
                    return;
                }

                {
                    var id = idNode.GetValue<string>();

                    if (_pendingRequests.TryGetValue(id, out var tcs)) {
                        if (obj.TryGetPropertyValue("result", out var resultNode)) {
                            tcs.TrySetResult(resultNode);
                        } else if (obj.TryGetPropertyValue("error", out var errorNode)) {
                            tcs.TrySetException(new InvalidOperationException($"LSP错误: {errorNode?.ToJsonString()}"));
                        } else {
                            tcs.TrySetResult(null);
                        }

                        _pendingRequests.TryRemove(id, out _);
                    }
                }
            } else if (obj.TryGetPropertyValue("method", out var notifMethodNode) && notifMethodNode is not null) {
                var method = notifMethodNode.GetValue<string>();
                var @params = obj.TryGetPropertyValue("params", out var p) ? p : null;

                NotificationReceived?.Invoke(this, (method, @params));

                if (_handlers.TryGetValue(method, out var entry) && entry.Notification is not null) {
                    await entry.Notification(@params, cancellationToken).ConfigureAwait(false);
                }
            }
        } catch (Exception ex) {
            logger?.LogError(ex, "处理LSP消息失败: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    private static async Task SendResponseAsync(string id, JsonNode? result, LspJsonRpcError? error, CancellationToken cancellationToken, Func<string, CancellationToken, Task> sendJsonAsync) {
        var response = new Dictionary<string, JsonElement> {
            ["jsonrpc"] = JsonElementHelper.FromString("2.0"),
            ["id"] = JsonElementHelper.FromString(id)
        };
        if (error != null) {
            response["error"] = JsonElementHelper.FromObject(error, LspJsonContext.Default.LspJsonRpcError);
        } else {
            response["result"] = result is null
                ? JsonElementHelper.NullElement()
                : JsonNodeToElement(result);
        }

        var json = JsonSerializer.Serialize(response, LspJsonContext.Default.DictionaryStringJsonElement);
        await sendJsonAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement JsonNodeToElement(JsonNode node) {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }
}

/// <summary>
/// LSP 方法处理器 — 聚合通知处理器和请求处理器,按方法名索引
/// LSP 协议中一个方法名要么是通知要么是请求,不会两者都是
/// </summary>
internal sealed record LspMethodHandler(
    Func<JsonNode?, CancellationToken, ValueTask>? Notification = null,
    Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>>? Request = null
);