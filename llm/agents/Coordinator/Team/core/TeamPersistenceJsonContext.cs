namespace Core.Agents.Coordinator;

/// <summary>
/// 团队持久化 JSON 源码生成上下文 — 为 TeamStateData 及相关集合类型生成 AOT 兼容的序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeamStateData))]
[JsonSerializable(typeof(ChatRoomStateData))]
[JsonSerializable(typeof(List<TeamInfo>))]
[JsonSerializable(typeof(List<TeamMessage>))]
[JsonSerializable(typeof(List<TeamAllowedPath>))]
[JsonSerializable(typeof(List<TeamMemberInfo>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, List<TeamMessage>>))]
[JsonSerializable(typeof(Dictionary<string, List<TeamMemberInfo>>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class TeamPersistenceJsonContext : JsonSerializerContext;
