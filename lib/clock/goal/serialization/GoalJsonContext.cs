
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
    /// <summary>目标是否已完成</summary>
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    /// <summary>评估原因</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 负向评价节点输出的 JSON 格式
/// </summary>
public sealed class NegReviewOutputJson
{
    /// <summary>负评数量</summary>
    [JsonPropertyName("negativeReviewCount")]
    public int NegativeReviewCount { get; set; }

    /// <summary>路由决策</summary>
    [JsonPropertyName("route")]
    public string Route { get; set; } = "NEG_STOP";

    /// <summary>任务 ID</summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>负评条目列表</summary>
    [JsonPropertyName("items")]
    public List<NegReviewItemJson> Items { get; set; } = [];

    /// <summary>汇总描述</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 负向评价条目
/// </summary>
public sealed class NegReviewItemJson
{
    /// <summary>负评类别</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>负评描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>严重程度</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";
}

/// <summary>
/// 修复负评节点输出的 JSON 格式
/// </summary>
public sealed class FixNegOutputJson
{
    /// <summary>路由决策</summary>
    [JsonPropertyName("route")]
    public string Route { get; set; } = "NEG_STOP";

    /// <summary>已修复数量</summary>
    [JsonPropertyName("fixedCount")]
    public int FixedCount { get; set; }

    /// <summary>剩余数量</summary>
    [JsonPropertyName("remainingCount")]
    public int RemainingCount { get; set; }

    /// <summary>汇总描述</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 任务分解分析 LLM 输出的 JSON 格式
/// </summary>
public sealed class DecompositionAnalysisJson
{
    /// <summary>是否可分解</summary>
    [JsonPropertyName("isDecomposable")]
    public bool IsDecomposable { get; set; }

    /// <summary>分析原因</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>复杂度级别</summary>
    [JsonPropertyName("complexity")]
    public string Complexity { get; set; } = "medium";

    /// <summary>执行模式</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "A";

    /// <summary>选择理由</summary>
    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;

    /// <summary>子任务列表</summary>
    [JsonPropertyName("subTasks")]
    public List<SubTaskDefinitionJson> SubTasks { get; set; } = [];
}

/// <summary>
/// 子任务定义 JSON — LLM 输出
/// </summary>
public sealed class SubTaskDefinitionJson
{
    /// <summary>子任务 ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>子任务标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>子任务描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>依赖的子任务 ID 列表</summary>
    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = [];

    /// <summary>拥有的文件列表</summary>
    [JsonPropertyName("ownedFiles")]
    public List<string> OwnedFiles { get; set; } = [];

    /// <summary>优先级</summary>
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    /// <summary>执行变体</summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = "code";
}

/// <summary>
/// 评分分析 LLM 输出的 JSON 格式
/// </summary>
public sealed class GradingAnalysisJson
{
    /// <summary>评分原因</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>评分维度列表</summary>
    [JsonPropertyName("criteria")]
    public List<GradingCriterionJson> Criteria { get; set; } = [];
}

/// <summary>
/// 评分维度 JSON — LLM 输出
/// </summary>
public sealed class GradingCriterionJson
{
    /// <summary>维度名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>维度得分（0.0-1.0）</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>维度反馈</summary>
    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = string.Empty;
}
