
namespace JoinCode.ChatCommands;

/// <summary>
/// CLI JSON 序列化上下文 — 紧凑格式（无缩进），用于会话数据、代码分析报告等类型的 AOT 友好序列化
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SessionData))]
[JsonSerializable(typeof(SessionMessage))]
[JsonSerializable(typeof(SessionLiteData))]
[JsonSerializable(typeof(List<SessionMessage>))]
[JsonSerializable(typeof(List<SessionLiteData>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(CodeAnalysisReport))]
[JsonSerializable(typeof(FileTypeEntry))]
[JsonSerializable(typeof(List<FileTypeEntry>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
public partial class CliJsonContext : JsonSerializerContext;

/// <summary>
/// CLI JSON 序列化上下文 — 缩进格式（WriteIndented=true），用于人类可读的会话数据持久化
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SessionData))]
[JsonSerializable(typeof(SessionMessage))]
[JsonSerializable(typeof(SessionLiteData))]
[JsonSerializable(typeof(List<SessionMessage>))]
[JsonSerializable(typeof(List<SessionLiteData>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(CodeAnalysisReport))]
[JsonSerializable(typeof(FileTypeEntry))]
[JsonSerializable(typeof(List<FileTypeEntry>))]
public partial class CliIndentedJsonContext : JsonSerializerContext;
