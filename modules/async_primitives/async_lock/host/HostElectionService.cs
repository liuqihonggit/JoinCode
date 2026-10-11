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

/// <summary>主机选举命令标记接口 — Actor 串行化决策。</summary>
public interface IHostElectionCommand;

/// <summary>选举命令 — 探测结果 + TCS 回复，Actor 消费者独占决策。</summary>
internal sealed record ElectCmd(TaskCompletionSource<HostElectionResult> Tcs, string? ExistingHostPid) : IHostElectionCommand;

/// <summary>更新快照命令 — 从机收到主机同步的上下文快照。</summary>
internal sealed record UpdateSnapshotCmd(HostContextSnapshot Snapshot) : IHostElectionCommand;

/// <summary>故障转移命令 — 心跳超时后从机选举新主机，回投给 Actor 串行化更新。</summary>
internal sealed record FailoverCmd(HostElectionResult NewRole) : IHostElectionCommand;

/// <summary>
/// 主机发现与选举服务 — 基于 NamedPipe 探测 + 进程句柄比较实现主从选举。
/// <para>选举协议：</para>
/// <para>1. 启动时探测有名管道（固定管道名 pipeName）是否存在。</para>
/// <para>2. 探测失败 → 注册自己为主机（创建 NamedPipeServerStream）。</para>
/// <para>3. 探测成功 → 连接主机，注册为从机。</para>
/// <para>4. 多主机冲突 → 比较进程句柄（<see cref="Environment.ProcessId"/>），句柄小者保留为主机，大者降级为从机。</para>
/// <para>5. 主机掉线 → 从机检测心跳超时，句柄最小的从机根据上下文快照替代为新主机。</para>
/// <para>线程安全：选举决策通过 ActorBase 串行化（单消费者独占），无需锁。心跳循环检测结果通过 Tell 回投 Actor。</para>
/// </summary>
public sealed class HostElectionService : ActorBase<IHostElectionCommand, Unit> {
    private readonly string _pipeName;
    private readonly ILogger? _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly string _processId;
    private readonly Channel<HostElectionResult> _electionChannel = Channel.CreateBounded<HostElectionResult>(new BoundedChannelOptions(16) {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _heartbeatCts = new();
    private Task? _heartbeatTask;
    private HostElectionResult? _currentRole;
    private HostContextSnapshot? _lastSnapshot;
    private Func<HostContextSnapshot?, CancellationToken, ValueTask<HostElectionResult>>? _onHostDown;
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
        string? processId = null)
        : base(logger: logger) {
        _pipeName = pipeName;
        _logger = logger;
        _processId = processId ?? Environment.ProcessId.ToString();
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(3);
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>当前进程标识 — 使用 PID 或注入的测试值。</summary>
    public string ProcessId => _processId;

    /// <summary>当前角色（null 表示尚未选举）。Actor 内部写，外部 Volatile.Read 安全。</summary>
    public HostElectionResult? CurrentRole => Volatile.Read(ref _currentRole);

    /// <summary>最后一次收到的主机上下文快照（故障转移用）。Actor 内部写，外部 Volatile.Read 安全。</summary>
    public HostContextSnapshot? LastSnapshot => Volatile.Read(ref _lastSnapshot);

    /// <summary>选举结果流 — 角色变更时产出（如从机转主机）。</summary>
    public IAsyncEnumerable<HostElectionResult> ElectionChangesAsync(CancellationToken ct = default)
        => _electionChannel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// 执行主机选举 — 探测有名管道，决定当前进程角色。
    /// <para>探测并行（不串行化），决策串行（Actor 消费者独占，无锁）。</para>
    /// <para>调用方：<see cref="ITransportTopology.StartAsync"/> 启动前调用。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>选举结果</returns>
    public async ValueTask<HostElectionResult> ElectAsync(CancellationToken ct = default) {
        ThrowIfDisposed();

        var existingHostPid = await TryDetectHostAsync(ct).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<HostElectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Tell(new ElectCmd(tcs, existingHostPid));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 启动心跳监控 — 从机定期检测主机心跳，超时触发故障转移。
    /// <para>仅从机角色调用；主机角色无需心跳。</para>
    /// <para>心跳循环检测结果通过 Tell(FailoverCmd) 回投 Actor，保证状态修改串行化。</para>
    /// </summary>
    /// <param name="onHostDown">主机掉线回调（从机选举新主机）</param>
    /// <param name="ct">取消令牌</param>
    public void StartHeartbeat(Func<HostContextSnapshot?, CancellationToken, ValueTask<HostElectionResult>> onHostDown, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (_heartbeatTask is not null) return;
        _onHostDown = onHostDown;
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(ct), ct);
    }

    /// <summary>
    /// 更新主机上下文快照 — 从机收到主机同步的上下文时调用。
    /// <para>通过 Tell 投递给 Actor 串行化更新，无需 Volatile.Write。</para>
    /// </summary>
    /// <param name="snapshot">主机上下文快照</param>
    public void UpdateSnapshot(HostContextSnapshot snapshot) {
        ThrowIfDisposed();
        Tell(new UpdateSnapshotCmd(snapshot));
    }

    /// <summary>
    /// Actor 命令处理 — Consumer 线程独占，无需锁。
    /// <para>ElectCmd: 决策角色 + 写结果流 + 回复 TCS。</para>
    /// <para>UpdateSnapshotCmd: 更新快照。</para>
    /// <para>FailoverCmd: 故障转移 + 写结果流。</para>
    /// </summary>
    protected override void Handle(IHostElectionCommand cmd, CancellationToken ct) {
        switch (cmd) {
            case ElectCmd elect:
                var result = DecideRole(elect.ExistingHostPid);
                _currentRole = result;
                _electionChannel.Writer.TryWrite(result);
                elect.Tcs.SetResult(result);
                break;
            case UpdateSnapshotCmd snapshot:
                _lastSnapshot = snapshot.Snapshot;
                break;
            case FailoverCmd failover:
                _currentRole = failover.NewRole;
                _electionChannel.Writer.TryWrite(failover.NewRole);
                break;
        }
    }

    /// <summary>
    /// 心跳循环 — 从机定期检测主机心跳，超时调 onHostDown 回调，结果通过 Tell 回投 Actor。
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct) {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_heartbeatCts.Token, ct);
        try {
            while (!_heartbeatCts.IsCancellationRequested && !ct.IsCancellationRequested) {
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

                    if (_onHostDown is null) return;
                    var newRole = await _onHostDown(snapshot, ct).ConfigureAwait(false);
                    Tell(new FailoverCmd(newRole));
                    return;
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "HostElection: heartbeat loop error");
        }
    }

    /// <summary>
    /// 根据探测结果决定角色 — 由 Actor Consumer 线程独占调用，无需锁。
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
    /// 释放选举服务 — 取消心跳循环、完成结果流、释放 Actor。
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _heartbeatCts.Cancel();
        if (_heartbeatTask is not null) {
            try {
                await _heartbeatTask.ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "HostElection: heartbeat task exception on dispose");
            }
        }
        _heartbeatCts.Dispose();
        _electionChannel.Writer.TryComplete();

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
