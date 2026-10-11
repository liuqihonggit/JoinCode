namespace JoinCode.Abstractions.Models.Task;

public sealed record TaskPossibility {
    /// <summary>获取可能性描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取是否被排除。</summary>
    public bool Excluded { get; init; }
    /// <summary>获取排除原因。</summary>
    public string? ExclusionReason { get; init; }
}

public sealed record StructuredTaskEntry {
    /// <summary>获取执行顺序。</summary>
    public required int Order { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取执行结果。</summary>
    public string? Result { get; init; }
    /// <summary>获取可能性列表。</summary>
    public List<TaskPossibility> Possibilities { get; init; } = new();
    /// <summary>获取执行状态。</summary>
    public string Status { get; init; } = TaskExecutionStatusEnumConstants.Pending;
}