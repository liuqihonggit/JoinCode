
namespace Core.Bridge;

// BridgeState 枚举、IReplBridgeHandle 接口已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// 全局桥句柄管理器 — 对齐 TS 端 replBridgeHandle.ts
/// 维护模块级变量，供 React 树外的调用者访问桥方法
/// </summary>
public static class BridgeHandle
{
    private static volatile IReplBridgeHandle? _handle;
    private static ConcurrentSessionService? _sessionService;

    /// <summary>设置并发会话服务（由启动代码调用）</summary>
    public static void SetSessionService(ConcurrentSessionService? service)
    {
        _sessionService = service;
    }

    /// <summary>设置全局桥句柄 — 对齐 TS 端 setReplBridgeHandle</summary>
    public static void SetHandle(IReplBridgeHandle? handle)
    {
        _handle = handle;

        // 对齐 TS 端: setReplBridgeHandle 中调用 updateSessionBridgeId
        // 发布（或清除）bridge session ID 到 PID 文件，以便本地 peer 去重
        var compatId = handle is not null
            ? SessionIdCompat.ToCompatSessionId(handle.SessionId)
            : null;
        _ = _sessionService?.UpdateBridgeSessionIdAsync(compatId);
    }

    /// <summary>获取全局桥句柄 — 对齐 TS 端 getReplBridgeHandle</summary>
    public static IReplBridgeHandle? GetHandle()
    {
        return _handle;
    }

    /// <summary>
    /// 获取当前桥会话的兼容 ID — 对齐 TS 端 getSelfBridgeCompatId
    /// 返回 session_* 兼容格式的 ID，用于去重
    /// </summary>
    public static string? GetSelfCompatId()
    {
        var handle = _handle;
        if (handle is null) return null;

        return SessionIdCompat.ToCompatSessionId(handle.SessionId);
    }
}
