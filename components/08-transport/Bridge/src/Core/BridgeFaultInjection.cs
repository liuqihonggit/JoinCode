
namespace Core.Bridge;

// BridgeFaultKind, BridgeFault 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// Bridge 调试句柄接口 — 对齐 TS 端 BridgeDebugHandle
/// </summary>
public interface IBridgeDebugHandle
{
    /// <summary>模拟连接关闭</summary>
    void FireClose();

    /// <summary>强制重连</summary>
    void ForceReconnect();

    /// <summary>注入故障</summary>
    void InjectFault(BridgeFault fault);

    /// <summary>唤醒工作轮询循环</summary>
    void WakePollLoop();

    /// <summary>描述当前调试状态</summary>
    string Describe();
}

/// <summary>
/// Bridge 故障注入控制器 — 对齐 TS 端 bridgeDebug.ts
/// 仅限内部(ant)使用，通过模块级变量维护故障队列
/// </summary>
public static class BridgeDebugController
{
    private static IBridgeDebugHandle? _handle;
    private static readonly List<BridgeFault> _faultQueue = [];
    private static readonly object _lock = new();

    /// <summary>注册调试句柄</summary>
    public static void RegisterHandle(IBridgeDebugHandle handle)
    {
        lock (_lock)
        {
            _handle = handle;
        }
    }

    /// <summary>清除调试句柄和故障队列</summary>
    public static void ClearHandle()
    {
        lock (_lock)
        {
            _handle = null;
            _faultQueue.Clear();
        }
    }

    /// <summary>获取当前调试句柄</summary>
    public static IBridgeDebugHandle? GetHandle()
    {
        lock (_lock)
        {
            return _handle;
        }
    }

    /// <summary>向故障队列注入一个故障</summary>
    public static void InjectFault(BridgeFault fault)
    {
        lock (_lock)
        {
            _faultQueue.Add(fault);
        }
    }

    /// <summary>
    /// 尝试消费匹配的故障 — 由 FaultInjectionBridgeApiClient 调用
    /// 返回 null 表示无匹配故障
    /// </summary>
    internal static BridgeFault? TryConsumeFault(string method)
    {
        lock (_lock)
        {
            for (var i = _faultQueue.Count - 1; i >= 0; i--)
            {
                var fault = _faultQueue[i];
                if (!string.Equals(fault.Method, method, StringComparison.OrdinalIgnoreCase)) continue;

                // RemainingCount > 0 时递减，到 0 时移除
                if (fault.RemainingCount > 1)
                {
                    fault.RemainingCount--;
                    return fault;
                }

                // Swap-and-Pop: O(1) 删除，避免 RemoveAt 的 O(n)
                var lastIndex = _faultQueue.Count - 1;
                if (i != lastIndex)
                {
                    _faultQueue[i] = _faultQueue[lastIndex];
                }

                _faultQueue.RemoveAt(lastIndex);
                return fault;
            }

            return null;
        }
    }
}

/// <summary>
/// 故障注入装饰器 — 包装 BridgeApiClient，在调用前检查故障队列
/// 对齐 TS 端 wrapApiForFaultInjection
/// 使用组合模式（BridgeApiClient 是 sealed）
/// </summary>
public sealed class FaultInjectionBridgeApiClient : IDisposable
{
    private readonly BridgeApiClient _inner;

    public FaultInjectionBridgeApiClient(BridgeApiClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>检查并消费匹配的故障，有则抛出</summary>
    private void CheckFault(string method)
    {
        var fault = BridgeDebugController.TryConsumeFault(method);
        if (fault is null) return;

        if (fault.Kind == BridgeFaultKind.Fatal)
        {
            throw new BridgeFatalError($"Injected fatal fault: {fault.ErrorType}", fault.Status, fault.ErrorType ?? "injected_fatal");
        }

        throw new HttpRequestException(
            $"Injected transient fault: {fault.Status} {fault.ErrorType}",
            null, System.Net.HttpStatusCode.InternalServerError);
    }

    public Task<BridgeWorkItem?> PollForWorkAsync(string environmentId, CancellationToken ct, int? reclaimOlderThanMs = null)
    {
        CheckFault("pollForWork");
        return _inner.PollForWorkAsync(environmentId, ct, reclaimOlderThanMs);
    }

    public Task<BridgeEnvironmentRegistrationResponse?> RegisterBridgeEnvironmentAsync(
        BridgeEnvironmentRegistration registration, CancellationToken ct)
    {
        CheckFault("registerBridgeEnvironment");
        return _inner.RegisterBridgeEnvironmentAsync(registration, ct);
    }

    public Task<BridgeReconnectResponse?> ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken ct)
    {
        CheckFault("reconnectSession");
        return _inner.ReconnectSessionAsync(environmentId, sessionId, ct);
    }

    public Task<BridgeHeartbeatResponse?> HeartbeatWorkAsync(
        string environmentId, string workId, string? sessionToken = null, CancellationToken ct = default)
    {
        CheckFault("heartbeatWork");
        return _inner.HeartbeatWorkAsync(environmentId, workId, sessionToken, ct);
    }

    public void Dispose() => _inner.Dispose();
}
