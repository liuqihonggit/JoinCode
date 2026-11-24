namespace NativeTui.Core.Bridge;

/// <summary>
/// 惰性引擎 — TASK078 启动性能优化：首帧用占位引擎构造 ReplShell（~70ms），
/// 后台预热真实引擎（EngineSession.CreateAsync ~958ms），就绪后 <see cref="AttachReal"/> 注入。
/// 引擎未就绪期间 Submit 排队（用户首帧可输入+提交，就绪后回放），其余 no-op/空。
/// </summary>
internal sealed class LazyEngine : IReplEngine
{
    private IReplEngine? _real;
    private readonly ConcurrentQueue<(string Text, IReadOnlyList<BridgeImageAttachment>? Images, string QueryId)> _pendingSubmit = new();
    private readonly ConcurrentQueue<BridgeEvent> _pendingEvents = new();
    private int _draining;

    /// <summary>注入真实引擎并回放排队的提交（幂等 — 重复调用忽略；CAS 单次发布）。</summary>
    public void AttachReal(IReplEngine real)
    {
        if (Interlocked.CompareExchange(ref _real, real, null) is not null) return;
        FlushPendingSubmits(real);
    }

    /// <summary>引擎是否就绪（真实引擎已注入）。</summary>
    public bool IsReady => Volatile.Read(ref _real) is not null;

    /// <summary>TASK078 — 引擎加载失败时入队错误事件（DrainEvents 返回给 TUI 显示 + IsAlive=false 触发退出）。</summary>
    public void EnqueueError(string message)
    {
        if (Volatile.Read(ref _real) is null) _pendingEvents.Enqueue(new BridgeEvent.Error(message));
    }

    public string Submit(string text) => Submit(text, null, null);

    public string Submit(string text, IReadOnlyList<BridgeImageAttachment>? images, string? queryId = null)
    {
        var effective = string.IsNullOrWhiteSpace(queryId) ? QueryIdFactory.New() : queryId;
        // 一律先入队再冲刷：FIFO 保证预热期排队的消息先于就绪后的新消息送达；queryId 随排队保留（ADR 0165）
        _pendingSubmit.Enqueue((text, images, effective));
        if (Volatile.Read(ref _real) is { } real) FlushPendingSubmits(real);
        return effective;
    }

    /// <summary>单冲刷者 CAS — 多生产者同一时刻只有一个在调用 real.Submit，保持 FIFO 顺序。</summary>
    private void FlushPendingSubmits(IReplEngine real)
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
        try
        {
            while (_pendingSubmit.TryDequeue(out var msg)) real.Submit(msg.Text, msg.Images, msg.QueryId);
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
        }

        // 释放冲刷权后再确认一次：避免入队发生在最后一次 TryDequeue 之后被遗落
        if (!_pendingSubmit.IsEmpty) FlushPendingSubmits(real);
    }

    public void Interrupt(string? queryId = null) => Volatile.Read(ref _real)?.Interrupt(queryId);

    public void SetPermissionMode(string mode) => Volatile.Read(ref _real)?.SetPermissionMode(mode);

    public IReadOnlyList<BridgeEvent> DrainEvents()
    {
        var events = new List<BridgeEvent>();
        while (_pendingEvents.TryDequeue(out var evt)) events.Add(evt);
        if (Volatile.Read(ref _real) is { } real) events.AddRange(real.DrainEvents());
        return events.Count == 0 ? Array.Empty<BridgeEvent>() : events;
    }

    public IReadOnlyList<string> DrainDiagnostics() => Volatile.Read(ref _real)?.DrainDiagnostics() ?? Array.Empty<string>();

    public void StopBackgroundTask(string kind, string taskId) => Volatile.Read(ref _real)?.StopBackgroundTask(kind, taskId);

    public void StartNewConversation() => Volatile.Read(ref _real)?.StartNewConversation();

    public bool IsAlive => Volatile.Read(ref _real)?.IsAlive ?? true;
}
