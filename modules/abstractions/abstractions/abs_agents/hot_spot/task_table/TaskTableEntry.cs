namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 任务表条目 — 任务表.md 的一行，含热点标注
/// </summary>
public sealed record TaskTableEntry {
    /// <summary>获取任务标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取关联文件列表。</summary>
    public required IReadOnlyList<string> Files { get; init; } = [];
    /// <summary>获取任务角色。</summary>
    public required string Role { get; init; } = "worker";
    /// <summary>获取依赖任务标识列表。</summary>
    public required IReadOnlyList<string> Dependencies { get; init; } = [];
    /// <summary>获取验证方式描述。</summary>
    public required string Verification { get; init; } = string.Empty;
    /// <summary>获取是否为热点文件。</summary>
    public required bool IsHotFile { get; init; }
    /// <summary>获取或设置热点标注。</summary>
    public string HotSpotAnnotation { get; init; } = string.Empty;
    /// <summary>获取任务状态。</summary>
    public required string Status { get; init; } = DreamTaskStatus.Pending.ToValue();
}