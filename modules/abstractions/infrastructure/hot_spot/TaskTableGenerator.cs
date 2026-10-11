namespace Infrastructure.HotSpot;

/// <summary>
/// 任务表生成器实现 — 产出 Markdown 表格，含热点标注列
/// </summary>
[Register(typeof(ITaskTableGenerator), ServiceLifetime.Singleton)]
public sealed class TaskTableGenerator : ITaskTableGenerator {
    /// <summary>
    /// 根据任务条目生成 Markdown 任务表，含热点标注列
    /// </summary>
    /// <param name="entries">任务表条目列表</param>
    /// <returns>渲染后的 Markdown 字符串</returns>
    public string Generate(IReadOnlyList<TaskTableEntry> entries) {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            return "# 任务表\n\n（无任务）\n";

        var builder = new MarkdownTableBuilder()
            .WithTitle("任务表")
            .AddHeader("编号", "描述", "涉及文件", "角色", "依赖", "验证方式", "热文件", "热点标注", "状态");

        foreach (var e in entries) {
            var files = string.Join(", ", e.Files);
            var deps = string.Join(", ", e.Dependencies);
            var hot = e.IsHotFile ? "🔥" : "";
            var annotation = string.IsNullOrEmpty(e.HotSpotAnnotation) ? "" : e.HotSpotAnnotation;
            builder.AddRow(e.Id, e.Description, files, e.Role, deps, e.Verification, hot, annotation, e.Status);
        }

        return builder.Build();
    }

    /// <summary>
    /// 更新指定任务的状态并重新生成任务表
    /// </summary>
    /// <param name="entries">原任务表条目列表</param>
    /// <param name="taskId">待更新任务的标识</param>
    /// <param name="newStatus">新状态文本</param>
    /// <returns>更新后渲染的 Markdown 字符串</returns>
    public string UpdateStatus(IReadOnlyList<TaskTableEntry> entries, string taskId, string newStatus) {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var updated = entries.Select(e =>
            e.Id == taskId ? e with { Status = newStatus } : e).ToList();

        return Generate(updated);
    }
}