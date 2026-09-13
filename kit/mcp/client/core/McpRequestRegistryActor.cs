namespace McpClient;

/// <summary>
/// MCP 请求注册表 Actor — 用单消费者 Channel 串行管理 pending requests，消除 AsyncLock 死锁风险。
/// <para>所有可变状态（_pending 字典）由 Consumer 线程独占，无需锁。</para>
/// <para>响应通过 OutputAsync 流直接拉取，无需 TCS 字典。</para>
/// </summary>
public sealed class McpRequestRegistryActor : ActorBase<McpRequestRegistryActor.IRequestCommand, JsonRpcResponse>
{
    private readonly Dictionary<int, TaskCompletionSource<JsonRpcResponse>> _pending = new();
    private readonly ILogger? _logger;

    /// <summary>命令标记接口 — 约束合法命令类型</summary>
    public interface IRequestCommand;

    /// <summary>注册 pending request</summary>
    private sealed record RegisterCommand(int RequestId, TaskCompletionSource<JsonRpcResponse> Tcs) : IRequestCommand;

    /// <summary>响应到达，完成 pending request</summary>
    private sealed record CompleteCommand(int RequestId, JsonRpcResponse Response) : IRequestCommand;

    /// <summary>移除 pending request（异常/超时时）</summary>
    private sealed record RemoveCommand(int RequestId) : IRequestCommand;

    /// <summary>取消所有 pending requests</summary>
    private sealed record CancelAllCommand(CancellationToken CancellationToken) : IRequestCommand;

    /// <summary>构造 McpRequestRegistryActor 实例 — 初始化日志记录器。</summary>
    /// <param name="logger">日志记录器,为 null 时不记录日志。</param>
    public McpRequestRegistryActor(ILogger? logger = null)
        : base()
    {
        _logger = logger;
    }

    /// <summary>处理命令 — 由 Actor 单消费者线程调用,根据命令类型操作 pending 字典。</summary>
    /// <param name="command">待处理命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的值任务。</returns>
    protected override ValueTask HandleAsync(IRequestCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case RegisterCommand reg:
                _pending[reg.RequestId] = reg.Tcs;
                return default;

            case CompleteCommand comp:
                if (_pending.Remove(comp.RequestId, out var pendingTcs))
                {
                    pendingTcs.TrySetResult(comp.Response);
                    TryPublish(comp.Response);
                }
                else
                {
                    _logger?.LogWarning("[MCP] 收到响应但无匹配 pending request: id={Id}", comp.RequestId);
                }
                return default;

            case RemoveCommand rem:
                _pending.Remove(rem.RequestId);
                return default;

            case CancelAllCommand cancel:
                foreach (var tcs in _pending.Values)
                    tcs.TrySetCanceled(cancel.CancellationToken);
                _pending.Clear();
                return default;

            default:
                return default;
        }
    }

    /// <summary>注册 pending request — 由 SendRequestAsync 调用</summary>
    public ValueTask RegisterAsync(int requestId, TaskCompletionSource<JsonRpcResponse> tcs, CancellationToken ct = default)
        => SendAsync(new RegisterCommand(requestId, tcs), ct);

    /// <summary>完成 pending request — 由 ProcessResponseAsync 调用</summary>
    public ValueTask CompleteAsync(int requestId, JsonRpcResponse response, CancellationToken ct = default)
        => SendAsync(new CompleteCommand(requestId, response), ct);

    /// <summary>移除 pending request — 由 SendRequestAsync catch 块调用</summary>
    public ValueTask RemoveAsync(int requestId, CancellationToken ct = default)
        => SendAsync(new RemoveCommand(requestId), ct);

    /// <summary>取消所有 pending requests — 由 CancelPendingRequestsAsync 调用</summary>
    public ValueTask CancelAllAsync(CancellationToken cancellationToken = default)
        => SendAsync(new CancelAllCommand(cancellationToken), cancellationToken);
}
