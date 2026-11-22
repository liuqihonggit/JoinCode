namespace JoinCode.Abstractions.Models.ErrorRecovery;

public interface ICrashSnapshotStore {
    /// <summary>添加崩溃快照。</summary>
    void Add(CrashSnapshot snapshot);
    /// <summary>获取最近的崩溃快照列表。</summary>
    IReadOnlyList<CrashSnapshot> GetRecent(int count = 20);
    /// <summary>按围栏名称获取崩溃快照列表。</summary>
    IReadOnlyList<CrashSnapshot> GetByFence(string fenceName);
    /// <summary>按标识获取崩溃快照。</summary>
    CrashSnapshot? GetById(Guid id);
    /// <summary>确认指定崩溃快照。</summary>
    void Acknowledge(Guid id);
    /// <summary>获取快照总数。</summary>
    int TotalCount { get; }
    /// <summary>获取未确认快照数。</summary>
    int UnacknowledgedCount { get; }
    /// <summary>快照添加事件。</summary>
    event EventHandler<CrashSnapshot>? SnapshotAdded;
}