namespace JoinCode.Pipe;

/// <summary>代码会话管理器 — 维护代码会话的创建、查询、删除、列表与工作目录更新，线程安全</summary>
[Register(typeof(CodeSessionManager), ServiceLifetime.Singleton)]
public sealed partial class CodeSessionManager : ServiceEntity
{
    private readonly CodeSessionRepo _repo;
    private readonly AsyncLock _lock = new();
    private readonly IClockService _clock;

    /// <summary>
    /// 构造函数 — 注入会话仓储与可选的时钟服务
    /// </summary>
    /// <param name="repo">代码会话仓储</param>
    /// <param name="clock">时钟服务，为 null 时使用系统时钟</param>
    public CodeSessionManager(CodeSessionRepo repo, IClockService? clock = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 创建新的代码会话并持久化
    /// </summary>
    /// <param name="projectName">项目名称</param>
    /// <param name="workDirectory">工作目录路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新创建的会话记录</returns>
    public async ValueTask<CodeSessionRecord> CreateSessionAsync(
        string projectName,
        string workDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

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

    /// <summary>
    /// 根据会话标识符获取会话记录
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>会话记录；不存在时返回 null</returns>
    public async ValueTask<CodeSessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据会话标识符删除会话
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>删除成功返回 true；会话不存在返回 false</returns>
    public async ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var existing = await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (existing is null) return false;

        await _repo.DeleteAsync(sessionId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 列出全部代码会话记录
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>会话记录只读列表</returns>
    public async ValueTask<IReadOnlyList<CodeSessionRecord>> ListSessionsAsync(CancellationToken ct = default)
    {
        return await _repo.GetAllAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新指定会话的工作目录
    /// </summary>
    /// <param name="sessionId">会话标识符</param>
    /// <param name="newWorkDirectory">新的工作目录路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>更新成功返回 true；会话不存在返回 false</returns>
    public async ValueTask<bool> UpdateWorkDirectoryAsync(
        string sessionId,
        string newWorkDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newWorkDirectory);

        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var existing = await _repo.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (existing is null) return false;

        existing.WorkDirectory = newWorkDirectory;
        existing.UpdatedAt = _clock.GetUtcNowOffset();

        await _repo.SaveAsync(existing, ct).ConfigureAwait(false);
        return true;
    
    }

    /// <summary>释放托管资源 — 销毁异步锁</summary>
    protected override void OnDispose()
    {
        _lock.Dispose();
    }
}