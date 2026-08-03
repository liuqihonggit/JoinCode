namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 具有对象标识的上下文 — 管道 Context 实现此接口即可被 LoggingScopeMiddleware 识别
/// 适用于 Context 本身不是 Entity 但持有 Entity 引用的场景（如 ToolExecutionContext）
/// </summary>
public interface IHasObjectId
{
    /// <summary>上下文关联的 ObjectId — 优先返回主 Entity 的 ObjectId</summary>
    ObjectId? ContextObjectId { get; }
}
