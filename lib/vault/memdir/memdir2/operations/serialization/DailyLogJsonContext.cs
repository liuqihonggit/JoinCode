namespace Core.Memdir;

/// <summary>
/// 助手日志 JSON 序列化上下文 — 为 DailyLogFile/DailyLogEntry 提供 AOT 友好的序列化支持
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DailyLogFile))]
[JsonSerializable(typeof(DailyLogEntry))]
[JsonSerializable(typeof(List<DailyLogEntry>))]
public partial class DailyLogJsonContext : JsonSerializerContext;
