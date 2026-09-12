namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 工具执行实体 — 每次工具调用都创建一个实例
/// 统一: ToolName + ToolUseId + 执行参数摘要 + 结果摘要 + 关联 SpanId
/// 所有工具（无论耗时）都走同一套 Entity 生命周期，确保：
///   1. 链路追踪完整（ObjectIdManager 全局可查）
///   2. 内存泄漏检测无盲区（超时/僵尸统一检测）
///   3. 不需要预判工具是否耗时
/// 与 LoggingScopeMiddleware (w3) 配合: ToolExecutionContext 实现 IHasObjectId 返回 ExecutionEntity?.ObjectId
/// 子类（BashProcessEntity 等）仅为需要额外字段的工具服务，非必需
/// </summary>
public class ToolExecutionEntity : Entity
{
    /// <summary>工具名称（如 "bash", "web_fetch", "read_file"）</summary>
    public string ToolName { get; }

    /// <summary>工具调用ID — LLM 返回的 tool_use 唯一标识</summary>
    public string? ToolUseId { get; init; }

    /// <summary>关联的遥测 SpanId — 与 PermissionAwareToolExecutor 创建的 Span 对接</summary>
    public string? SpanId { get; init; }

    /// <summary>执行参数摘要（截断，避免内存爆炸）</summary>
    public string? ArgumentsSummary { get; set; }

    /// <summary>结果摘要（完成后设置）</summary>
    public string? ResultSummary { get; set; }

    /// <summary>执行是否出错</summary>
    public bool IsError { get; set; }

    /// <summary>关联的 Session ObjectId — 属于哪个会话</summary>
    public ObjectId? SessionObjectId { get; set; }

    /// <summary>全局注册器 — 查询所有活跃/超时/僵尸的工具执行</summary>
    public static ToolExecutionEntityRegistry Registry { get; } = new();

    public ToolExecutionEntity(
        string toolName,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Tool, sessionId, displayName ?? toolName)
    {
        ToolName = toolName;
        ToolUseId = toolUseId;
        SpanId = spanId;
        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 子类专用构造器 — 允许指定 ObjectType（如 BashProcessEntity 用 ObjectType.ShellCommand）
    /// </summary>
    protected ToolExecutionEntity(
        ObjectType objectType,
        string toolName,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(objectType, sessionId, displayName ?? toolName)
    {
        ToolName = toolName;
        ToolUseId = toolUseId;
        SpanId = spanId;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);

    /// <summary>
    /// 将基类字段拷贝到克隆体 — 子类 Clone 调用此方法避免重复代码
    /// SessionObjectId 通过 RemapNullableOrThrow 重映射，未映射则抛异常暴露引用断裂
    /// 克隆后 Touch() 刷新活跃时间，避免 EntityReaper 立即回收
    /// </summary>
    protected void ApplyCloneState(ToolExecutionEntity cloned, CloneContext context)
    {
        cloned.ArgumentsSummary = ArgumentsSummary;
        cloned.ResultSummary = ResultSummary;
        cloned.IsError = IsError;
        cloned.SessionObjectId = context.RemapNullableOrThrow(SessionObjectId);
        cloned.LifecycleState = LifecycleState;
        cloned.StartedAt = StartedAt;
        cloned.CompletedAt = CompletedAt;
        cloned.LastActivityAt = LastActivityAt;
        cloned.Touch();
        context.Map(ObjectId, cloned.ObjectId);
    }

    /// <summary>
    /// 跨会话深拷贝 — 新 ObjectId + 目标会话，深拷贝所有字段
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new ToolExecutionEntity(
            toolName: ToolName,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId);
        ApplyCloneState(cloned, context);
        return cloned;
    }
}

/// <summary>
/// 工具执行实体全局注册器 — 基于 MapRegistry，统一查询所有工具执行的生命周期
/// </summary>
public sealed class ToolExecutionEntityRegistry : MapRegistry<ObjectId, ToolExecutionEntity>
{
    internal void Add(ObjectId id, ToolExecutionEntity entity) => AddCore(id, entity);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<ToolExecutionEntity> GetActive() => Where(e => e.LifecycleState == EntityLifecycle.Active);
    public IEnumerable<ToolExecutionEntity> GetCompleted() => Where(e => e.LifecycleState == EntityLifecycle.Completed);
    public IEnumerable<ToolExecutionEntity> GetTimedOut() => Where(e => e.IsTimedOut);
    public IEnumerable<ToolExecutionEntity> GetByToolName(string toolName) => Where(e => string.Equals(e.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
}
