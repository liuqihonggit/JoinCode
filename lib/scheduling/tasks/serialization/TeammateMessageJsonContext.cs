namespace Core.Scheduling.Tasks;

/// <summary>
/// Teammate 消息 JSON 序列化上下文 — 覆盖空闲通知与关闭请求消息类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeammateIdleNotification))]
[JsonSerializable(typeof(TeammateShutdownRequest))]
public sealed partial class TeammateMessageJsonContext : JsonSerializerContext;