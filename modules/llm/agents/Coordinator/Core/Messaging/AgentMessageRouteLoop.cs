namespace Core.Agents.Coordinator;

/// <summary>
/// 子代理消息路由循环（TASK062 P3 单一职责提取）— 邮箱消息 → 处理器的常驻路由小类：
/// 三个路由器（AgentServiceImpl worker 权限响应 / SwarmPermissionMessageRouter worker 响应 /
/// PlanApprovalMessageRouter teammate 响应）的循环复制收敛，"ct 传入 ReceiveAsync" 收敛为
/// 类内一处构造契约（漏传在结构上不可能——防 ReceiveAsync 漏传 ct 缺陷模式再犯，
/// 该模式曾致 Host.DisposeAsync 永挂：MailboxBase.ReadAllAsync(ct) 无令牌时无消息永不退出）。
/// 生命周期：Start 单次启动；DisposeAsync 取消 → 无超时等待循环收尾（幂等）。
/// </summary>
internal sealed class AgentMessageRouteLoop : IAsyncDisposable
{
    private readonly IMailbox _broker;
    private readonly string _agentId;
    private readonly Func<CoordinatorMessage, CancellationToken, Task> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _disposed;

    /// <summary>构造 — broker=邮箱；agentId=接收方路由键；handler=消息处理器（收到消息时调用，异常由调用方语义负责）。</summary>
    public AgentMessageRouteLoop(IMailbox broker, string agentId, Func<CoordinatorMessage, CancellationToken, Task> handler)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>启动路由循环（单次语义 — 二次调用 no-op）。</summary>
    public void Start()
    {
        if (_loop is not null || _disposed)
        {
            return;
        }

        _loop = RunAsync(_cts.Token);
    }

    /// <summary>循环主体 — ct 传入 ReceiveAsync（MailboxBase 底层 ReadAllAsync(ct) 随取消退出 —
    /// 本类存在的唯一理由：把这行契约收敛到一处，三个路由器共享不再散落复制）。</summary>
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _broker.ReceiveAsync(_agentId, ct).ConfigureAwait(false))
            {
                await _handler(message, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消收尾 — Dispose 触发的正常退出路径
        }
    }

    /// <summary>收尾 — 取消循环 → 无超时等待完成（幂等）。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}
