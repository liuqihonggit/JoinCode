namespace JoinCode.Pipe;

[Register(typeof(CodeSessionManager), ServiceLifetime.Singleton)]
public sealed partial class CodeSessionManager : ServiceEntity
{
    private readonly CodeSessionRepo _repo;
    private readonly AsyncLock _lock = new();
    private readonly IClockService _clock;

    public CodeSessionManager(CodeSessionRepo repo, IClockService? clock = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _clock = clock ?? SystemClockService.Instance;
    }

    public async ValueTask<CodeSessionRecord> CreateSessionAsync(
        string projectName,
        string workDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        var record = new CodeSessionRecord
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProjectName = projectName,
            WorkDirectory = workDirectory,
            Status = CodeSessionStatus.Active,
            CreatedAt = _clock.GetUtcNowOffset(),
            UpdatedAt = _clock.GetUtcNowOffset()
        };

        await _repo.SaveAsync(record, ct).ConfigureAwait(false);
        return record;
    
    }

    public async ValueTask<CodeSessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var existing = await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (existing is null) return false;

        await _repo.DeleteAsync(sessionId, ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<IReadOnlyList<CodeSessionRecord>> ListSessionsAsync(CancellationToken ct = default)
    {
        return await _repo.GetAllAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> UpdateWorkDirectoryAsync(
        string sessionId,
        string newWorkDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newWorkDirectory);

        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        var existing = await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (existing is null) return false;

        existing.WorkDirectory = newWorkDirectory;
        existing.UpdatedAt = _clock.GetUtcNowOffset();

        await _repo.SaveAsync(existing, ct).ConfigureAwait(false);
        return true;
    
    }

    protected override void OnDispose()
    {
        _lock.Dispose();
    }
}