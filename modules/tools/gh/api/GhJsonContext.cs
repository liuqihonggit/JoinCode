namespace JoinCode.Gh;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, UseStringEnumConverter = false)]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(List<ToolContent>))]
[JsonSerializable(typeof(string))]
internal sealed partial class GhJsonContext : JsonSerializerContext {
    private static readonly Lazy<GhJsonContext> s_safe = new(() => new GhJsonContext(
        new JsonSerializerOptions(Default!.Options) {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = Default!
        }));

    /// <summary>带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符</summary>
    public static GhJsonContext Safe => s_safe.Value;

    /// <summary>
    /// 把 UnsafeRelaxedJsonEscaping 转义的 HTML 敏感字符 \uXXXX 还原为原始字符。
    /// CLI 输出不需要 XSS 防护，+ &lt; &gt; &amp; ' / 应直接输出。
    /// </summary>
    public static string UnescapeHtmlChars(string json) => json
        .Replace("\\u002B", "+")
        .Replace("\\u003C", "<")
        .Replace("\\u003E", ">")
        .Replace("\\u0026", "&")
        .Replace("\\u0027", "'")
        .Replace("\\u002F", "/");
}
