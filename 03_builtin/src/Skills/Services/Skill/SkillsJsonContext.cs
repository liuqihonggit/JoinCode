
namespace Core.Skills;

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
