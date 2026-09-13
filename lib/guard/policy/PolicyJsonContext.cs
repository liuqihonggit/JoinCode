
namespace Core.Policy;

/// <summary>
/// 策略 JSON 序列化上下文 — 为策略相关类型生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PolicyRule))]
[JsonSerializable(typeof(List<PolicyRule>))]
[JsonSerializable(typeof(PolicyEvaluationResult))]
[JsonSerializable(typeof(List<PolicyEvaluationResult>))]
[JsonSerializable(typeof(PolicyFetchResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
public partial class PolicyJsonContext : JsonSerializerContext;

/// <summary>
/// 策略拉取响应 — 包含从远端获取的策略规则列表及拉取时间戳
/// </summary>
public sealed class PolicyFetchResponse
{
    /// <summary>
    /// 策略规则列表
    /// </summary>
    public List<PolicyRule> Rules { get; set; } = [];

    /// <summary>
    /// 拉取时间戳（UTC）
    /// </summary>
    public DateTime? FetchedAt { get; set; }
}
