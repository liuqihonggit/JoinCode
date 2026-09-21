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
public sealed class BusTransport : ITransportTopology {
    private readonly string _pipeName;
    private readonly HostElectionService _election;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, BusClientConnection> _clientConnections;
    private readonly Channel<TransportFrame> _receiveChannel;
    private readonly CancellationTokenSource _cts;
    private readonly string _processId;
    private NamedPipeClientStream? _slaveClient;
    private HostElectionResult? _role;
    private Task? _acceptTask;
    private Task? _slaveReceiveTask;
    private int _disposed;

    /// <summary>
    /// 构造总线拓扑传输。
    /// </summary>
    /// <param name="pipeName">管道名称（默认 jcc-bus）</param>
    /// <param name="election">主机选举服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="processId">进程标识（默认 Environment.ProcessId，测试可注入）</param>
    public BusTransport(
        string pipeName = "jcc-bus",
        HostElectionService? election = null,
        ILogger? logger = null,
        string? processId = null) {
        _pipeName = pipeName;
        _processId = processId ?? Environment.ProcessId.ToString();
        _election = election ?? new HostElectionService(pipeName, logger, processId: _processId);
        _logger = logger;
        _clientConnections = new ConcurrentDictionary<string, BusClientConnection>();
        _receiveChannel = Channel.CreateUnbounded<TransportFrame>(new UnboundedChannelOptions {
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
    public string ProcessId => _processId;

    /// <inheritdoc/>
    public string HostProcessId => _role?.HostProcessId ?? ProcessId;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && _role is not null;

    /// <summary>获取底层选举服务 — 外部可订阅选举变更。</summary>
    public HostElectionService Election => _election;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default) {
        ThrowIfDisposed();
        _role = await _election.ElectAsync(ct).ConfigureAwait(false);

        if (_role.Role == ProcessRole.Host) {
            await StartHostAsync(ct).ConfigureAwait(false);
        } else {
            await StartSlaveAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(string targetProcessId, ReadOnlyMemory<byte> data, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (_role is null) throw new InvalidOperationException("Transport not started");

        var envelope = BinaryProtocol.Encode(MessageType.Data, ProcessId, targetProcessId, data);

        if (_role.Role == ProcessRole.Host) {
            if (targetProcessId == ProcessId) {
                _receiveChannel.Writer.TryWrite(new TransportFrame(ProcessId, data));
                return;
            }

            foreach (var kvp in _clientConnections) {
                if (kvp.Key != targetProcessId) continue;
                await kvp.Value.WriteAsync(envelope, ct).ConfigureAwait(false);
                break;
            }
        } else {
            if (_slaveClient is { IsConnected: true } client) {
                await client.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (_role is null) throw new InvalidOperationException("Transport not started");

        var envelope = BinaryProtocol.Encode(MessageType.Broadcast, ProcessId, null, data);

        if (_role.Role == ProcessRole.Host) {
            foreach (var kvp in _clientConnections) {
                await kvp.Value.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        } else {
            if (_slaveClient is { IsConnected: true } client) {
                await client.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TransportFrame> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnectedProcesses() => _clientConnections.Keys.ToArray();

    private async Task StartHostAsync(CancellationToken ct) {
        _logger?.LogInformation("BusTransport: HOST started on pipe {Pipe} (pid={Pid})", _pipeName, ProcessId);
        _acceptTask = Task.Run(() => PipeAcceptLoop.RunAsync(_pipeName, HandleBusConnectionAsync, _cts.Token));
    }

    private async Task StartSlaveAsync(CancellationToken ct) {
        _slaveClient = NamedPipeFactory.CreateClient(_pipeName);

        await _slaveClient.ConnectAsync(ct).ConfigureAwait(false);

        var hostLine = await BinaryProtocol.ReadLineRawAsync(_slaveClient, ct).ConfigureAwait(false);
        if (hostLine is not null && hostLine.StartsWith("HOST:", StringComparison.Ordinal)) {
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

        _slaveReceiveTask = Task.Run(() => SlaveReceiveLoopAsync(_cts.Token));
    }

    private async Task HandleBusConnectionAsync(NamedPipeServerStream server, CancellationToken ct) {
        string? slavePid = null;
        try {
            await BinaryProtocol.WriteLineRawAsync(server, $"HOST:{ProcessId}", ct).ConfigureAwait(false);

            var firstMsg = await BinaryProtocol.ReadAsync(server, ct).ConfigureAwait(false);
            TransportDiagnostics.Log("BUS", () => $"host recv handshake: type={firstMsg.Type}, payload={Encoding.UTF8.GetString(firstMsg.Payload.Span)}");
            if (firstMsg.Type == MessageType.Control) {
                var text = Encoding.UTF8.GetString(firstMsg.Payload.Span);
                if (text.StartsWith("BUS_SLAVE:", StringComparison.Ordinal)) {
                    slavePid = text["BUS_SLAVE:".Length..].Trim();
                }
            }

            if (slavePid is null) {
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
            TransportDiagnostics.Log("BUS", () => $"host sent ACK to slave {slavePid}");

            var conn = new BusClientConnection(slavePid, server, _logger);
            _clientConnections[slavePid] = conn;
            _logger?.LogDebug("BusTransport: slave {Slave} joined bus", slavePid);

            await foreach (var msg in conn.ReadMessagesAsync(ct).ConfigureAwait(false)) {
                TransportDiagnostics.Log("BUS", () => $"host recv msg: type={msg.Type}, source={msg.SourcePid}, len={msg.Payload.Length}");
                if (msg.Type is MessageType.Data or MessageType.Broadcast) {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));

                    foreach (var kvp in _clientConnections) {
                        if (kvp.Key == msg.SourcePid) continue;
                        var forwarded = BinaryProtocol.Encode(msg.Type, msg.SourcePid, msg.TargetPid, msg.Payload);
                        await kvp.Value.WriteAsync(forwarded, ct).ConfigureAwait(false);
                    }
                }
            }
        } catch (OperationCanceledException) { TransportDiagnostics.Log("BUS", () => $"HandleBusConnection cancelled, slave={slavePid}"); } catch (Exception ex) {
            TransportDiagnostics.Log("BUS", () => $"HandleBusConnection error: {ex.GetType().Name}: {ex.Message}, slave={slavePid}");
            _logger?.LogError(ex, "BusTransport: connection error for slave {Slave}", slavePid);
        } finally {
            if (slavePid is not null) {
                _clientConnections.TryRemove(slavePid, out _);
            }
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SlaveReceiveLoopAsync(CancellationToken ct) {
        if (_slaveClient is null) return;
        try {
            await foreach (var msg in BinaryProtocol.ReadStreamAsync(_slaveClient, ct).ConfigureAwait(false)) {
                TransportDiagnostics.Log("BUS", () => $"slave recv msg: type={msg.Type}, source={msg.SourcePid}, len={msg.Payload.Length}");
                if (msg.Type is MessageType.Data or MessageType.Broadcast) {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "BusTransport: slave receive loop error");
        }
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(BusTransport));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _receiveChannel.Writer.TryComplete();

        var conns = _clientConnections.Values.ToArray();
        _clientConnections.Clear();
        var slaveClient = _slaveClient;

        if (_acceptTask is not null) await _acceptTask.ConfigureAwait(false);
        if (_slaveReceiveTask is not null) await _slaveReceiveTask.ConfigureAwait(false);
        _slaveClient = null;
        _acceptTask = null;
        _slaveReceiveTask = null;
        Cleanup(conns, slaveClient, _election, _cts);
    }

    private static void Cleanup(IAsyncDisposable[] conns, NamedPipeClientStream? slaveClient, HostElectionService election, CancellationTokenSource cts) {
        foreach (var conn in conns) conn.DisposeAsync().AsTask().Wait();
        slaveClient?.DisposeAsync().AsTask().Wait();
        election.DisposeAsync().AsTask().Wait();
        cts.Dispose();
    }
}

/// <summary>总线客户端连接 — 封装主机侧单个从机连接的读写，用 Channel 串行化写入（无锁 Actor 模型）。</summary>
internal sealed class BusClientConnection : IAsyncDisposable {
    private readonly NamedPipeServerStream _stream;
    private readonly ILogger? _logger;
    private readonly Channel<ReadOnlyMemory<byte>> _writeQueue;
    private readonly Task _writeLoop;
    private int _disposed;

    /// <summary>获取从机进程标识。</summary>
    public string ProcessId { get; }

    /// <summary>
    /// 构造总线客户端连接。
    /// </summary>
    /// <param name="processId">从机进程标识</param>
    /// <param name="stream">命名管道服务端流</param>
    /// <param name="logger">日志记录器(可选)</param>
    public BusClientConnection(string processId, NamedPipeServerStream stream, ILogger? logger) {
        ProcessId = processId;
        _stream = stream;
        _logger = logger;
        _writeQueue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>异步写入数据 — 投递到内部写入通道,由后台写循环串行化发出。</summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) {
        if (Volatile.Read(ref _disposed) != 0) return ValueTask.CompletedTask;
        return _writeQueue.Writer.WriteAsync(data, ct);
    }

    private async Task WriteLoopAsync() {
        try {
            await foreach (var data in _writeQueue.Reader.ReadAllAsync().ConfigureAwait(false)) {
                if (Volatile.Read(ref _disposed) != 0) return;
                try {
                    await _stream.WriteAsync(data).ConfigureAwait(false);
                    await _stream.FlushAsync().ConfigureAwait(false);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    _logger?.LogWarning(ex, "BusClientConnection: write failed for {Pid}", ProcessId);
                }
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>异步读取消息 — 从管道流解码二进制协议消息并逐条返回。</summary>
    public async IAsyncEnumerable<DecodedMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var msg in BinaryProtocol.ReadStreamAsync(_stream, ct).ConfigureAwait(false)) {
            yield return msg;
        }
    }

    /// <summary>异步释放资源。</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _writeQueue.Writer.TryComplete();
        await _writeLoop.ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}