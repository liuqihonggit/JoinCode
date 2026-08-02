namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Task 注册器 — 独立类，与 AgentRegistry 同套路
/// 管理所有 AgentTask 的注册/查询
/// 全局唯一实例通过 AgentTask.Registry 静态属性获取
/// </summary>
public sealed class TaskRegistry
{
    private readonly ConcurrentDictionary<ObjectId, AgentTask> _tasks = new();

    /// <summary>
    /// 注册任务（internal，AgentTask构造时自动调用，外部不手动调）
    /// </summary>
    internal void Add(ObjectId id, AgentTask task)
    {
        _tasks.TryAdd(id, task);
    }

    /// <summary>
    /// 注销任务（internal，AgentTask.Dispose时自动调用）
    /// </summary>
    internal bool Remove(ObjectId id)
    {
        return _tasks.TryRemove(id, out _);
    }

    /// <summary>
    /// 按ObjectId获取任务
    /// </summary>
    public AgentTask? Get(ObjectId id) => _tasks.GetValueOrDefault(id);

    /// <summary>
    /// 获取所有任务
    /// </summary>
    public IReadOnlyList<AgentTask> GetAll() => [.. _tasks.Values];

    /// <summary>
    /// 按状态获取任务
    /// </summary>
    public IReadOnlyList<AgentTask> GetByStatus(TaskExecutionStatus status)
        => [.. _tasks.Values.Where(t => t.Status == status)];

    /// <summary>
    /// 按执行者获取任务
    /// </summary>
    public IReadOnlyList<AgentTask> GetByAssignee(ObjectId assigneeId)
        => [.. _tasks.Values.Where(t => t.AssigneeObjectId == assigneeId)];

    /// <summary>
    /// 按类型获取任务
    /// </summary>
    public IReadOnlyList<AgentTask> GetByType(TaskType type)
        => [.. _tasks.Values.Where(t => t.Type == type)];

    /// <summary>
    /// 当前注册的任务总数
    /// </summary>
    public int Count => _tasks.Count;

    /// <summary>
    /// 清空所有注册（测试用）
    /// </summary>
    public void Clear()
    {
        _tasks.Clear();
    }
}
