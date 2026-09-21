
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// SubagentStop 质量关卡 — 在子代理结束时执行质量检查
/// </summary>
public interface ISubagentStopCheckpoint {
    /// <summary>异步执行质量检查。</summary>
    Task<CheckpointResult> ExecuteAsync(CheckpointContext context, CancellationToken ct = default);
}

/// <summary>
/// 关卡上下文
/// </summary>
public sealed class CheckpointContext {
    /// <summary>获取代理 ID。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取会话 ID。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
    /// <summary>获取工作目录。</summary>
    public string WorkingDirectory { get; init; } = "";
}

/// <summary>
/// 关卡结果
/// </summary>
public sealed class CheckpointResult {
    /// <summary>获取是否通过检查。</summary>
    public bool Passed { get; init; }
    /// <summary>获取违规项列表。</summary>
    public IReadOnlyList<CheckpointViolation> Violations { get; init; } = [];

    /// <summary>创建通过结果。</summary>
    public static CheckpointResult Pass(IReadOnlyList<CheckpointViolation>? warnings = null) => new() { Passed = true, Violations = warnings ?? [] };
    /// <summary>创建失败结果。</summary>
    public static CheckpointResult Fail(IReadOnlyList<CheckpointViolation> violations) => new() { Passed = false, Violations = violations };
}

/// <summary>
/// 关卡违规项
/// </summary>
public sealed class CheckpointViolation {
    /// <summary>获取规则名称。</summary>
    public required string Rule { get; init; }
    /// <summary>获取违规消息。</summary>
    public required string Message { get; init; }
    /// <summary>获取严重级别。</summary>
    public string Severity { get; init; } = "error";
}
