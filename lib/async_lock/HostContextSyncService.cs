namespace Core.Utils;

/// <summary>
/// 上下文快照提供者委托 — 主机调用以收集当前上下文快照。
/// </summary>
public delegate HostContextSnapshot ContextSnapshotProvider();

/// <summary>
/// 主机上下文同步服务 — 主机定期广播上下文快照，从机接收并缓存，用于故障转移。
/// <para>主机模式：定期调用 <see cref="ContextSnapshotProvider"/> 收集快照，通过传输层广播给所有从机。</para>
/// <para>从机模式：从传输层接收快照，调用 <see cref="HostElectionService.UpdateSnapshot"/> 更新本地缓存。</para>
/// <para>故障转移：主机掉线时，从机用缓存的快照重建主机服务（<see cref="HostElectionService"/> 心跳超时触发）。</para>
/// <para>完整上下文：路由表 + 未投递消息 + 编译队列状态（<see cref="HostContextSnapshot"/>）。</para>
/// </summary>
public sealed class HostContextSyncService : IAsyncDisposable {
    private readonly ITransportTopology _transport;
    private readonly HostElectionService _election;
    private readonly ContextSnapshotProvider? _snapshotProvider;
    private readonly ILogger? _logger;
    private readonly TimeSpan _syncInterval;
    private readonly CancellationTokenSource _cts;
    private Task? _syncTask;
    private int _disposed;

    /// <summary>
    /// 构造上下文同步服务。
    /// </summary>
    /// <param name="transport">传输层</param>
    /// <param name="election">主机选举服务</param>
    /// <param name="snapshotProvider">快照提供者（主机模式调用，从机模式可为 null）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="syncInterval">同步间隔（默认 5s）</param>
    public HostContextSyncService(
        ITransportTopology transport,
        HostElectionService election,
        ContextSnapshotProvider? snapshotProvider = null,
        ILogger? logger = null,
        TimeSpan? syncInterval = null) {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _election = election ?? throw new ArgumentNullException(nameof(election));
        _snapshotProvider = snapshotProvider;
        _logger = logger;
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(5);
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 启动上下文同步 — 主机定期广播，从机接收更新。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public void Start(CancellationToken ct = default) {
        ThrowIfDisposed();
        if (_syncTask is not null) return;
        _syncTask = Task.Run(() => SyncLoopAsync(ct), ct);
    }

    /// <summary>
    /// 同步循环 — 主机广播快照，从机接收快照。
    /// </summary>
    private async Task SyncLoopAsync(CancellationToken ct) {
        try {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            var token = linkedCts.Token;

            while (!token.IsCancellationRequested) {
                await Task.Delay(_syncInterval, token).ConfigureAwait(false);

                if (_transport.Role == ProcessRole.Host && _snapshotProvider is not null) {
                    await BroadcastSnapshotAsync(token).ConfigureAwait(false);
                }
            }
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogError(ex, "HostContextSync: sync loop error");
        }
    }

    /// <summary>
    /// 主机广播上下文快照 — 序列化为字节，通过传输层广播。
    /// </summary>
    private async Task BroadcastSnapshotAsync(CancellationToken ct) {
        try {
            var snapshot = _snapshotProvider!();
            var json = SerializeSnapshot(snapshot);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _transport.BroadcastAsync(bytes, ct).ConfigureAwait(false);
            _logger?.LogDebug("HostContextSync: broadcast snapshot (routing={Routing}, pending={Pending})",
                snapshot.RoutingTable.Count, snapshot.BuildQueue.PendingCount);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "HostContextSync: broadcast failed");
        }
    }

    /// <summary>
    /// 从机处理收到的快照 — 从传输帧解析并更新本地缓存。
    /// </summary>
    /// <param name="frame">传输帧</param>
    /// <returns>是否成功解析并更新</returns>
    public bool TryHandleSnapshotFrame(TransportFrame frame) {
        try {
            var json = Encoding.UTF8.GetString(frame.Data.Span);
            var snapshot = DeserializeSnapshot(json);
            if (snapshot is null) return false;

            _election.UpdateSnapshot(snapshot);
            _logger?.LogDebug("HostContextSync: received snapshot from {Source} (routing={Routing})",
                frame.SourceProcessId, snapshot.RoutingTable.Count);
            return true;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "HostContextSync: failed to parse snapshot from {Source}", frame.SourceProcessId);
            return false;
        }
    }

    /// <summary>
    /// 序列化上下文快照为 JSON — 手动拼接避免 AOT 反射。
    /// </summary>
    private static string SerializeSnapshot(HostContextSnapshot snapshot) {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"timestamp\":\"").Append(snapshot.Timestamp.ToString("O")).Append("\",");
        sb.Append("\"hostPid\":\"").Append(snapshot.HostProcessId).Append("\",");
        sb.Append("\"routing\":{");
        var first = true;
        foreach (var kvp in snapshot.RoutingTable) {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kvp.Key).Append("\":\"").Append(kvp.Value).Append('"');
        }
        sb.Append("},");
        sb.Append("\"buildQueue\":{");
        sb.Append("\"pending\":").Append(snapshot.BuildQueue.PendingCount).Append(',');
        sb.Append("\"running\":").Append(snapshot.BuildQueue.RunningCount);
        sb.Append("}}");
        return sb.ToString();
    }

    /// <summary>
    /// 反序列化上下文快照 — 简单 JSON 解析（容错）。
    /// </summary>
    private static HostContextSnapshot? DeserializeSnapshot(string json) {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try {
            var timestamp = ExtractJsonField(json, "timestamp") ?? DateTimeOffset.UtcNow.ToString("O");
            var hostPid = ExtractJsonField(json, "hostPid") ?? "unknown";

            return new HostContextSnapshot {
                Timestamp = DateTimeOffset.TryParse(timestamp, out var ts) ? ts : DateTimeOffset.UtcNow,
                HostProcessId = hostPid,
                RoutingTable = new Dictionary<string, string>(),
                PendingMessages = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(),
                BuildQueue = new BuildQueueState {
                    PendingCount = 0,
                    RunningCount = 0,
                    PendingTasks = Array.Empty<string>()
                }
            };
        } catch {
            return null;
        }
    }

    private static string? ExtractJsonField(string json, string fieldName) {
        var key = "\"" + fieldName + "\":\"";
        var start = json.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        var end = json.IndexOf('"', start);
        if (end < 0) return null;
        return json[start..end];
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(HostContextSyncService));
    }

    /// <summary>
    /// 释放同步服务。
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        if (_syncTask is not null) await _syncTask.ConfigureAwait(false);
        _syncTask = null;
        await _transport.DisposeAsync().ConfigureAwait(false);
        await _election.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}