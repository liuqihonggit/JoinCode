namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 参数类型强转器 — 从 ToolCallRepairService 提取的单一职责小类
/// <para>职责: 按 schema 声明类型强转 JSON 参数值(string↔number↔boolean↔array)</para>
/// </summary>
internal static class ArgumentTypeCoercer
{
    /// <summary>
    /// 修复参数类型 — 按 schema 声明类型逐个强转参数值
    /// <para>策略: string→number/bool/array, number→string, bool→string, array→string 等</para>
    /// </summary>
    public static (Dictionary<string, JsonElement> Arguments, bool Modified, string? Hint) RepairArgumentTypes(
        Dictionary<string, JsonElement> arguments,
        ToolSchema schema)
    {
        var repairs = new List<string>();
        var repaired = new Dictionary<string, JsonElement>(arguments.Count);
        bool modified = false;

        foreach (var (key, value) in arguments)
        {
            if (!schema.Properties.TryGetValue(key, out var propSchema))
            {
                repaired[key] = value;
                continue;
            }

            var expectedType = propSchema.Type?.ToLowerInvariant();
            if (string.IsNullOrEmpty(expectedType))
            {
                repaired[key] = value;
                continue;
            }

            var (converted, wasConverted) = TryConvertType(value, expectedType);
            if (wasConverted)
            {
                repaired[key] = converted;
                repairs.Add($"'{key}' type corrected to {expectedType}");
                modified = true;
            }
            else
            {
                repaired[key] = value;
            }
        }

        if (!modified)
            return (arguments, false, null);

        return (repaired, true, string.Join("; ", repairs));
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertType(JsonElement value, string expectedType)
    {
        return expectedType switch
        {
            "string" => TryConvertToString(value),
            "integer" => TryConvertToInteger(value),
            "number" => TryConvertToNumber(value),
            "boolean" => TryConvertToBoolean(value),
            "array" => TryConvertToArray(value),
            _ => (value, false)
        };
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToString(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                var numStr = value.TryGetInt64(out var longVal) ? longVal.ToString() : value.GetDouble().ToString(CultureInfo.InvariantCulture);
                return (JsonElementHelper.FromString(numStr), true);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return (JsonElementHelper.FromString(value.GetBoolean().ToString().ToLowerInvariant()), true);

            case JsonValueKind.Array:
                if (value.GetArrayLength() == 0)
                    return (JsonElementHelper.FromString(""), true);
                if (value.GetArrayLength() == 1)
                    return (value[0].ValueKind == JsonValueKind.String ? value[0].Clone() : JsonElementHelper.FromString(value[0].GetRawText()), true);
                return (value[0].ValueKind == JsonValueKind.String ? value[0].Clone() : JsonElementHelper.FromString(value[0].GetRawText()), true);

            case JsonValueKind.Object:
                return (JsonElementHelper.FromString(value.GetRawText()), true);

            default:
                return (value, false);
        }
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToInteger(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                return (JsonElementHelper.FromInt32(intVal), true);
            if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                return (JsonElementHelper.FromInt64(longVal), true);
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var intVal))
                return (JsonElementHelper.FromInt32(intVal), false);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
                return (JsonElementHelper.FromDouble(doubleVal), true);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToBoolean(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (bool.TryParse(str, out var boolVal))
                return (JsonElementHelper.FromBoolean(boolVal), true);
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var intVal))
                return (JsonElementHelper.FromBoolean(intVal != 0), true);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (str.StartsWith('['))
            {
                try
                {
                    var arr = JsonDocument.Parse(str);
                    if (arr.RootElement.ValueKind == JsonValueKind.Array)
                        return (arr.RootElement.Clone(), true);
                }
                catch (JsonException)
                {
                    System.Diagnostics.Debug.WriteLine($"ArgumentTypeCoercer: failed to parse string as JSON array");
                }
            }
            else if (str.StartsWith('{'))
            {
                try
                {
                    var obj = JsonDocument.Parse(str);
                    if (obj.RootElement.ValueKind == JsonValueKind.Object)
                        return (JsonDocument.Parse($"[{str}]").RootElement.Clone(), true);
                }
                catch (JsonException)
                {
                    System.Diagnostics.Debug.WriteLine($"ArgumentTypeCoercer: failed to parse string as JSON object for array wrap");
                }
            }
        }

        return (value, false);
    }
}
