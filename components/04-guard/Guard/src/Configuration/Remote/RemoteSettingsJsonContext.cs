
namespace Core.Configuration.Remote;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ManagedSetting))]
[JsonSerializable(typeof(List<ManagedSetting>))]
[JsonSerializable(typeof(RemoteSettingsResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class RemoteSettingsJsonContext : JsonSerializerContext;

public sealed class RemoteSettingsResponse
{
    public List<ManagedSetting>? Settings { get; set; }
    public DateTime? FetchedAt { get; set; }
}
