namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Task 注册器 — 基于 MapRegistry，内部字典，对外暴露遍历器 + 字典视图
/// GetByType/GetByAssignee 用次级索引 O(1) 查找（Type/AssigneeObjectId 不可变），GetByStatus 保持 O(n) 遍历（Status 可变）
/// </summary>
public sealed class TaskRegistry : MapRegistry<ObjectId, AgentTask> {
    private readonly SecondaryIndex<ObjectId, AgentTask, TaskType> _byType;
    private readonly SecondaryIndex<ObjectId, AgentTask, ObjectId> _byAssignee;

    /// <summary>构造 TaskRegistry，初始化次级索引</summary>
    public TaskRegistry() {
        _byType = CreateIndex(t => t.Type);
        _byAssignee = CreateIndex(t => t.AssigneeObjectId ?? ObjectId.Empty);
    }

    /// <summary>注册任务（internal，AgentTask构造时自动调用）</summary>
    internal void Add(ObjectId id, AgentTask task) => AddCore(id, task);

    /// <summary>注销任务（internal，AgentTask.Dispose时自动调用）</summary>
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>按状态获取任务（Status 可变，保持 O(n) 遍历）</summary>
    public IEnumerable<AgentTask> GetByStatus(TaskExecutionStatus status)
        => Where(t => t.Status == status);

    /// <summary>按执行者获取任务（AssigneeObjectId 不可变，O(1) 索引查找）</summary>
    public IEnumerable<AgentTask> GetByAssignee(ObjectId assigneeId)
        => _byAssignee.GetValues(assigneeId, AsDictionary());

    /// <summary>按类型获取任务（Type 不可变，O(1) 索引查找）</summary>
    public IEnumerable<AgentTask> GetByType(TaskType type)
        => _byType.GetValues(type, AsDictionary());
}