namespace Core.Agents.Coordinator;

/// <summary>
/// 邮箱 JSON 源码生成上下文 — 为 MailboxMessage、MailboxReadCursor、MailboxSendRequest 等类型生成 AOT 兼容的序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MailboxMessage))]
[JsonSerializable(typeof(MailboxReadCursor))]
[JsonSerializable(typeof(MailboxSendRequest))]
[JsonSerializable(typeof(List<MailboxMessage>))]
public sealed partial class MailboxJsonContext : JsonSerializerContext;
