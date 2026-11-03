namespace JoinCode.Gh;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, UseStringEnumConverter = false)]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(List<ToolContent>))]
[JsonSerializable(typeof(string))]
internal sealed partial class GhJsonContext : JsonSerializerContext {
    private static readonly Lazy<GhJsonContext> s_safe = new(() => new GhJsonContext(
        new JsonSerializerOptions(Default!.Options) {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            TypeInfoResolver = Default!
        }));

    /// <summary>带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符</summary>
    public static GhJsonContext Safe => s_safe.Value;
}
