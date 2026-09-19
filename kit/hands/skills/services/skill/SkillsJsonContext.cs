
namespace Core.Skills;

/// <summary>
/// 技能 JSON 序列化上下文 — 为技能相关类型生成源码化 JSON 序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SkillDefinition))]
[JsonSerializable(typeof(Discovery.DiscoveredSkill))]
[JsonSerializable(typeof(Discovery.SkillValidationResult))]
[JsonSerializable(typeof(Discovery.SkillDiscoveryOptions))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(List<Discovery.DiscoveredSkill>))]
[JsonSerializable(typeof(List<string>))]
public partial class SkillsJsonContext : JsonSerializerContext;