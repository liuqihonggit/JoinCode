namespace Core.Utils;

/// <summary>
/// 有名管道传输 — 星型拓扑实现，主机中心转发。
/// <para>主机模式：创建 <see cref="NamedPipeServerStream"/> 监听，接受从机连接，维护连接表，按目标 PID 转发消息。</para>
/// <para>从机模式：连接主机 <see cref="NamedPipeClientStream"/>，通过主机转发消息到其他从机。</para>
/// <para>消息协议：二进制长度前缀 — [1字节类型][4字节源PID长度][源PID][4字节目标PID长度][目标PID][4字节payload长度][payload]。</para>
/// <para>AOT 兼容：纯二进制协议，无反射/JSON 序列化。</para>
/// <para>平台：Windows 用 NamedPipe，Linux 用 Unix domain socket（.NET 统一 API）。</para>
/// </summary>
public sealed class NamedPipeTransport : ITransportTopology {
    private readonly string _pipeName;
    private readonly HostElectionService _election;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, PipeConnection> _connections;
    private readonly Channel<TransportFrame> _receiveChannel;
    private readonly CancellationTokenSource _cts;
    private readonly string _processId;
    private NamedPipeClientStream? _slaveClient;
    private HostElectionResult? _role;
    private Task? _acceptTask;
    private Task? _slaveReceiveTask;
    private int _disposed;

    /// <summary>
    /// 构造有名管道传输。
    /// </summary>
    /// <param name="pipeName">管道名称（默认 jcc-mailbox-host）</param>
    /// <param name="election">主机选举服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="processId">进程标识（默认 Environment.ProcessId，测试可注入）</param>
    public NamedPipeTransport(
        string pipeName = "jcc-mailbox-host",
        HostElectionService? election = null,
        ILogger? logger = null,
        string? processId = null) {
        _pipeName = pipeName;
        _processId = processId ?? Environment.ProcessId.ToString();
        _election = election ?? new HostElectionService(pipeName, logger, processId: _processId);
        _logger = logger;
        _connections = new ConcurrentDictionary<string, PipeConnection>();
        _receiveChannel = Channel.CreateUnbounded<TransportFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
    }

    /// <inheritdoc/>
    public TransportTopology Kind => TransportTopology.Star;

    /// <inheritdoc/>
    public ProcessRole Role => _role?.Role ?? ProcessRole.Slave;

    /// <inheritdoc/>
    public string ProcessId => _processId;

    /// <inheritdoc/>
    public string HostProcessId => _role?.HostProcessId ?? ProcessId;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _disposed) == 0 && _role is not null;

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

        var envelope = BinaryProtocol.Encode(
            MessageType.Data,
            ProcessId,
            targetProcessId,
            data);

        if (_role.Role == ProcessRole.Host) {
            if (_connections.TryGetValue(targetProcessId, out var conn)) {
                await conn.WriteAsync(envelope, ct).ConfigureAwait(false);
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

        var envelope = BinaryProtocol.Encode(
            MessageType.Broadcast,
            ProcessId,
            null,
            data);

        if (_role.Role == ProcessRole.Host) {
            foreach (var conn in _connections.Values) {
                await conn.WriteAsync(envelope, ct).ConfigureAwait(false);
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
    public IReadOnlyCollection<string> GetConnectedProcesses() => _connections.Keys.ToArray();

    /// <summary>获取底层选举服务 — 外部可订阅选举变更。</summary>
    public HostElectionService Election => _election;

    private async Task StartHostAsync(CancellationToken ct) {
        _logger?.LogInformation("NamedPipeTransport: HOST started on pipe {Pipe} (pid={Pid})", _pipeName, ProcessId);

        _acceptTask = Task.Run(() => PipeAcceptLoop.RunAsync(_pipeName, HandleHostConnectionAsync, _cts.Token));
    }

    private async Task StartSlaveAsync(CancellationToken ct) {
        _slaveClient = NamedPipeFactory.CreateClient(_pipeName);

        await _slaveClient.ConnectAsync(ct).ConfigureAwait(false);

        var hostLine = await BinaryProtocol.ReadLineRawAsync(_slaveClient, ct).ConfigureAwait(false);
        if (hostLine is not null && hostLine.StartsWith("HOST:", StringComparison.Ordinal)) {
            var detectedHostPid = hostLine["HOST:".Length..].Trim();
            _logger?.LogDebug("NamedPipeTransport: received host handshake {Line}", hostLine);
        }

        var handshake = BinaryProtocol.Encode(
            MessageType.Control,
            ProcessId,
            _role!.HostProcessId,
            Encoding.UTF8.GetBytes($"SLAVE:{ProcessId}"));
        await _slaveClient.WriteAsync(handshake, ct).ConfigureAwait(false);

        _logger?.LogInformation("NamedPipeTransport: SLAVE connected to host {Host} (pid={Pid})",
            _role.HostProcessId, ProcessId);

        _slaveReceiveTask = Task.Run(() => SlaveReceiveLoopAsync(_cts.Token));
    }

    private async Task HandleHostConnectionAsync(NamedPipeServerStream server, CancellationToken ct) {
        string? slavePid = null;
        try {
            await BinaryProtocol.WriteLineRawAsync(server, $"HOST:{ProcessId}", ct).ConfigureAwait(false);

            var firstMsg = await BinaryProtocol.ReadAsync(server, ct).ConfigureAwait(false);
            TransportDiagnostics.Log("NP", () => $"host recv handshake: type={firstMsg.Type}, payload={Encoding.UTF8.GetString(firstMsg.Payload.Span)}");
            if (firstMsg.Type == MessageType.Control) {
                var text = Encoding.UTF8.GetString(firstMsg.Payload.Span);
                if (text.StartsWith("SLAVE:", StringComparison.Ordinal)) {
                    slavePid = text["SLAVE:".Length..].Trim();
                }
            }

            if (slavePid is null) {
                _logger?.LogWarning("NamedPipeTransport: connection without SLAVE handshake, closing");
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }

            var handshake = BinaryProtocol.Encode(
                MessageType.Control,
                ProcessId,
                slavePid,
                Encoding.UTF8.GetBytes($"HOST:{ProcessId}"));
            await server.WriteAsync(handshake, ct).ConfigureAwait(false);
            TransportDiagnostics.Log("NP", () => $"host sent ACK to slave {slavePid}");

            var conn = new PipeConnection(slavePid, server, _logger);
            _connections[slavePid] = conn;
            _logger?.LogDebug("NamedPipeTransport: slave {Slave} connected", slavePid);

            await foreach (var msg in conn.ReadMessagesAsync(ct).ConfigureAwait(false)) {
                TransportDiagnostics.Log("NP", () => $"host recv msg: type={msg.Type}, source={msg.SourcePid}, target={msg.TargetPid}, len={msg.Payload.Length}");
                if (msg.Type == MessageType.Data && msg.TargetPid is not null) {
                    if (_connections.TryGetValue(msg.TargetPid, out var targetConn)) {
                        var forwarded = BinaryProtocol.Encode(msg.Type, msg.SourcePid, msg.TargetPid, msg.Payload);
                        await targetConn.WriteAsync(forwarded, ct).ConfigureAwait(false);
                    }
                } else if (msg.Type == MessageType.Broadcast) {
                    foreach (var kvp in _connections) {
                        if (kvp.Key != msg.SourcePid) {
                            var forwarded = BinaryProtocol.Encode(MessageType.Broadcast, msg.SourcePid, null, msg.Payload);
                            await kvp.Value.WriteAsync(forwarded, ct).ConfigureAwait(false);
                        }
                    }
                } else if (msg.Type == MessageType.Data) {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));
                }
            }
        } catch (OperationCanceledException) { TransportDiagnostics.Log("NP", () => $"HandleHostConnection cancelled, slave={slavePid}"); } catch (Exception ex) {
            TransportDiagnostics.Log("NP", () => $"HandleHostConnection error: {ex.GetType().Name}: {ex.Message}, slave={slavePid}");
            _logger?.LogError(ex, "NamedPipeTransport: host connection error for slave {Slave}", slavePid);
        } finally {
            if (slavePid is not null) {
                _connections.TryRemove(slavePid, out _);
            }
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SlaveReceiveLoopAsync(CancellationToken ct) {
        if (_slaveClient is null) return;
        try {
            await foreach (var msg in BinaryProtocol.ReadStreamAsync(_slaveClient, ct).ConfigureAwait(false)) {
                TransportDiagnostics.Log("NP", () => $"slave recv msg: type={msg.Type}, source={msg.SourcePid}, len={msg.Payload.Length}");
                if (msg.Type is MessageType.Data or MessageType.Broadcast) {
                    _receiveChannel.Writer.TryWrite(new TransportFrame(msg.SourcePid, msg.Payload));
                } else if (msg.Type == MessageType.Snapshot) {
                    var json = Encoding.UTF8.GetString(msg.Payload.Span);
                    _logger?.LogDebug("NamedPipeTransport: received snapshot ({Len} bytes)", msg.Payload.Length);
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "NamedPipeTransport: slave receive loop error");
        }
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(NamedPipeTransport));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _receiveChannel.Writer.TryComplete();

        var conns = _connections.Values.ToArray();
        _connections.Clear();
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

/// <summary>消息类型 — 二进制协议的消息分类。</summary>
internal enum MessageType : byte {
    /// <summary>控制消息（握手/心跳）。</summary>
    Control = 0,
    /// <summary>定向数据消息。</summary>
    Data = 1,
    /// <summary>广播数据消息。</summary>
    Broadcast = 2,
    /// <summary>上下文快照（故障转移）。</summary>
    Snapshot = 3,
}

/// <summary>解码后的消息信封。</summary>
internal sealed record DecodedMessage(
    MessageType Type,
    string SourcePid,
    string? TargetPid,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// 二进制长度前缀协议 — AOT 兼容，无 JSON 序列化。
/// <para>格式：[1字节类型][4字节源PID长度][源PID][4字节目标PID长度][目标PID][4字节payload长度][payload]</para>
/// </summary>
internal static class BinaryProtocol {
    private const int MaxMessageSize = 16 * 1024 * 1024;

    public static ReadOnlyMemory<byte> Encode(
        MessageType type,
        string sourcePid,
        string? targetPid,
        ReadOnlyMemory<byte> payload) {
        var sourceBytes = Encoding.UTF8.GetBytes(sourcePid);
        var targetBytes = targetPid is not null ? Encoding.UTF8.GetBytes(targetPid) : Array.Empty<byte>();

        var totalSize = 1 + 4 + sourceBytes.Length + 4 + targetBytes.Length + 4 + payload.Length;
        var buffer = new byte[totalSize];
        var offset = 0;

        buffer[offset++] = (byte)type;
        WriteInt32BigEndian(buffer, ref offset, sourceBytes.Length);
        Buffer.BlockCopy(sourceBytes, 0, buffer, offset, sourceBytes.Length);
        offset += sourceBytes.Length;
        WriteInt32BigEndian(buffer, ref offset, targetBytes.Length);
        Buffer.BlockCopy(targetBytes, 0, buffer, offset, targetBytes.Length);
        offset += targetBytes.Length;
        WriteInt32BigEndian(buffer, ref offset, payload.Length);
        payload.Span.TryCopyTo(buffer.AsSpan(offset));
        offset += payload.Length;

        return buffer;
    }

    public static async ValueTask<DecodedMessage> ReadAsync(Stream stream, CancellationToken ct) {
        var typeByte = await ReadByteAsync(stream, ct).ConfigureAwait(false);
        var type = (MessageType)typeByte;

        var sourceLen = await ReadInt32BigEndianAsync(stream, ct).ConfigureAwait(false);
        if (sourceLen is < 0 or > 1024) throw new InvalidDataException($"Invalid source PID length: {sourceLen}");
        var sourceBytes = new byte[sourceLen];
        await ReadExactAsync(stream, sourceBytes, ct).ConfigureAwait(false);
        var sourcePid = Encoding.UTF8.GetString(sourceBytes);

        var targetLen = await ReadInt32BigEndianAsync(stream, ct).ConfigureAwait(false);
        if (targetLen is < 0 or > 1024) throw new InvalidDataException($"Invalid target PID length: {targetLen}");
        string? targetPid = null;
        if (targetLen > 0) {
            var targetBytes = new byte[targetLen];
            await ReadExactAsync(stream, targetBytes, ct).ConfigureAwait(false);
            targetPid = Encoding.UTF8.GetString(targetBytes);
        }

        var payloadLen = await ReadInt32BigEndianAsync(stream, ct).ConfigureAwait(false);
        if (payloadLen is < 0 or > MaxMessageSize) throw new InvalidDataException($"Invalid payload length: {payloadLen}");
        var payload = new byte[payloadLen];
        if (payloadLen > 0) {
            await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);
        }

        return new DecodedMessage(type, sourcePid, targetPid, payload);
    }

    public static async IAsyncEnumerable<DecodedMessage> ReadStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            DecodedMessage msg;
            try {
                msg = await ReadAsync(stream, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { yield break; } catch (EndOfStreamException) { yield break; }
            yield return msg;
        }
    }

    private static void WriteInt32BigEndian(byte[] buffer, ref int offset, int value) {
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }

    private static async ValueTask<byte> ReadByteAsync(Stream stream, CancellationToken ct) {
        var buf = new byte[1];
        await ReadExactAsync(stream, buf, ct).ConfigureAwait(false);
        return buf[0];
    }

    private static async ValueTask<int> ReadInt32BigEndianAsync(Stream stream, CancellationToken ct) {
        var buf = new byte[4];
        await ReadExactAsync(stream, buf, ct).ConfigureAwait(false);
        return (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct) {
        var offset = 0;
        while (offset < buffer.Length) {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    /// <summary>
    /// 裸读取一行文本（直到 '\n'）— 不用 StreamReader 避免缓冲污染后续二进制读取。
    /// <para>用于主机握手 "HOST:{pid}\n"，兼容 <see cref="HostElectionService"/> 探测协议。</para>
    /// </summary>
    public static async ValueTask<string?> ReadLineRawAsync(Stream stream, CancellationToken ct) {
        var sb = new StringBuilder(64);
        var buf = new byte[1];
        while (true) {
            var read = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
            if (read == 0) return sb.Length > 0 ? sb.ToString() : null;
            if (buf[0] == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)buf[0]);
        }
    }

    /// <summary>
    /// 写入一行文本（以 '\n' 结尾）— 用于主机握手 "HOST:{pid}\n"。
    /// </summary>
    public static async ValueTask WriteLineRawAsync(Stream stream, string line, CancellationToken ct) {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}

/// <summary>管道连接 — 封装单个从机连接的读写，用 Channel 串行化写入（无锁 Actor 模型）。</summary>
internal sealed class PipeConnection : IAsyncDisposable {
    private readonly NamedPipeServerStream _stream;
    private readonly ILogger? _logger;
    private readonly Channel<ReadOnlyMemory<byte>> _writeQueue;
    private readonly Task _writeLoop;
    private int _disposed;

    public string ProcessId { get; }

    public PipeConnection(string processId, NamedPipeServerStream stream, ILogger? logger) {
        ProcessId = processId;
        _stream = stream;
        _logger = logger;
        _writeQueue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        _writeLoop = Task.Run(WriteLoopAsync);
    }

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
                    _logger?.LogWarning(ex, "PipeConnection: write failed for {Pid}", ProcessId);
                }
            }
        } catch (OperationCanceledException) { }
    }

    public async IAsyncEnumerable<DecodedMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var msg in BinaryProtocol.ReadStreamAsync(_stream, ct).ConfigureAwait(false)) {
            yield return msg;
        }
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _writeQueue.Writer.TryComplete();
        await _writeLoop.ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}