
namespace Core.Bridge;

/// <summary>
/// Bridge 权限回调接口 — 继承 IPermissionCallbacks 并扩展 Bridge 专用类型
/// </summary>
public interface IBridgePermissionCallbacks : IPermissionCallbacks {
}

/// <summary>
/// Bridge 权限回调服务 — 基于 IReplBridgeTransport 发送
/// </summary>
public sealed class BridgePermissionCallbackService : IBridgePermissionCallbacks {
    private readonly IReplBridgeTransport _transport;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<Func<PermissionCallbackResponse, Task>>> _handlers = new();
    private readonly AsyncLock _semaphore = new();
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// 构造权限回调服务
    /// </summary>
    /// <param name="transport">桥接传输,用于发送权限消息</param>
    /// <param name="logger">日志记录器(可选)</param>
    public BridgePermissionCallbackService(IReplBridgeTransport transport, ILogger? logger = null) {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger;
    }

    /// <summary>
    /// 发送权限请求 — 手写 JSON 构建,通过传输写入
    /// </summary>
    /// <param name="requestId">请求标识</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="input">工具输入参数</param>
    /// <param name="toolUseId">工具使用标识</param>
    /// <param name="description">权限请求描述</param>
    /// <param name="suggestions">权限建议列表(可选)</param>
    /// <param name="blockedPath">被阻止的路径(可选)</param>
    public void SendRequest(string requestId, string toolName, Dictionary<string, JsonElement> input,
        string toolUseId, string description, List<PermissionCallbackUpdate>? suggestions = null, string? blockedPath = null) {
        var dto = new BridgePermissionRequestDto {
            RequestId = requestId,
            Request = new BridgePermissionRequestBodyDto {
                ToolName = toolName,
                ToolUseId = toolUseId,
                Description = description,
                PermissionSuggestions = suggestions is not null && suggestions.Count > 0
                    ? suggestions.Select(s => new BridgePermissionSuggestionDto {
                        ToolName = s.ToolName ?? string.Empty,
                        PermissionMode = s.PermissionMode ?? string.Empty
                    }).ToList()
                    : null,
                BlockedPath = blockedPath
            }
        };
        var json = JsonSerializer.Serialize(dto, BridgeJsonContext.Default.BridgePermissionRequestDto);

        _ = _transport.WriteAsync(json, _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 发送权限请求: {RequestId}, Tool={ToolName}", requestId, toolName);
    }

    /// <summary>
    /// 发送权限响应 — 手写 JSON 构建,通过传输写入
    /// </summary>
    /// <param name="requestId">请求标识</param>
    /// <param name="response">权限回调响应</param>
    public void SendResponse(string requestId, PermissionCallbackResponse response) {
        var dto = new BridgePermissionResponseMessageDto {
            RequestId = requestId,
            Response = new BridgePermissionResponseBodyDto {
                Behavior = response.Behavior,
                Message = response.Message
            }
        };
        var json = JsonSerializer.Serialize(dto, BridgeJsonContext.Default.BridgePermissionResponseMessageDto);

        _ = _transport.WriteAsync(json, _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 发送权限响应: {RequestId}, Behavior={Behavior}", requestId, response.Behavior);
    }

    /// <summary>
    /// 取消权限请求 — 通过传输发送取消消息
    /// </summary>
    /// <param name="requestId">请求标识</param>
    public void CancelRequest(string requestId) {
        var json = JsonSerializer.Serialize(
            new BridgePermissionCancelDto { RequestId = requestId, Request = new BridgePermissionCancelBodyDto() },
            BridgeJsonContext.Default.BridgePermissionCancelDto);

        _ = _transport.WriteAsync(json, _disposeCts.Token);
        _logger?.LogDebug("[PermissionCallbacks] 取消权限请求: {RequestId}", requestId);
    }

    /// <summary>
    /// 注册权限响应处理器 — 返回取消订阅函数
    /// </summary>
    /// <param name="requestId">请求标识</param>
    /// <param name="handler">响应处理委托</param>
    /// <returns>取消订阅函数,调用后移除该处理器</returns>
    public Action OnResponse(string requestId, Func<PermissionCallbackResponse, Task> handler) {
        using var guard = _semaphore.LockOrCrash();
        if (!_handlers.TryGetValue(requestId, out var list)) {
            list = new List<Func<PermissionCallbackResponse, Task>>();
            _handlers[requestId] = list;
        }

        list.Add(handler);

        // 返回取消订阅函数
        return () => {
            using var guard = _semaphore.LockOrCrash();
            if (_handlers.TryGetValue(requestId, out var list)) {
                list.Remove(handler);
                if (list.Count == 0) {
                    _handlers.Remove(requestId);
                }
            }
        };
    }

    /// <summary>
    /// 处理收到的权限响应 — 由 BridgeMessaging 调用
    /// </summary>
    public async Task HandleResponseAsync(string requestId, PermissionCallbackResponse response) {
        using var guard = _semaphore.LockOrCrash();

        if (!_handlers.TryGetValue(requestId, out var handlers)) return;

        foreach (var handler in handlers) {
            try { await handler(response).ConfigureAwait(false); } catch (Exception ex) { _logger?.LogWarning(ex, "[BridgePermissionCallbacks] 处理器抛出异常"); }
        }

    }

    /// <summary>
    /// 判断是否为 BridgePermissionResponse — 对齐 TS 端 isBridgePermissionResponse
    /// </summary>
    public static bool IsBridgePermissionResponse(JsonElement value) {
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("behavior", out var behavior)
            && (behavior.ValueEquals(PermissionBehaviorEnumConstants.Allow) || behavior.ValueEquals(PermissionBehaviorEnumConstants.Deny));
    }

}
