namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Task 注册器 — 基于 MapRegistry，内部字典，对外暴露遍历器 + 字典视图
/// </summary>
public sealed class TaskRegistry : MapRegistry<ObjectId, AgentTask>
{
    /// <summary>注册任务（internal，AgentTask构造时自动调用）</summary>
    internal void Add(ObjectId id, AgentTask task) => AddCore(id, task);

    /// <summary>注销任务（internal，AgentTask.Dispose时自动调用）</summary>
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>按状态获取任务</summary>
    public IEnumerable<AgentTask> GetByStatus(TaskExecutionStatus status)
        => Where(t => t.Status == status);

    /// <summary>按执行者获取任务</summary>
    public IEnumerable<AgentTask> GetByAssignee(ObjectId assigneeId)
        => Where(t => t.AssigneeObjectId == assigneeId);

    /// <summary>按类型获取任务</summary>
    public IEnumerable<AgentTask> GetByType(TaskType type)
        => Where(t => t.Type == type);
}
