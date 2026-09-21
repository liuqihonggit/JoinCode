namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 构建实体 — 派生自 Entity，编译队列管理
/// </summary>
public sealed class BuildEntity : Entity {
    /// <summary>获取或设置项目路径。</summary>
    public string? ProjectPath { get; init; }
    /// <summary>获取或设置构建配置（如 Debug/Release）。</summary>
    public string? Configuration { get; init; }
    /// <summary>获取或设置构建执行状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    /// <summary>获取或设置构建输出内容。</summary>
    public string? Output { get; set; }
    /// <summary>获取或设置构建错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>获取构建实体注册表。</summary>
    public static BuildEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构建构建实体。
    /// </summary>
    /// <param name="projectPath">项目路径。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public BuildEntity(string? projectPath = null, string? configuration = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Build, sessionId, displayName ?? projectPath) {
        ProjectPath = projectPath;
        Configuration = configuration;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class BuildEntityRegistry : MapRegistry<ObjectId, BuildEntity> {
    internal void Add(ObjectId id, BuildEntity build) => AddCore(id, build);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>按执行状态获取构建实体集合。</summary>
    public IEnumerable<BuildEntity> GetByStatus(TaskExecutionStatus status) => Where(b => b.Status == status);
}

/// <summary>
/// 沙箱实体 — 派生自 Entity，沙箱生命周期管理
/// </summary>
public sealed class SandboxEntity : Entity {
    /// <summary>获取或设置沙箱工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取或设置沙箱执行状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    /// <summary>获取沙箱实体注册表。</summary>
    public static SandboxEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造沙箱实体。
    /// </summary>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public SandboxEntity(string? workingDirectory = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Sandbox, sessionId, displayName ?? workingDirectory) {
        WorkingDirectory = workingDirectory;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class SandboxEntityRegistry : MapRegistry<ObjectId, SandboxEntity> {
    internal void Add(ObjectId id, SandboxEntity sandbox) => AddCore(id, sandbox);
    internal bool Remove(ObjectId id) => RemoveCore(id);
}

/// <summary>
/// 代码仓库实体 — 派生自 Entity，代码索引仓库管理
/// </summary>
public sealed class RepoEntity : Entity {
    /// <summary>获取或设置代码仓库路径。</summary>
    public string? RepoPath { get; init; }
    /// <summary>获取或设置仓库执行状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    /// <summary>获取代码仓库实体注册表。</summary>
    public static RepoEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造代码仓库实体。
    /// </summary>
    /// <param name="repoPath">仓库路径。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public RepoEntity(string? repoPath = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Repo, sessionId, displayName ?? repoPath) {
        RepoPath = repoPath;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class RepoEntityRegistry : MapRegistry<ObjectId, RepoEntity> {
    internal void Add(ObjectId id, RepoEntity repo) => AddCore(id, repo);
    internal bool Remove(ObjectId id) => RemoveCore(id);
}

/// <summary>
/// Shell后台任务实体 — 派生自 Entity，Shell后台任务管理
/// </summary>
public sealed class ShellTaskEntity : Entity {
    /// <summary>获取或设置 Shell 命令。</summary>
    public string? Command { get; init; }
    /// <summary>获取或设置任务执行状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    /// <summary>获取或设置命令输出内容。</summary>
    public string? Output { get; set; }
    /// <summary>获取或设置错误信息。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>获取或设置进程退出码。</summary>
    public int? ExitCode { get; set; }

    /// <summary>获取 Shell 任务实体注册表。</summary>
    public static ShellTaskEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造 Shell 后台任务实体。
    /// </summary>
    /// <param name="command">Shell 命令。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public ShellTaskEntity(string? command = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.ShellCommand, sessionId, displayName ?? command) {
        Command = command;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class ShellTaskEntityRegistry : MapRegistry<ObjectId, ShellTaskEntity> {
    internal void Add(ObjectId id, ShellTaskEntity task) => AddCore(id, task);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>按执行状态获取 Shell 任务实体集合。</summary>
    public IEnumerable<ShellTaskEntity> GetByStatus(TaskExecutionStatus status) => Where(t => t.Status == status);
}

/// <summary>
/// 权限请求实体 — 派生自 Entity，权限请求等待/回调
/// </summary>
public sealed class PermissionRequestEntity : Entity {
    /// <summary>获取或设置工具名称。</summary>
    public string? ToolName { get; init; }
    /// <summary>获取或设置请求的操作。</summary>
    public string? RequestedAction { get; init; }
    /// <summary>获取或设置权限请求状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    /// <summary>获取权限请求实体注册表。</summary>
    public static PermissionRequestEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造权限请求实体。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="requestedAction">请求的操作。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public PermissionRequestEntity(string? toolName = null, string? requestedAction = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Request, sessionId, displayName ?? toolName) {
        ToolName = toolName;
        RequestedAction = requestedAction;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class PermissionRequestEntityRegistry : MapRegistry<ObjectId, PermissionRequestEntity> {
    internal void Add(ObjectId id, PermissionRequestEntity request) => AddCore(id, request);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>获取所有待处理的权限请求。</summary>
    public IEnumerable<PermissionRequestEntity> GetPending() => Where(r => r.Status == TaskExecutionStatus.Pending);
}

/// <summary>
/// 通知实体 — 派生自 Entity，通知队列管理
/// </summary>
public sealed class NotificationEntity : Entity {
    /// <summary>获取或设置通知消息内容。</summary>
    public string? Message { get; init; }
    /// <summary>获取或设置通知分类。</summary>
    public string? Category { get; init; }
    /// <summary>获取或设置通知是否已读。</summary>
    public bool IsRead { get; set; }

    /// <summary>获取通知实体注册表。</summary>
    public static NotificationEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造通知实体。
    /// </summary>
    /// <param name="message">通知消息内容。</param>
    /// <param name="category">通知分类。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public NotificationEntity(string? message = null, string? category = null, string? displayName = null, ObjectId sessionId = default)
        : base(ObjectType.Notification, sessionId, displayName ?? message) {
        Message = message;
        Category = category;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }
}

public sealed class NotificationEntityRegistry : MapRegistry<ObjectId, NotificationEntity> {
    internal void Add(ObjectId id, NotificationEntity notification) => AddCore(id, notification);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>获取所有未读通知。</summary>
    public IEnumerable<NotificationEntity> GetUnread() => Where(n => !n.IsRead);
}
