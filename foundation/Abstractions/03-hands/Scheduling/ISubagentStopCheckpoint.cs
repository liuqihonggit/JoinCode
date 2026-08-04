
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// SubagentStop 质量关卡 — 在子代理结束时执行质量检查
/// </summary>
public interface ISubagentStopCheckpoint
{
    Task<CheckpointResult> ExecuteAsync(CheckpointContext context, CancellationToken ct = default);
}

/// <summary>
/// 关卡上下文
/// </summary>
public sealed class CheckpointContext
{
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public string? WorktreePath { get; init; }
    public string WorkingDirectory { get; init; } = "";
}

/// <summary>
/// 关卡结果
/// </summary>
public sealed class CheckpointResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<CheckpointViolation> Violations { get; init; } = [];

    public static CheckpointResult Pass(IReadOnlyList<CheckpointViolation>? warnings = null) => new() { Passed = true, Violations = warnings ?? [] };
    public static CheckpointResult Fail(IReadOnlyList<CheckpointViolation> violations) => new() { Passed = false, Violations = violations };
}

/// <summary>
/// 关卡违规项
/// </summary>
public sealed class CheckpointViolation
{
    public required string Rule { get; init; }
    public required string Message { get; init; }
    public string Severity { get; init; } = "error";
}
