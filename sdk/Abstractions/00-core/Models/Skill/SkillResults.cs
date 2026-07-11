namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 技能执行结果
/// </summary>
public sealed record SkillResult
{
    public required string SkillName { get; init; }
    public string Output { get; init; } = string.Empty;
    public Dictionary<string, JsonElement>? Data { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public long? DurationMs { get; init; }

    public static SkillResult SuccessResult(string skillName, string output, Dictionary<string, JsonElement>? data = null, long? durationMs = null)
        => new()
        {
            SkillName = skillName,
            Output = output,
            Data = data,
            Success = true,
            DurationMs = durationMs
        };

    public static SkillResult FailureResult(string skillName, string errorMessage)
        => new()
        {
            SkillName = skillName,
            Output = string.Empty,
            Success = false,
            ErrorMessage = errorMessage
        };
}

public sealed class SkillExecutionResult
{
    public required string SkillName { get; init; }
    public bool IsSuccess { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
    public List<StepResult> StepResults { get; init; } = new();
    public TimeSpan ExecutionTime { get; init; }
}

public sealed class StepResult
{
    public required string StepId { get; init; }
    public bool IsSuccess { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan ExecutionTime { get; init; }
}
