namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskListResult {
    /// <summary>获取是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取运行时任务列表。</summary>
    public IReadOnlyList<RuntimeTask> Tasks { get; init; } = Array.Empty<RuntimeTask>();
    /// <summary>获取任务总数。</summary>
    public int TotalCount { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>创建成功结果。</summary>
    /// <param name="tasks">任务列表。</param>
    /// <param name="totalCount">任务总数。</param>
    public static RuntimeTaskListResult Ok(IReadOnlyList<RuntimeTask> tasks, int totalCount) =>
        new() { Success = true, Tasks = tasks, TotalCount = totalCount };
    /// <summary>创建失败结果。</summary>
    /// <param name="error">错误消息。</param>
    public static RuntimeTaskListResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
