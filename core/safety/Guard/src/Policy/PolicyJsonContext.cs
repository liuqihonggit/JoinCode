
namespace Core.Policy;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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

public sealed class PolicyFetchResponse
{
    public List<PolicyRule>? Rules { get; set; }
    public DateTime? FetchedAt { get; set; }
}
