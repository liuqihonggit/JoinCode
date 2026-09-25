namespace JoinCode.Abstractions.Constants;

/// <summary>
/// CLI 输出格式枚举 — --output-format/--format 参数的合法值集合
/// 适用范围: jcc --format text|json|ndjson
///
/// 使用示例:
/// - FromValue("text")   → OutputFormat.Text
/// - FromValue("JSON")   → OutputFormat.Json (OrdinalIgnoreCase)
/// - OutputFormat.Ndjson.ToValue() → "ndjson"
/// </summary>
public enum OutputFormat {
    /// <summary>纯文本输出（默认，带彩色）</summary>
    [EnumValue("text")] Text,

    /// <summary>结构化 JSON 输出</summary>
    [EnumValue("json")] Json,

    /// <summary>NDJSON 流式输出（每行一个 JSON 对象）</summary>
    [EnumValue("ndjson")] Ndjson,
}
