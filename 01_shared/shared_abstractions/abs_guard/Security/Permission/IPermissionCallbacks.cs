namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限回调接口 — 用于跨组件权限交互（如 Bridge 通信、Swarm 协调）
/// </summary>
public interface IPermissionCallbacks
{
    /// <summary>发送权限请求</summary>
    void SendRequest(string requestId, string toolName, Dictionary<string, JsonElement> input,
        string toolUseId, string description, List<PermissionCallbackUpdate>? suggestions = null, string? blockedPath = null);

    /// <summary>发送权限响应</summary>
    void SendResponse(string requestId, PermissionCallbackResponse response);

    /// <summary>取消权限请求</summary>
    void CancelRequest(string requestId);

    /// <summary>注册权限响应处理器 — 返回取消订阅函数</summary>
    Action OnResponse(string requestId, Func<PermissionCallbackResponse, Task> handler);
}
