namespace Core.Utils;

/// <summary>
/// 网状拓扑传输 — 点对点直连，每进程一个独立管道，无中心转发。
/// <para>每进程创建自己的 <see cref="NamedPipeServerStream"/>（管道名 = baseName + "-" + PID），接收其他进程的连接。</para>
/// <para>发送到目标进程时，连接目标进程的 server pipe（连接缓存复用），直接点对点传输。</para>
/// <para>无主机选举：所有进程平等，<see cref="Role"/> 恒为 <see cref="ProcessRole.Host"/>，<see cref="HostProcessId"/> = <see cref="ProcessId"/>。</para>
/// <para>消息协议：复用 <see cref="BinaryProtocol"/> 二进制长度前缀协议。</para>
/// <para>连接管理：<see cref="ConcurrentDictionary{TKey, TValue}"/> 缓存到各 peer 的客户端连接，首次发送时建立。</para>
/// <para>广播：遍历所有已连接 peer，逐个点对点发送。</para>
/// </summary>
public sealed class MeshTransport : ITransportTopology
{
    private readonly string _basePipeName;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, MeshPeerConnection> _peerConnections;
    private readonly Channel<TransportFrame> _receiveChannel;
    private readonly CancellationTokenSource _cts;
    private readonly string _processId;
    private Task? _acceptTask;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 构造网状拓扑传输。
    /// </summary>
    /// <param name="basePipeName">管道名称前缀（实际管道名 = basePipeName + "-" + PID）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="processId">进程标识（默认 Environment.ProcessId，测试可注入）</param>
    public MeshTransport(
        string basePipeName = "jcc-mesh",
        ILogger? logger = null,
        string? processId = null)
    {
        _basePipeName = basePipeName;
        _logger = logger;
        _processId = processId ?? Environment.ProcessId.ToString();
        _peerConnections = new ConcurrentDictionary<string, MeshPeerConnection>();
        _receiveChannel = Channel.CreateUnbounded<TransportFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
    }

    /// <inheritdoc/>
    public TransportTopology Kind => TransportTopology.Mesh;

    /// <inheritdoc/>
    public ProcessRole Role => ProcessRole.Host;

    /// <inheritdoc/>
    public string ProcessId => _processId;

    /// <inheritdoc/>
    public string HostProcessId => ProcessId;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _started) != 0;

    /// <summary>本进程的管道名称 — basePipeName + "-" + PID。</summary>
    public string MyPipeName => $"{_basePipeName}-{ProcessId}";

    /// <summary>
    /// 计算目标进程的管道名称。
    /// </summary>
    /// <param name="peerPid">目标进程标识</param>
    /// <returns>目标进程的管道名称</returns>
    public string GetPeerPipeName(string peerPid) => $"{_basePipeName}-{peerPid}";

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        Interlocked.Exchange(ref _started, 1);
        _logger?.LogInformation("MeshTransport: started on pipe {Pipe} (pid={Pid})", MyPipeName, ProcessId);
        _acceptTask = Task.Run(() => PipeAcceptLoop.RunAsync(MyPipeName, HandlePeerConnectionAsync, _cts.Token));
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(string targetProcessId, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (targetProcessId == ProcessId)
        {
            _receiveChannel.Writer.TryWrite(new TransportFrame(ProcessId, data));
            return;
        }

        var conn = await GetOrCreatePeerConnectionAsync(targetProcessId, ct).ConfigureAwait(false);
        if (conn is null) return;

        var envelope = BinaryProtocol.Encode(MessageType.Data, ProcessId, targetProcessId, data);
        await conn.WriteAsync(envelope, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var envelope = BinaryProtocol.Encode(MessageType.Broadcast, ProcessId, null, data);

        foreach (var kvp in _peerConnections)
        {
            await kvp.Value.WriteAsync(envelope, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TransportFrame> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnectedProcesses() => _peerConnections.Keys.ToArray();

    /// <summary>
    /// 主动添加 peer 连接 — 连接到指定进程的 server pipe。
    /// <para>通常发送时自动建立连接，此方法用于预连接或测试。</para>
    /// </summary>
    /// <param name="peerPid">目标进程标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功连接</returns>
    public async ValueTask<bool> AddPeerAsync(string peerPid, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (peerPid == ProcessId) return true;
        var conn = await GetOrCreatePeerConnectionAsync(peerPid, ct).ConfigureAwait(false);
        return conn is not null;
    }

    /// <summary>
    /// 获取或创建到目标进程的连接 — 连接缓存复用。
    /// </summary>
    private async ValueTask<MeshPeerConnection?> GetOrCreatePeerConnectionAsync(string peerPid, CancellationToken ct)
    {
        if (_peerConnections.TryGetValue(peerPid, out var existing) && existing.IsConnected)
        {
            return existing;
        }

        var pipeName = GetPeerPipeName(peerPid);
        var client = NamedPipeFactory.CreateClient(pipeName);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            _logger?.LogDebug("MeshTransport: failed to connect to peer {Peer} on {Pipe}", peerPid, pipeName);
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var handshake = BinaryProtocol.Encode(
            MessageType.Control,
            ProcessId,
            peerPid,
            Encoding.UTF8.GetBytes($"PEER:{ProcessId}"));
        await client.WriteAsync(handshake, ct).ConfigureAwait(false);

        var conn = new MeshPeerConnection(peerPid, client, _logger);
        _peerConnections[peerPid] = conn;
        _logger?.LogDebug("MeshTransport: connected to peer {Peer} on {Pipe}", peerPid, pipeName);
        return conn;
    }

    /// <summary>
    /// 接受连接循环 — 已提取到 PipeAcceptLoop.RunAsync,此处保留 HandlePeerConnectionAsync 处理单个连接。
    /// </summary>
    private async Task HandlePeerConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        string? peerPid = null;
        try
        {
            var firstMsg = await BinaryProtocol.ReadAsync(server, ct).ConfigureAwait(false);
            TransportDiagnostics.Log("MESH", () => $"recv handshake: type={firstMsg.Type}, payload={Encoding.UTF8.GetString(firstMsg.Payload.Span)}");
            if (firstMsg.Type == MessageType.Control)
            {
                var text = Encoding.UTF8.GetString(firstMsg.Payload.Span);
                if (text.StartsWith("PEER:", StringComparison.Ordinal))
                {
                    peerPid = text["PEER:".Length..].Trim();
                }
            }

            if (peerPid is not null)
            {
                TransportDiagnostics.Log("MESH", () => $"peer {peerPid} connected, entering read loop");
                _logger?.LogDebug("MeshTransport: peer {Peer} connected", peerPid);
            }

            await foreach (var msg in BinaryProtocol.ReadStreamAsync(server, ct).ConfigureAwait(false))
            {
                TransportDiagnostics.Log("MESH", () => $"recv msg: type={msg.Type}, source={msg.SourcePid}, len={msg.Payload.Length}");
                if (msg.Type is MessageType.Data or MessageType.Broadcast)
                {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));
                }
            }
        }
        catch (OperationCanceledException) { TransportDiagnostics.Log("MESH", () => $"HandlePeerConnection cancelled, peer={peerPid}"); }
        catch (Exception ex)
        {
            TransportDiagnostics.Log("MESH", () => $"HandlePeerConnection error: {ex.GetType().Name}: {ex.Message}, peer={peerPid}");
            _logger?.LogError(ex, "MeshTransport: peer connection error for {Peer}", peerPid);
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MeshTransport));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _receiveChannel.Writer.TryComplete();

        var conns = _peerConnections.Values.ToArray();
        _peerConnections.Clear();

        await _acceptTask.AwaitBackgroundTaskSafe();
        Cleanup(conns, _cts);
    }

    private static void Cleanup(IAsyncDisposable[] conns, CancellationTokenSource cts)
    {
        foreach (var conn in conns)
        {
            conn.DisposeAsync().AsTask().Wait();
        }
        cts.Dispose();
    }
}

/// <summary>网状拓扑 peer 连接 — 封装到单个 peer 的客户端连接，用 Channel 串行化写入（无锁 Actor 模型）。</summary>
internal sealed class MeshPeerConnection : IAsyncDisposable
{
    private readonly NamedPipeClientStream _stream;
    private readonly ILogger? _logger;
    private readonly Channel<ReadOnlyMemory<byte>> _writeQueue;
    private readonly Task _writeLoop;
    private int _disposed;

    public string PeerProcessId { get; }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _stream.IsConnected;

    public MeshPeerConnection(string peerPid, NamedPipeClientStream stream, ILogger? logger)
    {
        PeerProcessId = peerPid;
        _stream = stream;
        _logger = logger;
        _writeQueue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) != 0) return ValueTask.CompletedTask;
        return _writeQueue.Writer.WriteAsync(data, ct);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var data in _writeQueue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                TransportDiagnostics.Log("MESH-WRITE", () => $"consume: peer={PeerProcessId}, len={data.Length}");
                try
                {
                    await _stream.WriteAsync(data).ConfigureAwait(false);
                    await _stream.FlushAsync().ConfigureAwait(false);
                    TransportDiagnostics.Log("MESH-WRITE", () => $"write ok: peer={PeerProcessId}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TransportDiagnostics.Log("MESH-WRITE", () => $"write fail: peer={PeerProcessId}, {ex.GetType().Name}: {ex.Message}");
                    _logger?.LogWarning(ex, "MeshPeerConnection: write failed for peer {Pid}", PeerProcessId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _writeQueue.Writer.TryComplete();
        await _writeLoop.ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
