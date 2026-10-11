
namespace JoinCode.Reasoning.Agents;

/// <summary>
/// Reasoning Agent LLM 响应 JSON 序列化上下文 — AOT 源码生成，覆盖法官裁决、控方证据、辩方反驳及质疑列表类型
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JudgeVerdictsJson))]
[JsonSerializable(typeof(ProsecutorEvidenceJson))]
[JsonSerializable(typeof(DefenderCounterEvidenceJson))]
[JsonSerializable(typeof(List<string>))]
public partial class ReasoningJsonContext : JsonSerializerContext;
