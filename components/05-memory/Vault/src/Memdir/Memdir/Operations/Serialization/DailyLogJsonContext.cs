namespace Core.Memdir;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DailyLogFile))]
[JsonSerializable(typeof(DailyLogEntry))]
[JsonSerializable(typeof(List<DailyLogEntry>))]
public partial class DailyLogJsonContext : JsonSerializerContext;
