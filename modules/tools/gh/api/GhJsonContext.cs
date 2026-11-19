namespace JoinCode.Gh;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, UseStringEnumConverter = false)]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ToolContent))]
[JsonSerializable(typeof(List<ToolContent>))]
[JsonSerializable(typeof(string))]
internal sealed partial class GhJsonContext : JsonSerializerContext {
    private static readonly Lazy<GhJsonContext> s_safe = new(() => {
        var defaultContext = Default ?? throw new InvalidOperationException("[GhJsonContext] 源生成上下文 Default 未初始化");
        return new GhJsonContext(
            new JsonSerializerOptions(defaultContext.Options) {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = defaultContext
            });
    });

    /// <summary>带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符</summary>
    public static GhJsonContext Safe => s_safe.Value;

    /// <summary>
    /// 把 UnsafeRelaxedJsonEscaping 转义的 HTML 敏感字符 \uXXXX 还原为原始字符。
    /// 委托 RelaxedJsonSerializer.UnescapeHtmlChars，rg/gh 统一实现。
    /// </summary>
    public static string UnescapeHtmlChars(string json) => RelaxedJsonSerializer.UnescapeHtmlChars(json);
}
