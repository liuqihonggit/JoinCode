
namespace Core.Goal;

/// <summary>
/// 目标相关 JSON 序列化上下文 — NativeAOT 兼容
/// </summary>
[JsonSerializable(typeof(GoalEvaluationJson))]
[JsonSerializable(typeof(NegReviewOutputJson))]
[JsonSerializable(typeof(FixNegOutputJson))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
public partial class GoalJsonContext : JsonSerializerContext;

/// <summary>
/// 评估器返回的 JSON 格式
/// </summary>
public sealed class GoalEvaluationJson
{
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 负向评价节点输出的 JSON 格式
/// </summary>
public sealed class NegReviewOutputJson
{
    [JsonPropertyName("negativeReviewCount")]
    public int NegativeReviewCount { get; set; }

    [JsonPropertyName("route")]
    public string Route { get; set; } = "NEG_STOP";

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("items")]
    public List<NegReviewItemJson> Items { get; set; } = [];

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 负向评价条目
/// </summary>
public sealed class NegReviewItemJson
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";
}

/// <summary>
/// 修复负评节点输出的 JSON 格式
/// </summary>
public sealed class FixNegOutputJson
{
    [JsonPropertyName("route")]
    public string Route { get; set; } = "NEG_STOP";

    [JsonPropertyName("fixedCount")]
    public int FixedCount { get; set; }

    [JsonPropertyName("remainingCount")]
    public int RemainingCount { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}
