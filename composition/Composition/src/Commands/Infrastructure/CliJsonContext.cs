
namespace JoinCode.ChatCommands;

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
