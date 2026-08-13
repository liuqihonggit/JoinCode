namespace Infrastructure.Utils.Resilience;

[Register(typeof(ICrashSnapshotStore))]
public sealed partial class CrashSnapshotStore : ICrashSnapshotStore
{
    private readonly ConcurrentQueue<CrashSnapshot> _snapshots = new();
    private readonly int _maxCapacity;
    private int _unacknowledgedCount;

    public event EventHandler<CrashSnapshot>? SnapshotAdded;

    public CrashSnapshotStore(int maxCapacity = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity);
        _maxCapacity = maxCapacity;
    }

    public int TotalCount => _snapshots.Count;
    public int UnacknowledgedCount => _unacknowledgedCount;

    public void Add(CrashSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _snapshots.Enqueue(snapshot);
        Interlocked.Increment(ref _unacknowledgedCount);

        while (_snapshots.Count > _maxCapacity && _snapshots.TryDequeue(out var removed))
        {
            if (removed.State == CrashSnapshotState.Captured)
                Interlocked.Decrement(ref _unacknowledgedCount);
        }

        SnapshotAdded?.Invoke(this, snapshot);

        Diag.WriteError($"[CrashStore] 快照已捕获: {snapshot.ToSummary()}");
    }

    public IReadOnlyList<CrashSnapshot> GetRecent(int count = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return _snapshots.Reverse().Take(count).ToList();
    }

    public IReadOnlyList<CrashSnapshot> GetByFence(string fenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fenceName);
        return _snapshots.Where(s => s.FenceName == fenceName).Reverse().ToList();
    }

    public CrashSnapshot? GetById(Guid id) =>
        _snapshots.FirstOrDefault(s => s.Id == id);

    public void Acknowledge(Guid id)
    {
        var snapshot = GetById(id);
        if (snapshot is null || snapshot.State != CrashSnapshotState.Captured) return;

        snapshot.State = CrashSnapshotState.Acknowledged;
        Interlocked.Decrement(ref _unacknowledgedCount);
    }

    public string FormatReport(int count = 20)
    {
        var recent = GetRecent(count);
        if (recent.Count == 0)
            return "无崩溃快照记录。";

        var sb = new StringBuilder();
        sb.AppendLine($"崩溃快照报告（最近 {recent.Count} 条，共 {TotalCount} 条，未确认 {UnacknowledgedCount} 条）");
        sb.AppendLine(new string('─', 60));

        foreach (var s in recent)
        {
            var stateMark = s.State switch
            {
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
