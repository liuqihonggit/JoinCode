namespace Core.Utils;

/// <summary>
/// 主机选举结果 — 探测有名管道后得出的角色决策。
/// </summary>
public sealed record HostElectionResult {
    /// <summary>选举出的角色（主机/从机）。</summary>
    public required ProcessRole Role { get; init; }

    /// <summary>当前进程标识。</summary>
    public required string ProcessId { get; init; }

    /// <summary>主机进程标识（主机模式下为自身，从机模式下为探测到的主机）。</summary>
    public required string HostProcessId { get; init; }

    /// <summary>是否为新当选主机（从机转主机）。</summary>
    public bool IsNewlyElected { get; init; }
}

/// <summary>选举请求 — 探测结果 + 结果回调，通过 Channel 串行化决策（无锁 Actor 模型）。</summary>
internal sealed record ElectionRequest(
    string? ExistingHostPid,
    TaskCompletionSource<HostElectionResult> Tcs);

/// <summary>
/// 主机发现与选举服务 — 基于 NamedPipe 探测 + 进程句柄比较实现主从选举。
/// <para>选举协议：</para>
/// <para>1. 启动时探测有名管道（固定管道名 pipeName）是否存在。</para>
/// <para>2. 探测失败 → 注册自己为主机（创建 NamedPipeServerStream）。</para>
/// <para>3. 探测成功 → 连接主机，注册为从机。</para>
/// <para>4. 多主机冲突 → 比较进程句柄（<see cref="Environment.ProcessId"/>），句柄小者保留为主机，大者降级为从机。</para>
/// <para>5. 主机掉线 → 从机检测心跳超时，句柄最小的从机根据上下文快照替代为新主机。</para>
/// <para>线程安全：所有方法通过 <see cref="AsyncLock"/> 保护，避免并发选举冲突。</para>
/// </summary>
public sealed class HostElectionService : IAsyncDisposable {
    private readonly string _pipeName;
    private readonly ILogger? _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<HostElectionResult> _electionChannel;
    private readonly Channel<ElectionRequest> _electionCmdChannel;
    private Task? _electionConsumerTask;
    private readonly string _processId;
    private HostElectionResult? _currentRole;
    private HostContextSnapshot? _lastSnapshot;
    private Task? _heartbeatTask;
    private int _disposed;

    /// <summary>
    /// 构造主机选举服务。
    /// </summary>
    /// <param name="pipeName">有名管道名称（默认 jcc-mailbox-host）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="heartbeatInterval">心跳间隔（默认 3s）</param>
    /// <param name="heartbeatTimeout">心跳超时（默认 10s，超时判定主机掉线）</param>
    /// <param name="processId">进程标识（默认 Environment.ProcessId，测试可注入模拟不同进程）</param>
    public HostElectionService(
        string pipeName = "jcc-mailbox-host",
        ILogger? logger = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatTimeout = null,
        string? processId = null) {
        _pipeName = pipeName;
        _logger = logger;
        _processId = processId ?? Environment.ProcessId.ToString();
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(3);
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(10);
        _cts = new CancellationTokenSource();
        _electionChannel = Channel.CreateBounded<HostElectionResult>(new BoundedChannelOptions(16) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _electionCmdChannel = Channel.CreateBounded<ElectionRequest>(new BoundedChannelOptions(32) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>当前进程标识 — 使用 PID 或注入的测试值。</summary>
    public string ProcessId => _processId;

    /// <summary>当前角色（null 表示尚未选举）。</summary>
    public HostElectionResult? CurrentRole => Volatile.Read(ref _currentRole);

    /// <summary>最后一次收到的主机上下文快照（故障转移用）。</summary>
    public HostContextSnapshot? LastSnapshot => Volatile.Read(ref _lastSnapshot);

    /// <summary>选举结果流 — 角色变更时产出（如从机转主机）。</summary>
    public IAsyncEnumerable<HostElectionResult> ElectionChangesAsync(CancellationToken ct = default)
        => _electionChannel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// 执行主机选举 — 探测有名管道，决定当前进程角色。
    /// <para>探测并行（不串行化），决策串行（Channel 消费者独占，无锁 Actor 模型）。</para>
    /// <para>调用方：<see cref="ITransportTopology.StartAsync"/> 启动前调用。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>选举结果</returns>
    public async ValueTask<HostElectionResult> ElectAsync(CancellationToken ct = default) {
        ThrowIfDisposed();

        EnsureConsumerStarted();

        var existingHostPid = await TryDetectHostAsync(ct).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<HostElectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _electionCmdChannel.Writer.WriteAsync(new ElectionRequest(existingHostPid, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 确保选举消费者已启动 — 首次调用 ElectAsync 时延迟启动,避免构造即启后台线程。
    /// </summary>
    private void EnsureConsumerStarted() {
        if (_electionConsumerTask is not null) return;
        Interlocked.CompareExchange(ref _electionConsumerTask, Task.Run(ElectionConsumerLoopAsync), null);
    }

    /// <summary>
    /// 选举决策消费者 — Channel 串行化，单线程独占决策，无需锁。
    /// </summary>
    private async Task ElectionConsumerLoopAsync() {
        try {
            await foreach (var req in _electionCmdChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                var result = DecideRole(req.ExistingHostPid);
                Volatile.Write(ref _currentRole, result);
                _electionChannel.Writer.TryWrite(result);
                req.Tcs.SetResult(result);
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 根据探测结果决定角色 — 由消费者线程独占调用，无需锁。
    /// </summary>
    private HostElectionResult DecideRole(string? existingHostPid) {
        var pid = _processId;

        if (existingHostPid is null) {
            _logger?.LogInformation("HostElection: elected as HOST (pid={Pid}, pipe={Pipe})", pid, _pipeName);
            return new HostElectionResult {
                Role = ProcessRole.Host,
                ProcessId = pid,
                HostProcessId = pid,
                IsNewlyElected = true
            };
        }

        if (int.TryParse(existingHostPid, out var hostPid) && int.TryParse(_processId, out var myPid) && hostPid > myPid) {
            _logger?.LogInformation(
                "HostElection: CONFLICT resolved — local pid={Local} < remote pid={Remote}, taking over as HOST",
                _processId, hostPid);
            return new HostElectionResult {
                Role = ProcessRole.Host,
                ProcessId = pid,
                HostProcessId = pid,
                IsNewlyElected = true
            };
        }

        _logger?.LogInformation("HostElection: joined as SLAVE (pid={Pid}, host={Host})", pid, existingHostPid);
        return new HostElectionResult {
            Role = ProcessRole.Slave,
            ProcessId = pid,
            HostProcessId = existingHostPid,
            IsNewlyElected = false
        };
    }

    /// <summary>
    /// 启动心跳监控 — 从机定期检测主机心跳，超时触发故障转移。
    /// <para>仅从机角色调用；主机角色无需心跳。</para>
    /// </summary>
    /// <param name="onHostDown">主机掉线回调（从机选举新主机）</param>
    /// <param name="ct">取消令牌</param>
    public void StartHeartbeat(Func<HostContextSnapshot?, CancellationToken, ValueTask<HostElectionResult>> onHostDown, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (_heartbeatTask is not null) return;
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(onHostDown, ct), ct);
    }

    /// <summary>
    /// 更新主机上下文快照 — 从机收到主机同步的上下文时调用。
    /// </summary>
    /// <param name="snapshot">主机上下文快照</param>
    public void UpdateSnapshot(HostContextSnapshot snapshot) {
        Volatile.Write(ref _lastSnapshot, snapshot);
    }

    private async Task HeartbeatLoopAsync(
        Func<HostContextSnapshot?, CancellationToken, ValueTask<HostElectionResult>> onHostDown,
        CancellationToken ct) {
        var lastHeartbeat = DateTimeOffset.UtcNow;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        try {
            while (!_cts.IsCancellationRequested && !ct.IsCancellationRequested) {
                await Task.Delay(_heartbeatInterval, linkedCts.Token)
                    .ConfigureAwait(false);

                var role = Volatile.Read(ref _currentRole);
                if (role is null || role.Role != ProcessRole.Slave) continue;

                var snapshot = Volatile.Read(ref _lastSnapshot);
                var lastUpdate = snapshot?.Timestamp ?? DateTimeOffset.MinValue;

                if (DateTimeOffset.UtcNow - lastUpdate > _heartbeatTimeout) {
                    _logger?.LogWarning(
                        "HostElection: host {Host} heartbeat timeout (last={LastUpdate:F1}s ago), triggering failover",
                        role.HostProcessId, (DateTimeOffset.UtcNow - lastUpdate).TotalSeconds);

                    var newRole = await onHostDown(snapshot, ct).ConfigureAwait(false);
                    Volatile.Write(ref _currentRole, newRole);
                    _electionChannel.Writer.TryWrite(newRole);
                    return;
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "HostElection: heartbeat loop error");
        }
    }

    /// <summary>
    /// 探测有名管道是否存在且可连接 — 返回主机 PID（null 表示无主机）。
    /// </summary>
    private async ValueTask<string?> TryDetectHostAsync(CancellationToken ct) {
        try {
            await using var client = NamedPipeFactory.CreateClient(_pipeName);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            var hostPid = await ReadHostPidAsync(client, ct).ConfigureAwait(false);
            return hostPid;
        } catch (OperationCanceledException) { } catch (Exception ex) when (ex is IOException or TimeoutException) {
            _logger?.LogDebug("HostElection: no existing host detected on pipe {Pipe}", _pipeName);
        }
        return null;
    }

    /// <summary>
    /// 从管道读取主机 PID — 握手协议：客户端连接后，服务端发送 "HOST:{pid}\n"。
    /// <para>超时保护：2 秒内未读到数据则返回 null，防止协议不匹配时死锁。</para>
    /// </summary>
    private async ValueTask<string?> ReadHostPidAsync(NamedPipeClientStream client, CancellationToken ct) {
        try {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(2));
            var line = await BinaryProtocol.ReadLineRawAsync(client, readCts.Token).ConfigureAwait(false);
            if (line is null || !line.StartsWith("HOST:", StringComparison.Ordinal)) return null;
            return line["HOST:".Length..].Trim();
        } catch (OperationCanceledException) { return null; }
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HostElectionService));
    }

    /// <summary>
    /// 释放选举服务 — 取消心跳循环。
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _electionCmdChannel.Writer.TryComplete();
        _electionChannel.Writer.TryComplete();
        if (_heartbeatTask is not null) await _heartbeatTask.ConfigureAwait(false);
        if (_electionConsumerTask is not null) await _electionConsumerTask.ConfigureAwait(false);
        _heartbeatTask = null;
        _electionConsumerTask = null;
        _cts.Dispose();
    }
}