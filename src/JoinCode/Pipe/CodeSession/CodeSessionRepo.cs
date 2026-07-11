namespace JoinCode.Pipe;

public enum CodeSessionStatus
{
    [EnumValue("active")] Active,
    [EnumValue("closed")] Closed
}

public sealed class CodeSessionRecord
{
    public required string SessionId { get; init; }
    public required string ProjectName { get; set; }
    public required string WorkDirectory { get; set; }
    public CodeSessionStatus Status { get; set; } = CodeSessionStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Register]
public sealed partial class CodeSessionRepo
{
    private readonly ConcurrentDictionary<string, CodeSessionRecord> _store = new(StringComparer.Ordinal);

    public ValueTask SaveAsync(CodeSessionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _store[record.SessionId] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask<CodeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryGetValue(sessionId, out var record);
        return ValueTask.FromResult(record);
    }

    public ValueTask DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<CodeSessionRecord>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<CodeSessionRecord> result = _store.Values
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        return ValueTask.FromResult(result);
    }
}