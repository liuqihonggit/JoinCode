namespace Core.Context;

/// <summary>
/// 上下文层 JSON 序列化上下文，启用缩进、宽松解析和驼峰命名
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ContextLayer))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(int))]
public partial class ContextJsonContext : JsonSerializerContext;

/// <summary>
/// 上下文层默认 JSON 序列化上下文，不缩进、宽松解析、驼峰命名
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ContextLayer))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(int))]
public partial class ContextDefaultJsonContext : JsonSerializerContext;

/// <summary>
/// 聊天服务 JSON 序列化上下文，覆盖 TokenUsage 与字典类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class ChatServiceJsonContext : JsonSerializerContext;
