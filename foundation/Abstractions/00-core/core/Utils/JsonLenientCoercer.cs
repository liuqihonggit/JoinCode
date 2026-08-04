namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 单字段精确的 JSON 类型转换失败报告。
/// 用于纵深防御最深层：当某个字段无法被宽容转换时，记录"哪个字段崩坏了"，
/// 供上层报告给 LLM 以便下一轮自我修正。
/// </summary>
public sealed class JsonCoercionIssue
{
    public required string PropertyPath { get; init; }
    public required string ExpectedType { get; init; }
    public required string ActualValueKind { get; init; }
    public required string Reason { get; init; }

    public override string ToString()
    {
        return $"字段 '{PropertyPath}': 期望 {ExpectedType}, 实际 {ActualValueKind} ({Reason})";
    }
}

/// <summary>
/// LlmJsonHelper 统一门控返回的纵深防御报告。
/// 包含：语法修复提示（RepairHint）+ 类型转换单字段问题（CoercionIssues），
/// 消费方可将本报告追加到提示词回喂给 LLM，实现精确的自我纠正。
/// </summary>
public sealed class JsonLeniencyReport
{
    /// <summary>语法层修复提示（尾随逗号/未加引号键/单引号/截断等），无修复时为 null</summary>
    public string? RepairHint { get; init; }

    /// <summary>成功反序列化（可能含降级值）</summary>
    public bool Deserialized { get; init; }

    /// <summary>单字段类型转换失败明细，供报告给 LLM</summary>
    public IReadOnlyList<JsonCoercionIssue> CoercionIssues { get; init; } = [];

    /// <summary>
    /// 拼接为一条面向 LLM 的可读错误摘要
    /// </summary>
    public string FormatForLlm()
    {
        var parts = new List<string>();
        if (RepairHint is not null)
            parts.Add($"语法修复: {RepairHint}");
        if (CoercionIssues.Count > 0)
        {
            var field = string.Join("; ", CoercionIssues.Select(i => i.ToString()));
            parts.Add($"字段类型不合格: {field}");
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : string.Empty;
    }
}

/// <summary>
/// LLM 结构化输出 JSON 纵深防御第三层 — 基于目标 CLR 元数据（JsonTypeInfo）的字段类型强制转换。
/// 不依赖反射（NativeAOT 安全）：直接读取 source-generator 生成的 JsonTypeInfo.Properties。
/// 宽容策略：number↔bool、number→string、bool→string、string→number、string Trim。
/// </summary>
public static class JsonLenientCoercer
{
    /// <summary>
    /// 尝试对顶层对象 DTO 的标量字段做类型强制转换并输出修复后的 JSON。
    /// 可转换字段被纠正；不可转换字段被降级为默认值并记录为 JsonCoercionIssue（不阻断整条解析）。
    /// 返回 true 表示产生了至少一处转换。
    /// </summary>
    public static bool TryCoerceObjectJson<T>(
        string json,
        JsonTypeInfo<T> jsonTypeInfo,
        out string? coercedJson,
        out List<JsonCoercionIssue> issues)
        where T : class
    {
        coercedJson = null;
        issues = [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return false;

        var properties = jsonTypeInfo.Properties;
        var targetBySerialized = new Dictionary<string, JsonPropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in properties)
        {
            if (!string.IsNullOrEmpty(p.Name))
                targetBySerialized[p.Name] = p;
        }

        var rebuilt = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var srcProp in doc.RootElement.EnumerateObject())
        {
            if (targetBySerialized.TryGetValue(srcProp.Name, out var targetProp))
            {
                var action = CoerceValue(srcProp.Name, targetProp, srcProp.Value);
                if (action.Issue is not null)
                    issues.Add(action.Issue);
                if (action.Changed)
                    changed = true;
                rebuilt[srcProp.Name] = action.Result;
            }
            else
            {
                rebuilt[srcProp.Name] = srcProp.Value.Clone();
            }
        }

        if (!changed)
            return false;

        coercedJson = BuildJsonObjectString(rebuilt);
        return true;
    }

    /// <summary>
    /// 将重建的 KV 集合序列化为 JSON 对象字符串，属性名做 JSON 转义。
    /// 不使用 JsonObject/JsonNode，避免引入额外命名空间依赖。
    /// </summary>
    private static string BuildJsonObjectString(IEnumerable<KeyValuePair<string, JsonElement>> items)
    {
        var sb = new StringBuilder(128);
        sb.Append('{');
        var first = true;
        foreach (var (key, value) in items)
        {
            if (!first)
                sb.Append(',');
            first = false;

            sb.Append('"').Append(EscapeJsonKey(key)).Append('"').Append(':').Append(value.GetRawText());
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// JSON 对象键逃逸：仅处理双引号、反斜杠与控制字符（码点小于 0x20）。
    /// 用于重建 JSON 对象字符串时保证键名合法。
    /// </summary>
    private static string EscapeJsonKey(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static (JsonElement Result, bool Changed, JsonCoercionIssue? Issue) CoerceValue(
        string name, JsonPropertyInfo prop, JsonElement value)
    {
        var declaredType = prop.PropertyType;
        var underlying = Nullable.GetUnderlyingType(declaredType);
        var effective = underlying ?? declaredType;
        var kind = value.ValueKind;

        // 字符串 Trim（业务校验层宽容）
        if (effective == typeof(string) && kind == JsonValueKind.String)
        {
            var s = value.GetString();
            if (s is null)
                return (value, false, null);
            var trimmed = s.Trim();
            if (!string.Equals(trimmed, s, StringComparison.Ordinal))
                return (JsonElementHelper.FromString(trimmed), true, null);
            return (value, false, null);
        }

        if (effective == typeof(bool) || effective == typeof(bool?))
            return CoerceToBool(name, effective, value, kind);

        if (effective == typeof(string))
            return CoerceToString(name, value, kind);

        if (IsNumericType(effective))
            return CoerceToNumber(name, effective, value, kind);

        // enum / 集合 / 嵌套对象等：System.Text.Json 原生已宽容（枚举忽略大小写、未知字段忽略），无需额外转换
        return (value, false, null);
    }

    private static (JsonElement Result, bool Changed, JsonCoercionIssue? Issue) CoerceToBool(
        string name, Type effective, JsonElement value, JsonValueKind kind)
    {
        switch (kind)
        {
            case JsonValueKind.Number:
                var intVal = value.TryGetInt64(out var l) ? l : (long)value.GetDouble();
                return (JsonElementHelper.FromBoolean(intVal != 0), true, null);

            case JsonValueKind.String:
                var s = value.GetString();
                if (s is null)
                    return (JsonElementHelper.FromBoolean(false), true, null);
                var trimmed = s.Trim();
                switch (trimmed.ToLowerInvariant())
                {
                    case "true":
                    case "1":
                    case "yes":
                    case "y":
                    case "on":
                        return (JsonElementHelper.FromBoolean(true), true, null);
                    case "false":
                    case "0":
                    case "no":
                    case "n":
                    case "off":
                    case "":
                        return (JsonElementHelper.FromBoolean(false), true, null);
                    default:
                        return (JsonElementHelper.FromBoolean(false), true,
                            new JsonCoercionIssue
                            {
                                PropertyPath = name,
                                ExpectedType = effective.Name,
                                ActualValueKind = JsonValueKind.String.ToString(),
                                Reason = $"无法将字符串 '{s}' 转换为布尔值"
                            });
                }

            case JsonValueKind.Null:
                // null → 降级为 default(bool)=false
                return (JsonElementHelper.FromBoolean(false), true, null);

            default:
                return (value, false, null);
        }
    }

    private static (JsonElement Result, bool Changed, JsonCoercionIssue? Issue) CoerceToString(
        string name, JsonElement value, JsonValueKind kind)
    {
        switch (kind)
        {
            case JsonValueKind.Number:
                // 超大数字保留原始文本（防 JS 精度丢失 / 雪花ID）
                var raw = value.TryGetInt64(out var longVal)
                    ? longVal.ToString(CultureInfo.InvariantCulture)
                    : value.GetRawText();
                return (JsonElementHelper.FromString(raw), true, null);

            case JsonValueKind.True:
                return (JsonElementHelper.FromString("true"), true, null);

            case JsonValueKind.False:
                return (JsonElementHelper.FromString("false"), true, null);

            case JsonValueKind.Null:
                // null 字符串 → 空串（等价缺省），不阻断解析
                return (JsonElementHelper.FromString(""), true, null);

            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return (JsonElementHelper.FromString(""), true,
                    new JsonCoercionIssue
                    {
                        PropertyPath = name,
                        ExpectedType = "String",
                        ActualValueKind = kind.ToString(),
                        Reason = "对象/数组无法转换为字符串，已降级为空串"
                    });

            default:
                return (value, false, null);
        }
    }

    private static (JsonElement Result, bool Changed, JsonCoercionIssue? Issue) CoerceToNumber(
        string name, Type effective, JsonElement value, JsonValueKind kind)
    {
        if (kind != JsonValueKind.String)
            return (value, false, null);

        var s = value.GetString();
        if (s is null)
        {
            var zero = IsIntegral(effective)
                ? JsonElementHelper.FromInt64(0)
                : JsonElementHelper.FromDouble(0);
            return (zero, true, null);
        }

        var trimmed = s.Trim();
        var numStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        if (IsIntegral(effective)
            && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
        {
            return (JsonElementHelper.FromInt64(longVal), true, null);
        }

        if (double.TryParse(trimmed, numStyle, CultureInfo.InvariantCulture, out var doubleVal))
        {
            return (JsonElementHelper.FromDouble(doubleVal), true, null);
        }

        return (value, false, new JsonCoercionIssue
        {
            PropertyPath = name,
            ExpectedType = effective.Name,
            ActualValueKind = JsonValueKind.String.ToString(),
            Reason = $"无法将字符串 '{s}' 转换为 {effective.Name}"
        });
    }

    private static bool IsNumericType(Type t)
    {
        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
    }

    private static bool IsIntegral(Type t)
    {
        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
    }
}