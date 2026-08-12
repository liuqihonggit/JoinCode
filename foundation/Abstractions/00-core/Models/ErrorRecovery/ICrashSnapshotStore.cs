namespace JoinCode.Abstractions.Models.ErrorRecovery;

public interface ICrashSnapshotStore
{
    void Add(CrashSnapshot snapshot);
    IReadOnlyList<CrashSnapshot> GetRecent(int count = 20);
    IReadOnlyList<CrashSnapshot> GetByFence(string fenceName);
    CrashSnapshot? GetById(Guid id);
    void Acknowledge(Guid id);
    int TotalCount { get; }
    int UnacknowledgedCount { get; }
    event EventHandler<CrashSnapshot>? SnapshotAdded;
}
