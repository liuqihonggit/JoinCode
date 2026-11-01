namespace NativeTui.Core.Bridge;

/// <summary>
/// 惰性引擎 — TASK078 启动性能优化：首帧用占位引擎构造 ReplShell（~70ms），
/// 后台预热真实引擎（EngineSession.CreateAsync ~958ms），就绪后 <see cref="AttachReal"/> 注入。
/// 引擎未就绪期间 Submit 排队（用户首帧可输入+提交，就绪后回放），其余 no-op/空。
/// </summary>
internal sealed class LazyEngine : IReplEngine
{
    private IReplEngine? _real;
    private readonly Queue<(string Text, IReadOnlyList<BridgeImageAttachment>? Images)> _pendingSubmit = new();
    private readonly Lock _gate = new();

    /// <summary>注入真实引擎并回放排队的提交（幂等 — 重复调用忽略）。</summary>
    public void AttachReal(IReplEngine real)
    {
        lock (_gate)
        {
            if (_real is not null) return;
            _real = real;
            while (_pendingSubmit.TryDequeue(out var msg))
                _real.Submit(msg.Text, msg.Images);
        }
    }

    /// <summary>引擎是否就绪（真实引擎已注入）。</summary>
    public bool IsReady => Volatile.Read(ref _real) is not null;

    public void Submit(string text) => Submit(text, null);

    public void Submit(string text, IReadOnlyList<BridgeImageAttachment>? images)
    {
        lock (_gate)
        {
            if (_real is { } real) real.Submit(text, images);
            else _pendingSubmit.Enqueue((text, images));
        }
    }

    public void Interrupt() => Volatile.Read(ref _real)?.Interrupt();

    public void SetPermissionMode(string mode) => Volatile.Read(ref _real)?.SetPermissionMode(mode);

    public IReadOnlyList<BridgeEvent> DrainEvents() => Volatile.Read(ref _real)?.DrainEvents() ?? Array.Empty<BridgeEvent>();

    public IReadOnlyList<string> DrainDiagnostics() => Volatile.Read(ref _real)?.DrainDiagnostics() ?? Array.Empty<string>();

    public void StopBackgroundTask(string kind, string taskId) => Volatile.Read(ref _real)?.StopBackgroundTask(kind, taskId);

    public void StartNewConversation() => Volatile.Read(ref _real)?.StartNewConversation();

    public bool IsAlive => Volatile.Read(ref _real)?.IsAlive ?? true;
}
