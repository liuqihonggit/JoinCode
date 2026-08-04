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
/// 宽容策略：number↔bool、number→string、bool→string、string→number、string Trim、
/// 数值越界截断、未定义枚举值降级为默认值。
/// </summary>
public static class JsonLenientCoercer
{
    private readonly record struct CoerceAction(JsonElement Result, bool Changed, bool Drop, JsonCoercionIssue? Issue);

    /// <summary>
    /// 尝试对顶层对象 DTO 的标量字段做类型强制转换并输出修复后的 JSON。
    /// 可转换字段被纠正；不可转换字段被降级为默认值（Drop）并记录为 JsonCoercionIssue（不阻断整条解析）。
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

                if (action.Drop)
                {
                    // 字段降级：从输出中移除，反序列化采用 CLR 默认值
                    changed = true;
                    continue;
                }

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

    private static CoerceAction CoerceValue(string name, JsonPropertyInfo prop, JsonElement value)
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
                return new CoerceAction(value, false, false, null);
            var trimmed = s.Trim();
            if (!string.Equals(trimmed, s, StringComparison.Ordinal))
                return new CoerceAction(JsonElementHelper.FromString(trimmed), true, false, null);
            return new CoerceAction(value, false, false, null);
        }

        if (effective == typeof(bool) || effective == typeof(bool?))
            return CoerceToBool(name, effective, value, kind);

        if (effective == typeof(string))
            return CoerceToString(name, value, kind);

        if (IsNumericType(effective))
            return CoerceToNumber(name, effective, value, kind);

        if (effective.IsEnum)
            return CoerceToEnum(name, effective, value, kind);

        // 集合 / 嵌套对象等：System.Text.Json 原生已宽容（未知字段忽略），无需额外转换
        return new CoerceAction(value, false, false, null);
    }

    private static CoerceAction CoerceToBool(string name, Type effective, JsonElement value, JsonValueKind kind)
    {
        switch (kind)
        {
            case JsonValueKind.Number:
                var intVal = value.TryGetInt64(out var l) ? l : (long)value.GetDouble();
                return new CoerceAction(JsonElementHelper.FromBoolean(intVal != 0), true, false, null);

            case JsonValueKind.String:
                var s = value.GetString();
                if (s is null)
                    return new CoerceAction(JsonElementHelper.FromBoolean(false), true, false, null);
                var trimmed = s.Trim();
                switch (trimmed.ToLowerInvariant())
                {
                    case "true":
                    case "1":
                    case "yes":
                    case "y":
                    case "on":
                        return new CoerceAction(JsonElementHelper.FromBoolean(true), true, false, null);
                    case "false":
                    case "0":
                    case "no":
                    case "n":
                    case "off":
                    case "":
                        return new CoerceAction(JsonElementHelper.FromBoolean(false), true, false, null);
                    default:
                        return new CoerceAction(default, true, true,
                            new JsonCoercionIssue
                            {
                                PropertyPath = name,
                                ExpectedType = effective.Name,
                                ActualValueKind = JsonValueKind.String.ToString(),
                                Reason = $"无法将字符串 '{s}' 转换为布尔值，已使用默认值 false"
                            });
                }

            case JsonValueKind.Null:
                return new CoerceAction(JsonElementHelper.FromBoolean(false), true, false, null);

            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return new CoerceAction(default, true, true,
                    new JsonCoercionIssue
                    {
                        PropertyPath = name,
                        ExpectedType = effective.Name,
                        ActualValueKind = kind.ToString(),
                        Reason = $"{kind} 无法转换为布尔值，已使用默认值 false"
                    });

            default:
                return new CoerceAction(value, false, false, null);
        }
    }

    private static CoerceAction CoerceToString(string name, JsonElement value, JsonValueKind kind)
    {
        switch (kind)
        {
            case JsonValueKind.Number:
                // 超大数字保留原始文本（防 JS 精度丢失 / 雪花ID）
                var raw = value.TryGetInt64(out var longVal)
                    ? longVal.ToString(CultureInfo.InvariantCulture)
                    : value.GetRawText();
                return new CoerceAction(JsonElementHelper.FromString(raw), true, false, null);

            case JsonValueKind.True:
                return new CoerceAction(JsonElementHelper.FromString("true"), true, false, null);

            case JsonValueKind.False:
                return new CoerceAction(JsonElementHelper.FromString("false"), true, false, null);

            case JsonValueKind.Null:
                // null 字符串 → 空串（等价缺省），不阻断解析
                return new CoerceAction(JsonElementHelper.FromString(""), true, false, null);

            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return new CoerceAction(JsonElementHelper.FromString(""), true, false,
                    new JsonCoercionIssue
                    {
                        PropertyPath = name,
                        ExpectedType = "String",
                        ActualValueKind = kind.ToString(),
                        Reason = "对象/数组无法转换为字符串，已降级为空串"
                    });

            default:
                return new CoerceAction(value, false, false, null);
        }
    }

    private static CoerceAction CoerceToNumber(string name, Type effective, JsonElement value, JsonValueKind kind)
    {
        if (IsIntegral(effective) && kind == JsonValueKind.Number)
        {
            // 数值越界截断：JSON 数字超出目标整型范围时钳制到 [Min, Max]
            if (value.TryGetInt64(out var l))
                return ClampIntegral(name, effective, l);

            if (value.TryGetUInt64(out var ul) && ul <= long.MaxValue)
                return ClampIntegral(name, effective, (long)ul);

            var d = value.GetDouble();
            if (d >= long.MinValue && d <= long.MaxValue && d == Math.Floor(d))
                return ClampIntegral(name, effective, (long)d);

            return new CoerceAction(default, true, true,
                new JsonCoercionIssue
                {
                    PropertyPath = name,
                    ExpectedType = effective.Name,
                    ActualValueKind = JsonValueKind.Number.ToString(),
                    Reason = "数值超出可表示范围，已使用默认值"
                });
        }

        if (kind is JsonValueKind.Object or JsonValueKind.Array)
            return new CoerceAction(default, true, true,
                new JsonCoercionIssue
                {
                    PropertyPath = name,
                    ExpectedType = effective.Name,
                    ActualValueKind = kind.ToString(),
                    Reason = $"{kind} 无法转换为数值，已使用默认值"
                });

        if (kind != JsonValueKind.String)
            return new CoerceAction(value, false, false, null);

        var s = value.GetString();
        if (s is null)
        {
            var zero = IsIntegral(effective)
                ? JsonElementHelper.FromInt64(0)
                : JsonElementHelper.FromDouble(0);
            return new CoerceAction(zero, true, false, null);
        }

        var trimmed = s.Trim();
        var numStyle = NumberStyles.Float | NumberStyles.AllowThousands;

        if (IsIntegral(effective)
            && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
        {
            return ClampIntegral(name, effective, longVal);
        }

        if (double.TryParse(trimmed, numStyle, CultureInfo.InvariantCulture, out var doubleVal))
        {
            return new CoerceAction(JsonElementHelper.FromDouble(doubleVal), true, false, null);
        }

        return new CoerceAction(value, false, false, new JsonCoercionIssue
        {
            PropertyPath = name,
            ExpectedType = effective.Name,
            ActualValueKind = JsonValueKind.String.ToString(),
            Reason = $"无法将字符串 '{s}' 转换为 {effective.Name}"
        });
    }

    private static CoerceAction ClampIntegral(string name, Type effective, long value)
    {
        var (min, max) = IntegralRange(effective);
        if (value < min || value > max)
        {
            var clamped = Math.Clamp(value, min, max);
            return new CoerceAction(JsonElementHelper.FromInt64(clamped), true, false,
                new JsonCoercionIssue
                {
                    PropertyPath = name,
                    ExpectedType = effective.Name,
                    ActualValueKind = JsonValueKind.Number.ToString(),
                    Reason = $"数值 {value} 超出 {effective.Name} 范围，已截断为 {clamped}"
                });
        }

        // 已在本类型范围内：转为整型字面量（字符串入参时也需落成数字）
        return new CoerceAction(JsonElementHelper.FromInt64(value), true, false, null);
    }

    private static CoerceAction CoerceToEnum(string name, Type effective, JsonElement value, JsonValueKind kind)
    {
        if (kind == JsonValueKind.String)
        {
            var s = value.GetString();
            if (s is not null && Enum.TryParse(effective, s, true, out var parsed))
            {
                // System.Text.Json 默认枚举转换器只认数字；将合法字符串枚举转为底层数值
                var underlying = Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
                return new CoerceAction(JsonElementHelper.FromInt64(underlying), true, false, null);
            }

            // 未定义枚举值 → 降级为默认值（省略字段），并精确报错
            return new CoerceAction(default, true, true,
                new JsonCoercionIssue
                {
                    PropertyPath = name,
                    ExpectedType = effective.Name,
                    ActualValueKind = JsonValueKind.String.ToString(),
                    Reason = $"未定义的枚举值 '{s}'，已使用默认值"
                });
        }

        // Number：System.Text.Json 原生支持按底层值反序列化
        return new CoerceAction(value, false, false, null);
    }

    private static (long Min, long Max) IntegralRange(Type t)
    {
        return t switch
        {
            _ when t == typeof(sbyte) => (sbyte.MinValue, sbyte.MaxValue),
            _ when t == typeof(byte) => (byte.MinValue, byte.MaxValue),
            _ when t == typeof(short) => (short.MinValue, short.MaxValue),
            _ when t == typeof(ushort) => (ushort.MinValue, ushort.MaxValue),
            _ when t == typeof(int) => (int.MinValue, int.MaxValue),
            _ when t == typeof(uint) => (uint.MinValue, uint.MaxValue),
            _ when t == typeof(long) => (long.MinValue, long.MaxValue),
            _ => (long.MinValue, long.MaxValue),
        };
    }

    private static bool IsNumericType(Type t)
    {
        return IsIntegral(t)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal);
    }

    private static bool IsIntegral(Type t)
    {
        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
    }
}