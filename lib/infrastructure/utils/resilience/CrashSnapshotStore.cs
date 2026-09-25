namespace Infrastructure.Utils.Resilience;

/// <summary>
/// 崩溃快照存储 — 线程安全的有界队列，捕获 CrashSnapshot 并提供查询/确认/报告能力
/// </summary>
[Register(typeof(ICrashSnapshotStore), ServiceLifetime.Singleton)]
public sealed partial class CrashSnapshotStore : ICrashSnapshotStore {
    private readonly ConcurrentQueue<CrashSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, CrashSnapshot> _byId = new();
    private readonly int _maxCapacity;
    private int _unacknowledgedCount;

    /// <summary>
    /// 快照新增事件 — 当 Add 方法被调用时触发
    /// </summary>
    public event EventHandler<CrashSnapshot>? SnapshotAdded;

    /// <summary>
    /// 构造崩溃快照存储
    /// </summary>
    /// <param name="maxCapacity">最大容量，超出后淘汰最旧快照</param>
    public CrashSnapshotStore(int maxCapacity = 200) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity);
        _maxCapacity = maxCapacity;
    }

    /// <summary>
    /// 当前快照总数
    /// </summary>
    public int TotalCount => _snapshots.Count;

    /// <summary>
    /// 未确认快照数
    /// </summary>
    public int UnacknowledgedCount => _unacknowledgedCount;

    /// <summary>
    /// 添加崩溃快照到存储，超出容量时淘汰最旧快照
    /// </summary>
    /// <param name="snapshot">崩溃快照</param>
    public void Add(CrashSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);

        _snapshots.Enqueue(snapshot);
        _byId[snapshot.Id] = snapshot;
        Interlocked.Increment(ref _unacknowledgedCount);

        while (_snapshots.Count > _maxCapacity && _snapshots.TryDequeue(out var removed)) {
            _byId.TryRemove(removed.Id, out _);
            if (removed.State == CrashSnapshotState.Captured)
                Interlocked.Decrement(ref _unacknowledgedCount);
        }

        SnapshotAdded?.Invoke(this, snapshot);

        Diag.WriteError($"[CrashStore] 快照已捕获: {snapshot.ToSummary()}");
    }

    /// <summary>
    /// 获取最近的若干条快照（按时间倒序）
    /// </summary>
    /// <param name="count">要获取的条数</param>
    /// <returns>快照列表（最新在前）</returns>
    public IReadOnlyList<CrashSnapshot> GetRecent(int count = 20) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return _snapshots.Reverse().Take(count).ToList();
    }

    /// <summary>
    /// 按围栏名称筛选快照
    /// </summary>
    /// <param name="fenceName">围栏名称</param>
    /// <returns>匹配的快照列表（最新在前）</returns>
    public IReadOnlyList<CrashSnapshot> GetByFence(string fenceName) {
        ArgumentException.ThrowIfNullOrEmpty(fenceName);
        return _snapshots.Where(s => s.FenceName == fenceName).Reverse().ToList();
    }

    /// <summary>
    /// 按唯一标识查找快照
    /// </summary>
    /// <param name="id">快照 ID</param>
    /// <returns>匹配的快照；未找到返回 null</returns>
    public CrashSnapshot? GetById(Guid id) =>
        _byId.GetValueOrDefault(id);

    /// <summary>
    /// 确认指定快照（标记为已确认，减少未确认计数）
    /// </summary>
    /// <param name="id">快照 ID</param>
    public void Acknowledge(Guid id) {
        var snapshot = GetById(id);
        if (snapshot is null || snapshot.State != CrashSnapshotState.Captured) return;

        snapshot.State = CrashSnapshotState.Acknowledged;
        Interlocked.Decrement(ref _unacknowledgedCount);
    }

    /// <summary>
    /// 格式化崩溃快照报告（含状态标记、严重级别、异常信息、时间等）
    /// </summary>
    /// <param name="count">要包含的最近条数</param>
    /// <returns>格式化的报告字符串</returns>
    public string FormatReport(int count = 20) {
        var recent = GetRecent(count);
        if (recent.Count == 0)
            return "无崩溃快照记录。";

        var sb = new StringBuilder();
        sb.AppendLine($"崩溃快照报告（最近 {recent.Count} 条，共 {TotalCount} 条，未确认 {UnacknowledgedCount} 条）");
        sb.AppendLine(new string('─', 60));

        foreach (var s in recent) {
            var stateMark = s.State switch {
                CrashSnapshotState.Captured => "🔴",
                CrashSnapshotState.Acknowledged => "🟡",
                CrashSnapshotState.Resolved => "🟢",
                CrashSnapshotState.Suppressed => "⚪",
                _ => "?"
            };

            sb.AppendLine($"{stateMark} [{s.Severity.ToValue()}] {s.FenceName}");
            sb.AppendLine($"  {s.ExceptionType}: {s.ExceptionMessage}");
            if (s.ErrorCode is not null)
                sb.AppendLine($"  错误码: {s.ErrorCode}");
            if (s.ExecutionContext.ToolName is not null)
                sb.AppendLine($"  工具: {s.ExecutionContext.ToolName}");
            if (s.ExecutionContext.TurnIndex is not null)
                sb.AppendLine($"  轮次: {s.ExecutionContext.TurnIndex}");
            sb.AppendLine($"  时间: {s.CapturedAt:HH:mm:ss.fff}");
            sb.AppendLine($"  ID: {s.Id:N}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}