
namespace Core.Goal;

/// <summary>
/// 目标相关 JSON 序列化上下文 — NativeAOT 兼容
/// </summary>
[JsonSerializable(typeof(GoalEvaluationJson))]
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
