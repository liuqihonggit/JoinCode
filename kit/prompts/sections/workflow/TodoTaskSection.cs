
namespace Core.Prompts.Sections;

/// <summary>
/// Todo/Task工具部分 - 关于任务管理的说明
/// </summary>
[PromptSection(Name = "todo_task", Order = 28)]
public static class TodoTaskSection {
    /// <summary>
    /// 获取任务管理部分的提示词内容。当未启用 Todo/Task 工具时返回 null。
    /// </summary>
    /// <returns>任务管理指导文本；若未启用任何任务工具则返回 null。</returns>
    public static string? GetContent() {
        var hasTodoTool = PromptConfigSnapshot.Current.HasTodoTool;
        var hasTaskTool = PromptConfigSnapshot.Current.HasTaskTool;
        if (!hasTodoTool && !hasTaskTool) {
            return null;
        }

        var toolName = hasTodoTool ? TodoToolName.TodoWrite.ToValue() : TaskToolName.TaskCreate.ToValue();

        return $"""
# 任务管理

使用{toolName}工具分解和管理工作。这些工具有助于规划您的工作并帮助用户跟踪您的进度。
完成任务后立即标记为完成。不要批量完成多个任务后再标记。

任务管理最佳实践：
- 将大型任务分解为可管理的小任务
- 为每个任务设置明确的完成标准
- 及时更新任务状态
- 在任务描述中包含足够的信息
""";
    }

    /// <summary>
    /// 创建任务管理 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>任务管理 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("todo_task", GetContent);
}