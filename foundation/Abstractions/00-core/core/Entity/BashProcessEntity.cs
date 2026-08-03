namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Bash 进程实体 — 派生自 Entity，追踪 Shell/bash 进程生命周期
/// 超时检测+强制终止，避免僵尸进程
/// </summary>
public sealed class BashProcessEntity : Entity
{
    public int? ProcessId { get; init; }
    public string? Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public BashProcessStatus Status { get; set; } = BashProcessStatus.Running;
    public int? ExitCode { get; set; }

    public static BashProcessEntityRegistry Registry { get; } = new();

    public BashProcessEntity(
        int? processId = null,
        string? command = null,
        string? workingDirectory = null,
        string? displayName = null)
        : base(ObjectType.Bash, displayName ?? command ?? $"pid:{processId}")
    {
        ProcessId = processId;
        Command = command;
        WorkingDirectory = workingDirectory;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    /// <summary>
    /// 回收判定 — Bash 进程已完成（有 ExitCode）且已持久化
    /// </summary>
    public override bool CanReclaim()
    {
        return base.CanReclaim() && ExitCode.HasValue;
    }
}

/// <summary>
/// Bash 进程状态
/// </summary>
public enum BashProcessStatus
{
    [EnumValue("running")] Running,
    [EnumValue("exited")] Exited,
    [EnumValue("killed")] Killed,
    [EnumValue("timed_out")] TimedOut,
}

public sealed class BashProcessEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, BashProcessEntity> _processes = new();
    internal void Add(ObjectId id, BashProcessEntity process) => _processes.TryAdd(id, process);
    internal bool Remove(ObjectId id) => _processes.TryRemove(id, out _);
    public BashProcessEntity? Get(ObjectId id) => _processes.GetValueOrDefault(id);
    public IReadOnlyList<BashProcessEntity> GetAll() => [.. _processes.Values];
    public IReadOnlyList<BashProcessEntity> GetRunning() => [.. _processes.Values.Where(p => p.Status == BashProcessStatus.Running)];
    public IReadOnlyList<BashProcessEntity> GetTimedOut() => [.. _processes.Values.Where(p => p.Status == BashProcessStatus.TimedOut)];
    public int Count => _processes.Count;
    public void Clear() => _processes.Clear();
}
