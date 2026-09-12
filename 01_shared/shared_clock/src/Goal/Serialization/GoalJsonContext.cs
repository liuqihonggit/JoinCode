
namespace Core.Goal;

/// <summary>
/// 目标相关 JSON 序列化上下文 — NativeAOT 兼容
/// </summary>
[JsonSerializable(typeof(GoalEvaluationJson))]
[JsonSerializable(typeof(NegReviewOutputJson))]
[JsonSerializable(typeof(FixNegOutputJson))]
[JsonSerializable(typeof(DecompositionAnalysisJson))]
[JsonSerializable(typeof(GradingAnalysisJson))]
[JsonSerializable(typeof(JoinCode.Abstractions.Models.Goal.GoalState))]
[JsonSerializable(typeof(JoinCode.Abstractions.Models.Goal.GoalEvaluationResult))]
[JsonSerializable(typeof(ApiMessageDocument))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
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

/// <summary>
/// 任务分解分析 LLM 输出的 JSON 格式
/// </summary>
public sealed class DecompositionAnalysisJson
{
    [JsonPropertyName("isDecomposable")]
    public bool IsDecomposable { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("complexity")]
    public string Complexity { get; set; } = "medium";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "A";

    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;

    [JsonPropertyName("subTasks")]
    public List<SubTaskDefinitionJson> SubTasks { get; set; } = [];
}

/// <summary>
/// 子任务定义 JSON — LLM 输出
/// </summary>
public sealed class SubTaskDefinitionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = [];

    [JsonPropertyName("ownedFiles")]
    public List<string> OwnedFiles { get; set; } = [];

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("variant")]
    public string Variant { get; set; } = "code";
}

/// <summary>
/// 评分分析 LLM 输出的 JSON 格式
/// </summary>
public sealed class GradingAnalysisJson
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("criteria")]
    public List<GradingCriterionJson> Criteria { get; set; } = [];
}

/// <summary>
/// 评分维度 JSON — LLM 输出
/// </summary>
public sealed class GradingCriterionJson
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = string.Empty;
}
