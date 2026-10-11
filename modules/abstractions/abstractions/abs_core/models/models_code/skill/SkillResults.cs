namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 技能执行结果
/// </summary>
public sealed record SkillResult {
    /// <summary>获取技能名称。</summary>
    public required string SkillName { get; init; }
    /// <summary>获取输出内容。</summary>
    public string Output { get; init; } = string.Empty;
    /// <summary>获取附加数据。</summary>
    public Dictionary<string, JsonElement>? Data { get; init; }
    /// <summary>获取一个值，指示执行是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取执行时长（毫秒）。</summary>
    public long? DurationMs { get; init; }

    /// <summary>创建执行成功结果。</summary>
    public static SkillResult SuccessResult(string skillName, string output, Dictionary<string, JsonElement>? data = null, long? durationMs = null)
        => new() {
            SkillName = skillName,
            Output = output,
            Data = data,
            Success = true,
            DurationMs = durationMs
        };

    /// <summary>创建执行失败结果。</summary>
    public static SkillResult FailureResult(string skillName, string errorMessage)
        => new() {
            SkillName = skillName,
            Output = string.Empty,
            Success = false,
            ErrorMessage = errorMessage
        };
}

public sealed class SkillExecutionResult {
    /// <summary>获取技能名称。</summary>
    public required string SkillName { get; init; }
    /// <summary>获取一个值，指示执行是否成功。</summary>
    public bool IsSuccess { get; init; }
    /// <summary>获取输出内容。</summary>
    public required string Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取各步骤执行结果列表。</summary>
    public List<StepResult> StepResults { get; init; } = new();
    /// <summary>获取执行时长。</summary>
    public TimeSpan ExecutionTime { get; init; }
}

public sealed class StepResult {
    /// <summary>获取步骤标识。</summary>
    public required string StepId { get; init; }
    /// <summary>获取一个值，指示步骤是否成功。</summary>
    public bool IsSuccess { get; init; }
    /// <summary>获取输出内容。</summary>
    public string? Output { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }
    /// <summary>获取执行时长。</summary>
    public TimeSpan ExecutionTime { get; init; }
}