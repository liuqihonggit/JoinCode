
namespace Core.Bridge;

// BridgeFaultKind, BridgeFault 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// Bridge 调试句柄接口 — 对齐 TS 端 BridgeDebugHandle
/// </summary>
public interface IBridgeDebugHandle {
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
/// 无锁实现：volatile 句柄 + ImmutableList&lt;BridgeFault&gt; + Interlocked.CompareExchange CAS 循环
/// </summary>
public static class BridgeDebugController {
    private static IBridgeDebugHandle? _handle;
    private static ImmutableList<BridgeFault> _faultQueue = ImmutableList<BridgeFault>.Empty;

    /// <summary>注册调试句柄</summary>
    public static void RegisterHandle(IBridgeDebugHandle handle)
        => Volatile.Write(ref _handle, handle);

    /// <summary>清除调试句柄和故障队列</summary>
    public static void ClearHandle() {
        Volatile.Write(ref _handle, null);
        Interlocked.Exchange(ref _faultQueue, ImmutableList<BridgeFault>.Empty);
    }

    /// <summary>获取当前调试句柄</summary>
    public static IBridgeDebugHandle? GetHandle()
        => Volatile.Read(ref _handle);

    /// <summary>向故障队列注入一个故障（CAS 循环无锁追加）</summary>
    public static void InjectFault(BridgeFault fault) {
        while (true) {
            var snapshot = Volatile.Read(ref _faultQueue);
            var updated = snapshot.Add(fault);
            if (Interlocked.CompareExchange(ref _faultQueue, updated, snapshot) == snapshot) return;
        }
    }

    /// <summary>
    /// 尝试消费匹配的故障 — 由 FaultInjectionBridgeApiClient 调用
    /// 返回 null 表示无匹配故障。CAS 循环无锁消费，并发安全
    /// </summary>
    internal static BridgeFault? TryConsumeFault(string method) {
        while (true) {
            var snapshot = Volatile.Read(ref _faultQueue);
            var matchIndex = FindMatchIndex(snapshot, method);
            if (matchIndex < 0) return null;

            var fault = snapshot[matchIndex];
            var updated = fault.RemainingCount > 1
                ? snapshot.SetItem(matchIndex, fault with { RemainingCount = fault.RemainingCount - 1 })
                : snapshot.RemoveAt(matchIndex);

            if (Interlocked.CompareExchange(ref _faultQueue, updated, snapshot) == snapshot) return fault;
        }
    }

    private static int FindMatchIndex(ImmutableList<BridgeFault> queue, string method) {
        for (var i = 0; i < queue.Count; i++) {
            if (string.Equals(queue[i].Method, method, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}

/// <summary>
/// 故障注入装饰器 — 包装 BridgeApiClient，在调用前检查故障队列
/// 对齐 TS 端 wrapApiForFaultInjection
/// 使用组合模式（BridgeApiClient 是 sealed）
/// </summary>
public sealed class FaultInjectionBridgeApiClient : IDisposable {
    private readonly BridgeApiClient _inner;

    /// <summary>
    /// 构造故障注入装饰器
    /// </summary>
    /// <param name="inner">被包装的真实 Bridge API 客户端</param>
    public FaultInjectionBridgeApiClient(BridgeApiClient inner) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>检查并消费匹配的故障，有则抛出</summary>
    private void CheckFault(string method) {
        var fault = BridgeDebugController.TryConsumeFault(method);
        if (fault is null) return;

        if (fault.Kind == BridgeFaultKind.Fatal) {
            throw new BridgeFatalError($"Injected fatal fault: {fault.ErrorType}", fault.Status, fault.ErrorType ?? "injected_fatal");
        }

        throw new HttpRequestException(
            $"Injected transient fault: {fault.Status} {fault.ErrorType}",
            null, System.Net.HttpStatusCode.InternalServerError);
    }

    /// <summary>
    /// 轮询工作项 — 委托给内部客户端,调用前检查故障注入
    /// </summary>
    /// <param name="environmentId">环境标识</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="reclaimOlderThanMs">回收超过此毫秒的旧工作项(可选)</param>
    /// <returns>工作项,无工作时返回 null</returns>
    public Task<BridgeWorkItem?> PollForWorkAsync(string environmentId, CancellationToken ct, int? reclaimOlderThanMs = null) {
        CheckFault("pollForWork");
        return _inner.PollForWorkAsync(environmentId, ct, reclaimOlderThanMs);
    }

    /// <summary>
    /// 注册桥接环境 — 委托给内部客户端,调用前检查故障注入
    /// </summary>
    /// <param name="registration">环境注册信息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>注册响应,失败时返回 null</returns>
    public Task<BridgeEnvironmentRegistrationResponse?> RegisterBridgeEnvironmentAsync(
        BridgeEnvironmentRegistration registration, CancellationToken ct) {
        CheckFault("registerBridgeEnvironment");
        return _inner.RegisterBridgeEnvironmentAsync(registration, ct);
    }

    /// <summary>
    /// 重连会话 — 委托给内部客户端,调用前检查故障注入
    /// </summary>
    /// <param name="environmentId">环境标识</param>
    /// <param name="sessionId">会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>重连响应,失败时返回 null</returns>
    public Task<BridgeReconnectResponse?> ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken ct) {
        CheckFault("reconnectSession");
        return _inner.ReconnectSessionAsync(environmentId, sessionId, ct);
    }

    /// <summary>
    /// 心跳保活 — 委托给内部客户端,调用前检查故障注入
    /// </summary>
    /// <param name="environmentId">环境标识</param>
    /// <param name="workId">工作项标识</param>
    /// <param name="sessionToken">会话令牌(可选)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>心跳响应,失败时返回 null</returns>
    public Task<BridgeHeartbeatResponse?> HeartbeatWorkAsync(
        string environmentId, string workId, string? sessionToken = null, CancellationToken ct = default) {
        CheckFault("heartbeatWork");
        return _inner.HeartbeatWorkAsync(environmentId, workId, sessionToken, ct);
    }

    /// <summary>
    /// 释放内部客户端资源
    /// </summary>
    public void Dispose() => _inner.Dispose();
}