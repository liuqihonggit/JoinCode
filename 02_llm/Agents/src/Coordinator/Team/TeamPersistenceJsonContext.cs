namespace Core.Agents.Coordinator;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeamStateData))]
[JsonSerializable(typeof(List<TeamInfo>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, List<TeamMessage>>))]
[JsonSerializable(typeof(Dictionary<string, List<TeamMemberInfo>>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class TeamPersistenceJsonContext : JsonSerializerContext;
