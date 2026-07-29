
namespace Core.Bridge;

/// <summary>
/// Bridge 权限回调接口 — 继承 IPermissionCallbacks 并扩展 Bridge 专用类型
/// </summary>
public interface IBridgePermissionCallbacks : IPermissionCallbacks
{
}

/// <summary>
/// Bridge 权限回调服务 — 基于 IReplBridgeTransport 发送
/// </summary>
public sealed class BridgePermissionCallbackService : IBridgePermissionCallbacks
{
    private readonly IReplBridgeTransport _transport;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<Func<PermissionCallbackResponse, Task>>> _handlers = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    public BridgePermissionCallbackService(IReplBridgeTransport transport, ILogger? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger;
    }

    public void SendRequest(string requestId, string toolName, Dictionary<string, JsonElement> input,
        string toolUseId, string description, List<PermissionCallbackUpdate>? suggestions = null, string? blockedPath = null)
    {
        // 构建权限请求消息 — 手写 JSON 避免 AOT 不兼容
        var sb = new StringBuilder(256);
        sb.Append("{\"type\":\"control_request\",\"request_id\":\"")
          .Append(requestId)
          .Append("\",\"request\":{\"subtype\":\"permission_request\",\"tool_name\":\"")
          .Append(toolName)
          .Append("\",\"tool_use_id\":\"")
          .Append(toolUseId)
          .Append("\",\"description\":\"")
          .Append(EscapeJson(description))
          .Append("\"");

        if (suggestions is not null && suggestions.Count > 0)
        {
            sb.Append(",\"permission_suggestions\":[");
            for (var i = 0; i < suggestions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"tool_name\":\"").Append(EscapeJson(suggestions[i].ToolName ?? string.Empty))
                  .Append("\",\"permission_mode\":\"").Append(EscapeJson(suggestions[i].PermissionMode ?? string.Empty))
                  .Append("\"}");
            }
            sb.Append(']');
        }

        if (blockedPath is not null)
        {
            sb.Append(",\"blocked_path\":\"").Append(EscapeJson(blockedPath)).Append("\"");
        }

        sb.Append("}}");

        _ = _transport.WriteAsync(sb.ToString(), _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 发送权限请求: {RequestId}, Tool={ToolName}", requestId, toolName);
    }

    public void SendResponse(string requestId, PermissionCallbackResponse response)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"type\":\"control_response\",\"request_id\":\"")
          .Append(requestId)
          .Append("\",\"response\":{\"behavior\":\"")
          .Append(response.Behavior)
          .Append("\"");

        if (response.Message is not null)
        {
            sb.Append(",\"message\":\"").Append(EscapeJson(response.Message)).Append("\"");
        }

        sb.Append("}}");

        _ = _transport.WriteAsync(sb.ToString(), _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 发送权限响应: {RequestId}, Behavior={Behavior}", requestId, response.Behavior);
    }

    public void CancelRequest(string requestId)
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"type\":\"control_request\",\"request_id\":\"")
          .Append(requestId)
          .Append("\",\"request\":{\"subtype\":\"permission_cancel\"}}");

        _ = _transport.WriteAsync(sb.ToString(), _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 取消权限请求: {RequestId}", requestId);
    }

    public Action OnResponse(string requestId, Func<PermissionCallbackResponse, Task> handler)
    {
        _semaphore.Wait();
        try
        {
            if (!_handlers.TryGetValue(requestId, out var list))
            {
                list = new List<Func<PermissionCallbackResponse, Task>>();
                _handlers[requestId] = list;
            }

            list.Add(handler);
        }
        finally
        {
            _semaphore.Release();
        }

        // 返回取消订阅函数
        return () =>
        {
            _semaphore.Wait();
            try
            {
                if (_handlers.TryGetValue(requestId, out var list))
                {
                    list.Remove(handler);
                    if (list.Count == 0)
                    {
                        _handlers.Remove(requestId);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        };
    }

    /// <summary>
    /// 处理收到的权限响应 — 由 BridgeMessaging 调用
    /// </summary>
    public async Task HandleResponseAsync(string requestId, PermissionCallbackResponse response)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_handlers.TryGetValue(requestId, out var handlers)) return;

            foreach (var handler in handlers)
            {
                try { await handler(response).ConfigureAwait(false); }
                catch (Exception ex) { /* 忽略处理器异常 */ System.Diagnostics.Trace.WriteLine($"[BridgePermissionCallbacks] Handler threw exception: {ex.Message}"); }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 判断是否为 BridgePermissionResponse — 对齐 TS 端 isBridgePermissionResponse
    /// </summary>
    public static bool IsBridgePermissionResponse(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("behavior", out var behavior)
            && (behavior.ValueEquals(PermissionBehaviorConstants.Allow) || behavior.ValueEquals(PermissionBehaviorConstants.Deny));
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
