namespace Core.Scheduling.Tasks;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(TeammateIdleNotification))]
[JsonSerializable(typeof(TeammateShutdownRequest))]
public sealed partial class TeammateMessageJsonContext : JsonSerializerContext;
