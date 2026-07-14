namespace Core.Memdir;

[JsonSerializable(typeof(SessionTagData))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal sealed partial class SessionTagJsonContext : JsonSerializerContext;
