namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 任务表生成器 — 从任务列表产出结构化任务表.md（含热点标注）
/// 纯逻辑生成 Markdown，不接入 GoalEngine（接入在 T8.3）
/// </summary>
public interface ITaskTableGenerator
{
    /// <summary>
    /// 生成任务表 Markdown 内容
    /// </summary>
    string Generate(IReadOnlyList<TaskTableEntry> entries);

    /// <summary>
    /// 增量更新：标记某任务状态变化，返回更新后的 Markdown
    /// </summary>
    string UpdateStatus(IReadOnlyList<TaskTableEntry> entries, string taskId, string newStatus);
}
