namespace Core.Agents.Coordinator;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(MailboxMessage))]
[JsonSerializable(typeof(MailboxReadCursor))]
[JsonSerializable(typeof(MailboxSendRequest))]
[JsonSerializable(typeof(List<MailboxMessage>))]
public sealed partial class MailboxJsonContext : JsonSerializerContext;
