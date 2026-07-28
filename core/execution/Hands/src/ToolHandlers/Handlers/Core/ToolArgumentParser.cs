

namespace Tools;

/// <summary>
/// 工具参数解析器 - 解析命令行参数为工具调用参数
/// </summary>
public sealed class ToolArgumentParser
{
    public ToolArgumentParser()
    {
    }

    /// <summary>
    /// 解析参数字符串为字典
    /// </summary>
    public Dictionary<string, JsonElement> Parse(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return new Dictionary<string, JsonElement>();
        }

        // 尝试解析为JSON对象
        try
        {
            var json = JsonSerializer.Deserialize(arguments, ToolsJsonContext.Default.DictionaryStringJsonElement);
            if (json != null)
            {
                return json;
            }
        }
        catch (JsonException ex)
        {
            // 不是有效的JSON，继续尝试其他格式
            System.Diagnostics.Trace.WriteLine($"JSON解析失败，将尝试键值对格式: {ex.Message}");
        }

        // 解析键值对格式: key=value key2=value2
        return ParseKeyValuePairs(arguments);
    }

    /// <summary>
    /// 解析键值对格式参数
    /// </summary>
    private Dictionary<string, JsonElement> ParseKeyValuePairs(string arguments)
    {
        var result = new Dictionary<string, JsonElement>();
        var pairs = SplitArguments(arguments);

        foreach (var pair in pairs)
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = pair[..separatorIndex].Trim();
                var value = pair[(separatorIndex + 1)..].Trim();

                // 尝试解析为JSON值
                var jsonElement = ParseValue(value);
                result[key] = jsonElement;
            }
        }

        return result;
    }

    /// <summary>
    /// 分割参数字符串
    /// </summary>
    private List<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (int i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];

            if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                quoteChar = '\0';
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// 解析值为JsonElement
    /// </summary>
    private JsonElement ParseValue(string value)
    {
        // 去除引号
        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
        }

        // 尝试解析为布尔值
        if (bool.TryParse(value, out var boolValue))
        {
            return JsonSerializer.SerializeToElement(boolValue, ToolsJsonContext.Default.Boolean);
        }

        // 尝试解析为整数
        if (int.TryParse(value, out var intValue))
        {
            return JsonSerializer.SerializeToElement(intValue, ToolsJsonContext.Default.Int32);
        }

        // 尝试解析为长整数
        if (long.TryParse(value, out var longValue))
        {
            return JsonSerializer.SerializeToElement(longValue, ToolsJsonContext.Default.Int64);
        }

        // 尝试解析为浮点数
        if (double.TryParse(value, out var doubleValue))
        {
            return JsonSerializer.SerializeToElement(doubleValue, ToolsJsonContext.Default.Double);
        }

        // 默认为字符串
        return JsonSerializer.SerializeToElement(value, ToolsJsonContext.Default.String);
    }

    /// <summary>
    /// 根据模式验证参数
    /// </summary>
    public ValidationResult Validate(Dictionary<string, JsonElement> arguments, ToolSchema schema)
    {
        var errors = new List<string>();

        // 检查必需参数
        if (schema.Required != null)
        {
            foreach (var requiredParam in schema.Required)
            {
                if (!arguments.ContainsKey(requiredParam))
                {
                    errors.Add($"Missing required parameter: {requiredParam}");
                }
            }
        }

        // 检查参数类型
        foreach (var (key, value) in arguments)
        {
            if (schema.Properties.TryGetValue(key, out var property))
            {
                if (!ValidateType(value, property.Type))
                {
                    errors.Add($"Parameter '{key}' has invalid type. Expected: {property.Type}");
                }
            }
            else
            {
                errors.Add($"Unknown parameter: {key}");
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// 验证JSON元素类型
    /// </summary>
    private bool ValidateType(JsonElement element, string expectedType)
    {
        return expectedType.ToLowerInvariant() switch
        {
            "string" => element.ValueKind == JsonValueKind.String,
            "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
            "number" => element.ValueKind == JsonValueKind.Number,
            "boolean" => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False,
            "array" => element.ValueKind == JsonValueKind.Array,
            "object" => element.ValueKind == JsonValueKind.Object,
            _ => true
        };
    }

    /// <summary>
    /// 从命令行参数构建工具调用请求
    /// </summary>
    public ToolCallRequest BuildRequest(string toolName, string[] args)
    {
        var arguments = args.Length > 0
            ? Parse(string.Join(" ", args))
            : new Dictionary<string, JsonElement>();

        return new ToolCallRequest
        {
            ToolName = toolName,
            Arguments = arguments
        };
    }
}

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = new List<string>();
}
