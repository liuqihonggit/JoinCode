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

    public BuildEntity(string? projectPath = null, string? configuration = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Build, sessionId, displayName ?? projectPath)
    {
        ProjectPath = projectPath;
        Configuration = configuration;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class BuildEntityRegistry : MapRegistry<ObjectId, BuildEntity>
{
    internal void Add(ObjectId id, BuildEntity build) => AddCore(id, build);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<BuildEntity> GetByStatus(TaskExecutionStatus status) => Where(b => b.Status == status);
}

/// <summary>
/// 沙箱实体 — 派生自 Entity，沙箱生命周期管理
/// </summary>
public sealed class SandboxEntity : Entity
{
    public string? WorkingDirectory { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public static SandboxEntityRegistry Registry { get; } = new();

    public SandboxEntity(string? workingDirectory = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Sandbox, sessionId, displayName ?? workingDirectory)
    {
        WorkingDirectory = workingDirectory;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class SandboxEntityRegistry : MapRegistry<ObjectId, SandboxEntity>
{
    internal void Add(ObjectId id, SandboxEntity sandbox) => AddCore(id, sandbox);
    internal bool Remove(ObjectId id) => RemoveCore(id);
}

/// <summary>
/// 代码仓库实体 — 派生自 Entity，代码索引仓库管理
/// </summary>
public sealed class RepoEntity : Entity
{
    public string? RepoPath { get; init; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public static RepoEntityRegistry Registry { get; } = new();

    public RepoEntity(string? repoPath = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Repo, sessionId, displayName ?? repoPath)
    {
        RepoPath = repoPath;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class RepoEntityRegistry : MapRegistry<ObjectId, RepoEntity>
{
    internal void Add(ObjectId id, RepoEntity repo) => AddCore(id, repo);
    internal bool Remove(ObjectId id) => RemoveCore(id);
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

    public ShellTaskEntity(string? command = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.ShellCommand, sessionId, displayName ?? command)
    {
        Command = command;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class ShellTaskEntityRegistry : MapRegistry<ObjectId, ShellTaskEntity>
{
    internal void Add(ObjectId id, ShellTaskEntity task) => AddCore(id, task);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<ShellTaskEntity> GetByStatus(TaskExecutionStatus status) => Where(t => t.Status == status);
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

    public PermissionRequestEntity(string? toolName = null, string? requestedAction = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Request, sessionId, displayName ?? toolName)
    {
        ToolName = toolName;
        RequestedAction = requestedAction;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class PermissionRequestEntityRegistry : MapRegistry<ObjectId, PermissionRequestEntity>
{
    internal void Add(ObjectId id, PermissionRequestEntity request) => AddCore(id, request);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<PermissionRequestEntity> GetPending() => Where(r => r.Status == TaskExecutionStatus.Pending);
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

    public NotificationEntity(string? message = null, string? category = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Notification, sessionId, displayName ?? message)
    {
        Message = message;
        Category = category;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);
}

public sealed class NotificationEntityRegistry : MapRegistry<ObjectId, NotificationEntity>
{
    internal void Add(ObjectId id, NotificationEntity notification) => AddCore(id, notification);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<NotificationEntity> GetUnread() => Where(n => !n.IsRead);
}
