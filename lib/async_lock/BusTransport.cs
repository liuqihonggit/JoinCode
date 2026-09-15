namespace Core.Utils;

/// <summary>
/// 总线拓扑传输 — 共享服务器，多客户端连同一管道，主机中继广播所有消息。
/// <para>主机模式：创建 <see cref="NamedPipeServerStream"/>，接受多个从机连接，收到任何消息后转发给所有其他从机（总线特性）。</para>
/// <para>从机模式：连接主机管道，发送消息到主机，由主机广播给所有其他从机。</para>
/// <para>与星型拓扑（<see cref="NamedPipeTransport"/>）的区别：星型按目标 PID 定向转发，总线广播给所有连接者。</para>
/// <para>与网状拓扑（<see cref="MeshTransport"/>）的区别：网状点对点直连无中心，总线有中心中继。</para>
/// <para>消息协议：复用 <see cref="BinaryProtocol"/> 二进制长度前缀协议。</para>
/// <para>适用场景：少量进程需要共享所有消息（如配置同步、状态广播），无需定向路由。</para>
/// </summary>
public sealed class BusTransport : ITransportTopology
{
    private readonly string _pipeName;
    private readonly HostElectionService _election;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, BusClientConnection> _clientConnections;
    private readonly Channel<TransportFrame> _receiveChannel;
    private readonly CancellationTokenSource _cts;
    private NamedPipeServerStream? _hostServer;
    private NamedPipeClientStream? _slaveClient;
    private HostElectionResult? _role;
    private int _disposed;

    /// <summary>
    /// 构造总线拓扑传输。
    /// </summary>
    /// <param name="pipeName">管道名称（默认 jcc-bus）</param>
    /// <param name="election">主机选举服务</param>
    /// <param name="logger">日志记录器</param>
    public BusTransport(
        string pipeName = "jcc-bus",
        HostElectionService? election = null,
        ILogger? logger = null)
    {
        _pipeName = pipeName;
        _election = election ?? new HostElectionService(pipeName, logger);
        _logger = logger;
        _clientConnections = new ConcurrentDictionary<string, BusClientConnection>();
        _receiveChannel = Channel.CreateUnbounded<TransportFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
    }

    /// <inheritdoc/>
    public TransportTopology Kind => TransportTopology.Bus;

    /// <inheritdoc/>
    public ProcessRole Role => _role?.Role ?? ProcessRole.Slave;

    /// <inheritdoc/>
    public string ProcessId => Environment.ProcessId.ToString();

    /// <inheritdoc/>
    public string HostProcessId => _role?.HostProcessId ?? ProcessId;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && _role is not null;

    /// <summary>获取底层选举服务 — 外部可订阅选举变更。</summary>
    public HostElectionService Election => _election;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _role = await _election.ElectAsync(ct).ConfigureAwait(false);

        if (_role.Role == ProcessRole.Host)
        {
            await StartHostAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await StartSlaveAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(string targetProcessId, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_role is null) throw new InvalidOperationException("Transport not started");

        var envelope = BinaryProtocol.Encode(MessageType.Data, ProcessId, targetProcessId, data);

        if (_role.Role == ProcessRole.Host)
        {
            if (targetProcessId == ProcessId)
            {
                _receiveChannel.Writer.TryWrite(new TransportFrame(ProcessId, data));
                return;
            }

            foreach (var kvp in _clientConnections)
            {
                if (kvp.Key != targetProcessId) continue;
                await kvp.Value.WriteAsync(envelope, ct).ConfigureAwait(false);
                break;
            }
        }
        else
        {
            if (_slaveClient is { IsConnected: true } client)
            {
                await client.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_role is null) throw new InvalidOperationException("Transport not started");

        var envelope = BinaryProtocol.Encode(MessageType.Broadcast, ProcessId, null, data);

        if (_role.Role == ProcessRole.Host)
        {
            foreach (var kvp in _clientConnections)
            {
                await kvp.Value.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
        else
        {
            if (_slaveClient is { IsConnected: true } client)
            {
                await client.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TransportFrame> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnectedProcesses() => _clientConnections.Keys.ToArray();

    private async Task StartHostAsync(CancellationToken ct)
    {
        _hostServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _logger?.LogInformation("BusTransport: HOST started on pipe {Pipe} (pid={Pid})", _pipeName, ProcessId);
        _ = Task.Run(() => AcceptConnectionsLoopAsync(ct), ct);
    }

    private async Task StartSlaveAsync(CancellationToken ct)
    {
        _slaveClient = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await _slaveClient.ConnectAsync(ct).ConfigureAwait(false);

        var hostLine = await BinaryProtocol.ReadLineRawAsync(_slaveClient, ct).ConfigureAwait(false);
        if (hostLine is not null && hostLine.StartsWith("HOST:", StringComparison.Ordinal))
        {
            _logger?.LogDebug("BusTransport: received host handshake {Line}", hostLine);
        }

        var handshake = BinaryProtocol.Encode(
            MessageType.Control,
            ProcessId,
            _role!.HostProcessId,
            Encoding.UTF8.GetBytes($"BUS_SLAVE:{ProcessId}"));
        await _slaveClient.WriteAsync(handshake, ct).ConfigureAwait(false);

        _logger?.LogInformation("BusTransport: SLAVE connected to host {Host} (pid={Pid})",
            _role.HostProcessId, ProcessId);

        _ = Task.Run(() => SlaveReceiveLoopAsync(ct), ct);
    }

    private async Task AcceptConnectionsLoopAsync(CancellationToken ct)
    {
        while (!_cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleBusConnectionAsync(server, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "BusTransport: accept connection error");
            }
        }
    }

    private async Task HandleBusConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        string? slavePid = null;
        try
        {
            await BinaryProtocol.WriteLineRawAsync(server, $"HOST:{ProcessId}", ct).ConfigureAwait(false);

            var firstMsg = await BinaryProtocol.ReadAsync(server, ct).ConfigureAwait(false);
            if (firstMsg.Type == MessageType.Control)
            {
                var text = Encoding.UTF8.GetString(firstMsg.Payload.Span);
                if (text.StartsWith("BUS_SLAVE:", StringComparison.Ordinal))
                {
                    slavePid = text["BUS_SLAVE:".Length..].Trim();
                }
            }

            if (slavePid is null)
            {
                _logger?.LogWarning("BusTransport: connection without BUS_SLAVE handshake, closing");
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }

            var ack = BinaryProtocol.Encode(
                MessageType.Control,
                ProcessId,
                slavePid,
                Encoding.UTF8.GetBytes($"BUS_HOST:{ProcessId}"));
            await server.WriteAsync(ack, ct).ConfigureAwait(false);

            var conn = new BusClientConnection(slavePid, server, _logger);
            _clientConnections[slavePid] = conn;
            _logger?.LogDebug("BusTransport: slave {Slave} joined bus", slavePid);

            await foreach (var msg in conn.ReadMessagesAsync(ct).ConfigureAwait(false))
            {
                if (msg.Type is MessageType.Data or MessageType.Broadcast)
                {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));

                    foreach (var kvp in _clientConnections)
                    {
                        if (kvp.Key == msg.SourcePid) continue;
                        var forwarded = BinaryProtocol.Encode(msg.Type, msg.SourcePid, msg.TargetPid, msg.Payload);
                        await kvp.Value.WriteAsync(forwarded, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BusTransport: connection error for slave {Slave}", slavePid);
        }
        finally
        {
            if (slavePid is not null)
            {
                _clientConnections.TryRemove(slavePid, out _);
            }
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SlaveReceiveLoopAsync(CancellationToken ct)
    {
        if (_slaveClient is null) return;
        try
        {
            await foreach (var msg in BinaryProtocol.ReadStreamAsync(_slaveClient, ct).ConfigureAwait(false))
            {
                if (msg.Type is MessageType.Data or MessageType.Broadcast)
                {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BusTransport: slave receive loop error");
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(BusTransport));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _receiveChannel.Writer.TryComplete();

        foreach (var conn in _clientConnections.Values)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }
        _clientConnections.Clear();

        if (_hostServer is not null) await _hostServer.DisposeAsync().ConfigureAwait(false);
        if (_slaveClient is not null) await _slaveClient.DisposeAsync().ConfigureAwait(false);
        await _election.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>总线客户端连接 — 封装主机侧单个从机连接的读写。</summary>
internal sealed class BusClientConnection : IAsyncDisposable
{
    private readonly NamedPipeServerStream _stream;
    private readonly ILogger? _logger;
    private readonly AsyncLock _writeLock;
    private int _disposed;

    public string ProcessId { get; }

    public BusClientConnection(string processId, NamedPipeServerStream stream, ILogger? logger)
    {
        ProcessId = processId;
        _stream = stream;
        _logger = logger;
        _writeLock = new AsyncLock();
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        using var guard = (await _writeLock.TryLockAsync(ct).ConfigureAwait(false))
            ?? throw new TimeoutException("BusClientConnection: write lock timeout");
        try
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "BusClientConnection: write failed for {Pid}", ProcessId);
        }
    }

    public async IAsyncEnumerable<DecodedMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var msg in BinaryProtocol.ReadStreamAsync(_stream, ct).ConfigureAwait(false))
        {
            yield return msg;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
