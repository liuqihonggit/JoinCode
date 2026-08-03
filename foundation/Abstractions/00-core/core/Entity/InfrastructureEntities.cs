namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 构建实体 — 派生自 Entity，编译队列管理
/// </summary>
public sealed class BuildEntity : Entity
{
    public string? ProjectPath { get; init; }
    public string? Configuration { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }

    public static BuildEntityRegistry Registry { get; } = new();

    public BuildEntity(string? projectPath = null, string? configuration = null, string? displayName = null)
        : base(ObjectType.Build, displayName ?? projectPath)
    {
        ProjectPath = projectPath;
        Configuration = configuration;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class BuildEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, BuildEntity> _builds = new();
    internal void Add(ObjectId id, BuildEntity build) => _builds.TryAdd(id, build);
    internal bool Remove(ObjectId id) => _builds.TryRemove(id, out _);
    public BuildEntity? Get(ObjectId id) => _builds.GetValueOrDefault(id);
    public IReadOnlyList<BuildEntity> GetAll() => [.. _builds.Values];
    public IReadOnlyList<BuildEntity> GetByStatus(TaskExecutionStatus status) => [.. _builds.Values.Where(b => b.Status == status)];
    public int Count => _builds.Count;
    public void Clear() => _builds.Clear();
}

/// <summary>
/// 沙箱实体 — 派生自 Entity，沙箱生命周期管理
/// </summary>
public sealed class SandboxEntity : Entity
{
    public string? WorkingDirectory { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public static SandboxEntityRegistry Registry { get; } = new();

    public SandboxEntity(string? workingDirectory = null, string? displayName = null)
        : base(ObjectType.Sandbox, displayName ?? workingDirectory)
    {
        WorkingDirectory = workingDirectory;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class SandboxEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, SandboxEntity> _sandboxes = new();
    internal void Add(ObjectId id, SandboxEntity sandbox) => _sandboxes.TryAdd(id, sandbox);
    internal bool Remove(ObjectId id) => _sandboxes.TryRemove(id, out _);
    public SandboxEntity? Get(ObjectId id) => _sandboxes.GetValueOrDefault(id);
    public IReadOnlyList<SandboxEntity> GetAll() => [.. _sandboxes.Values];
    public int Count => _sandboxes.Count;
    public void Clear() => _sandboxes.Clear();
}

/// <summary>
/// 代码仓库实体 — 派生自 Entity，代码索引仓库管理
/// </summary>
public sealed class RepoEntity : Entity
{
    public string? RepoPath { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public static RepoEntityRegistry Registry { get; } = new();

    public RepoEntity(string? repoPath = null, string? displayName = null)
        : base(ObjectType.Repo, displayName ?? repoPath)
    {
        RepoPath = repoPath;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class RepoEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, RepoEntity> _repos = new();
    internal void Add(ObjectId id, RepoEntity repo) => _repos.TryAdd(id, repo);
    internal bool Remove(ObjectId id) => _repos.TryRemove(id, out _);
    public RepoEntity? Get(ObjectId id) => _repos.GetValueOrDefault(id);
    public IReadOnlyList<RepoEntity> GetAll() => [.. _repos.Values];
    public int Count => _repos.Count;
    public void Clear() => _repos.Clear();
}

/// <summary>
/// Shell后台任务实体 — 派生自 Entity，Shell后台任务管理
/// </summary>
public sealed class ShellTaskEntity : Entity
{
    public string? Command { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ExitCode { get; set; }

    public static ShellTaskEntityRegistry Registry { get; } = new();

    public ShellTaskEntity(string? command = null, string? displayName = null)
        : base(ObjectType.Task, displayName ?? command)
    {
        Command = command;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class ShellTaskEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, ShellTaskEntity> _tasks = new();
    internal void Add(ObjectId id, ShellTaskEntity task) => _tasks.TryAdd(id, task);
    internal bool Remove(ObjectId id) => _tasks.TryRemove(id, out _);
    public ShellTaskEntity? Get(ObjectId id) => _tasks.GetValueOrDefault(id);
    public IReadOnlyList<ShellTaskEntity> GetAll() => [.. _tasks.Values];
    public IReadOnlyList<ShellTaskEntity> GetByStatus(TaskExecutionStatus status) => [.. _tasks.Values.Where(t => t.Status == status)];
    public int Count => _tasks.Count;
    public void Clear() => _tasks.Clear();
}

/// <summary>
/// 权限请求实体 — 派生自 Entity，权限请求等待/回调
/// </summary>
public sealed class PermissionRequestEntity : Entity
{
    public string? ToolName { get; init; }
    public string? RequestedAction { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public static PermissionRequestEntityRegistry Registry { get; } = new();

    public PermissionRequestEntity(string? toolName = null, string? requestedAction = null, string? displayName = null)
        : base(ObjectType.Request, displayName ?? toolName)
    {
        ToolName = toolName;
        RequestedAction = requestedAction;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class PermissionRequestEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, PermissionRequestEntity> _requests = new();
    internal void Add(ObjectId id, PermissionRequestEntity request) => _requests.TryAdd(id, request);
    internal bool Remove(ObjectId id) => _requests.TryRemove(id, out _);
    public PermissionRequestEntity? Get(ObjectId id) => _requests.GetValueOrDefault(id);
    public IReadOnlyList<PermissionRequestEntity> GetAll() => [.. _requests.Values];
    public IReadOnlyList<PermissionRequestEntity> GetPending() => [.. _requests.Values.Where(r => r.Status == TaskExecutionStatus.Pending)];
    public int Count => _requests.Count;
    public void Clear() => _requests.Clear();
}

/// <summary>
/// 通知实体 — 派生自 Entity，通知队列管理
/// </summary>
public sealed class NotificationEntity : Entity
{
    public string? Message { get; init; }
    public string? Category { get; init; }
    public bool IsRead { get; set; }

    public static NotificationEntityRegistry Registry { get; } = new();

    public NotificationEntity(string? message = null, string? category = null, string? displayName = null)
        : base(ObjectType.Notification, displayName ?? message)
    {
        Message = message;
        Category = category;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class NotificationEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, NotificationEntity> _notifications = new();
    internal void Add(ObjectId id, NotificationEntity notification) => _notifications.TryAdd(id, notification);
    internal bool Remove(ObjectId id) => _notifications.TryRemove(id, out _);
    public NotificationEntity? Get(ObjectId id) => _notifications.GetValueOrDefault(id);
    public IReadOnlyList<NotificationEntity> GetAll() => [.. _notifications.Values];
    public IReadOnlyList<NotificationEntity> GetUnread() => [.. _notifications.Values.Where(n => !n.IsRead)];
    public int Count => _notifications.Count;
    public void Clear() => _notifications.Clear();
}
