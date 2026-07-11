namespace JoinCode.Transport;

/// <summary>
/// 通用传输基类 — 提供生命周期管理、发送锁、事件触发的标准实现
/// </summary>
/// <remarks>
/// 子类只需实现 ConnectCoreAsync/DisconnectCoreAsync/SendCoreAsync 三个核心方法。
/// 认证逻辑留在子类（如 MCP 的 IMcpAuthProvider），基类不感知。
/// </remarks>
public abstract class TransportBase : ITransport
{
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    /// <inheritdoc/>
    public bool IsRunning { get; protected set; }

    /// <inheritdoc/>
    public event EventHandler<TransportPayloadEventArgs>? PayloadReceived;

    /// <inheritdoc/>
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    /// <inheritdoc/>
    public event EventHandler? ConnectionClosed;

    /// <summary>子类实现：建立连接</summary>
    protected abstract Task ConnectCoreAsync(CancellationToken ct);

    /// <summary>子类实现：断开连接</summary>
    protected abstract Task DisconnectCoreAsync(CancellationToken ct);

    /// <summary>子类实现：发送字节载荷</summary>
    protected abstract Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>获取或设置后台任务（子类在 StartAsync 中设置）</summary>
    protected Task? BackgroundTask
    {
        get => _backgroundTask;
        set => _backgroundTask = value;
    }

    /// <summary>获取取消令牌源（子类在 StartAsync 中创建）</summary>
    protected CancellationTokenSource? Cts => _cts;

    /// <summary>创建新的 CTS 并返回令牌</summary>
    protected CancellationToken CreateCtsAndToken()
    {
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    /// <inheritdoc/>
    public virtual async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        await ConnectCoreAsync(ct).ConfigureAwait(false);
        IsRunning = true;
    }

    /// <inheritdoc/>
    public virtual async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;

        await DisconnectCoreAsync(ct).ConfigureAwait(false);
        await GracefulStopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendCoreAsync(payload, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>通用断开流程：取消 CTS → 等待后台任务 → 释放资源 → 触发事件</summary>
    protected async Task GracefulStopAsync(CancellationToken ct = default)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
        }

        _cts?.Dispose();
        _cts = null;
        _backgroundTask = null;

        OnConnectionClosed();
    }

    /// <summary>触发 PayloadReceived 事件</summary>
    protected void OnPayloadReceived(ReadOnlyMemory<byte> payload)
    {
        PayloadReceived?.Invoke(this, new TransportPayloadEventArgs(payload));
    }

    /// <summary>触发 ErrorOccurred 事件</summary>
    protected void OnErrorOccurred(Exception exception)
    {
        ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(exception));
    }

    /// <summary>触发 ConnectionClosed 事件</summary>
    protected void OnConnectionClosed()
    {
        ConnectionClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await StopAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
