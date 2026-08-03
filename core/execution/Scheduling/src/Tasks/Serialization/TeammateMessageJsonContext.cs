namespace Core.Scheduling.Tasks;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeammateIdleNotification))]
[JsonSerializable(typeof(TeammateShutdownRequest))]
public sealed partial class TeammateMessageJsonContext : JsonSerializerContext;
