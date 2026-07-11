
namespace Core.Skills;

/// <summary>
/// 表达式求值器 - 支持简单算术、字符串方法和属性访问
/// </summary>
public sealed class ExpressionEvaluator
{
    /// <summary>
    /// 方法调用正则表达式
    /// </summary>
    private static readonly Regex MethodCallPattern = new(
        @"^(?<target>.+?)\.(?<method>[a-zA-Z_][a-zA-Z0-9_]*)\s*\(\s*(?<args>.*)\s*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 属性访问正则表达式
    /// </summary>
    private static readonly Regex PropertyAccessPattern = new(
        @"^(?<target>.+?)\.(?<property>[a-zA-Z_][a-zA-Z0-9_]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 算术表达式正则表达式
    /// </summary>
    private static readonly Regex ArithmeticPattern = new(
        @"^(?<left>.+?)\s*(?<op>[+\-*/])\s*(?<right>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 求值表达式
    /// </summary>
    /// <param name="expression">表达式</param>
    /// <param name="variables">变量字典</param>
    /// <returns>求值结果</returns>
    public string Evaluate(string expression, Dictionary<string, JsonElement> variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        expression = expression.Trim();

        if (TryEvaluateMethodCall(expression, variables, out var methodResult))
        {
            return methodResult;
        }

        if (TryEvaluatePropertyAccess(expression, variables, out var propertyResult))
        {
            return propertyResult;
        }

        if (TryEvaluateArithmetic(expression, variables, out var arithmeticResult))
        {
            return arithmeticResult;
        }

        if (TryGetVariableValue(expression, variables, out var variableResult))
        {
            if (variableResult is JsonElement je)
            {
                return je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.ToString();
            }
            return variableResult?.ToString() ?? string.Empty;
        }

        return expression;
    }

    /// <summary>
    /// 尝试求值方法调用
    /// </summary>
    private bool TryEvaluateMethodCall(string expression, Dictionary<string, JsonElement> variables, out string result)
    {
        result = string.Empty;
        var match = MethodCallPattern.Match(expression);

        if (!match.Success)
        {
            return false;
        }

        var target = match.Groups["target"].Value.Trim();
        var method = match.Groups["method"].Value.Trim();
        var argsString = match.Groups["args"].Value.Trim();

        if (!TryGetVariableValue(target, variables, out var targetValue) || targetValue == null)
        {
            return false;
        }

        var args = ParseArguments(argsString, variables);

        result = ExecuteMethod(targetValue, method, args);
        return true;
    }

    /// <summary>
    /// 尝试求值属性访问
    /// </summary>
    private bool TryEvaluatePropertyAccess(string expression, Dictionary<string, JsonElement> variables, out string result)
    {
        result = string.Empty;
        var match = PropertyAccessPattern.Match(expression);

        if (!match.Success)
        {
            return false;
        }

        var target = match.Groups["target"].Value.Trim();
        var property = match.Groups["property"].Value.Trim();

        if (!TryGetVariableValue(target, variables, out var targetValue) || targetValue == null)
        {
            return false;
        }

        var propertyValue = PropertyAccessor.GetPropertyValue(targetValue, property);
        result = propertyValue switch
        {
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.ToString(),
            _ => propertyValue?.ToString() ?? string.Empty
        };
        return true;
    }

    /// <summary>
    /// 尝试求值算术表达式
    /// </summary>
    private bool TryEvaluateArithmetic(string expression, Dictionary<string, JsonElement> variables, out string result)
    {
        result = string.Empty;
        var match = ArithmeticPattern.Match(expression);

        if (!match.Success)
        {
            return false;
        }

        var left = match.Groups["left"].Value.Trim();
        var op = match.Groups["op"].Value.Trim();
        var right = match.Groups["right"].Value.Trim();

        if (!TryGetNumericValue(left, variables, out var leftValue) ||
            !TryGetNumericValue(right, variables, out var rightValue))
        {
            return false;
        }

        double calcResult;
        switch (op)
        {
            case "+":
                calcResult = leftValue + rightValue;
                break;
            case "-":
                calcResult = leftValue - rightValue;
                break;
            case "*":
                calcResult = leftValue * rightValue;
                break;
            case "/":
                if (rightValue == 0)
                {
                    return false;
                }
                calcResult = leftValue / rightValue;
                break;
            default:
                return false;
        }

        if (IsInteger(leftValue) && IsInteger(rightValue) && op != "/")
        {
            result = ((long)calcResult).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            result = calcResult.ToString(CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>
    /// 尝试获取变量值
    /// </summary>
    private bool TryGetVariableValue(string name, Dictionary<string, JsonElement> variables, out object? value)
    {
        value = null;

        if (TryParseLiteral(name, out var literalValue))
        {
            value = literalValue;
            return true;
        }

        var pathParts = name.Split('.');
        var currentKey = pathParts[0];

        if (!variables.TryGetValue(currentKey, out var jsonValue) &&
            !variables.TryGetValue($"{{{{{currentKey}}}}}", out jsonValue))
        {
            return false;
        }

        value = jsonValue;

        for (var i = 1; i < pathParts.Length && value != null; i++)
        {
            value = PropertyAccessor.GetPropertyValue(value, pathParts[i]);
        }

        return true;
    }

    /// <summary>
    /// 尝试获取数值
    /// </summary>
    private bool TryGetNumericValue(string expression, Dictionary<string, JsonElement> variables, out double value)
    {
        value = 0;

        if (double.TryParse(expression, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (TryGetVariableValue(expression, variables, out var varValue) && varValue != null)
        {
            return ConvertToDouble(varValue, out value);
        }

        return false;
    }

    /// <summary>
    /// 转换为 double
    /// </summary>
    private static bool ConvertToDouble(object value, out double result)
    {
        result = 0;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
            {
                result = je.GetDouble();
                return true;
            }

            if (je.ValueKind == JsonValueKind.String)
            {
                var str = je.GetString();
                return str != null && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }

            return false;
        }

        if (value is double d)
        {
            result = d;
            return true;
        }

        if (value is float f)
        {
            result = f;
            return true;
        }

        if (value is long l)
        {
            result = l;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断是否为整数
    /// </summary>
    private bool IsInteger(double value)
    {
        return Math.Abs(value % 1) < double.Epsilon;
    }

    /// <summary>
    /// 解析参数
    /// </summary>
    private List<JsonElement> ParseArguments(string argsString, Dictionary<string, JsonElement> variables)
    {
        var args = new List<JsonElement>();

        if (string.IsNullOrWhiteSpace(argsString))
        {
            return args;
        }

        var parts = argsString.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (TryParseLiteralAsJsonElement(trimmed, out var literal))
            {
                args.Add(literal);
            }
            else if (TryGetVariableValueAsJsonElement(trimmed, variables, out var varValue))
            {
                args.Add(varValue);
            }
            else
            {
                args.Add(JsonSerializer.SerializeToElement(trimmed, SkillsJsonContext.Default.String));
            }
        }

        return args;
    }

    /// <summary>
    /// 尝试获取变量值为 JsonElement
    /// </summary>
    private bool TryGetVariableValueAsJsonElement(string name, Dictionary<string, JsonElement> variables, out JsonElement value)
    {
        value = default;

        var pathParts = name.Split('.');
        var currentKey = pathParts[0];

        if (!variables.TryGetValue(currentKey, out var jsonValue) &&
            !variables.TryGetValue($"{{{{{currentKey}}}}}", out jsonValue))
        {
            return false;
        }

        value = jsonValue;

        for (var i = 1; i < pathParts.Length; i++)
        {
            var propertyValue = PropertyAccessor.GetPropertyValue(value, pathParts[i]);
            if (propertyValue == null)
            {
                return false;
            }
            value = ObjectToJsonElement(propertyValue);
        }

        return true;
    }

    /// <summary>
    /// 将 object 转换为 JsonElement
    /// </summary>
    private static JsonElement ObjectToJsonElement(object obj)
    {
        return obj switch
        {
            JsonElement je => je,
            string s => JsonSerializer.SerializeToElement(s, SkillsJsonContext.Default.String),
            int i => JsonSerializer.SerializeToElement(i, SkillsJsonContext.Default.Int32),
            long l => JsonSerializer.SerializeToElement(l, SkillsJsonContext.Default.Int64),
            double d => JsonSerializer.SerializeToElement(d, SkillsJsonContext.Default.Double),
            bool b => JsonSerializer.SerializeToElement(b, SkillsJsonContext.Default.Boolean),
            _ => JsonSerializer.SerializeToElement(obj.ToString(), SkillsJsonContext.Default.String)
        };
    }

    /// <summary>
    /// 尝试解析字面量
    /// </summary>
    private static bool TryParseLiteral(string value, out object result)
    {
        result = value;

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            result = value[1..^1];
            return true;
        }

        if (long.TryParse(value, out var longValue))
        {
            result = longValue;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
        {
            result = doubleValue;
            return true;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            result = boolValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试解析字面量为 JsonElement
    /// </summary>
    private static bool TryParseLiteralAsJsonElement(string value, out JsonElement result)
    {
        result = default;

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            result = JsonSerializer.SerializeToElement(value[1..^1], SkillsJsonContext.Default.String);
            return true;
        }

        if (long.TryParse(value, out var longValue))
        {
            result = JsonSerializer.SerializeToElement(longValue, SkillsJsonContext.Default.Int64);
            return true;
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
        {
            result = JsonSerializer.SerializeToElement(doubleValue, SkillsJsonContext.Default.Double);
            return true;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            result = JsonSerializer.SerializeToElement(boolValue, SkillsJsonContext.Default.Boolean);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 将 JsonElement 转换为字符串表示（去除 JSON 字符串引号）
    /// </summary>
    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// 执行方法 — 通过 ExpressionMethodRegistry 策略分派，替代 switch
    /// </summary>
    private string ExecuteMethod(object target, string method, List<JsonElement> args)
    {
        var targetString = target switch
        {
            JsonElement je => JsonElementToString(je),
            _ => target.ToString() ?? string.Empty
        };

        var normalizedMethod = MethodNameCache.Normalize(method);

        var methodImpl = ExpressionMethodRegistry.TryGetMethod(normalizedMethod);
        if (methodImpl is not null)
        {
            return methodImpl.Execute(targetString, args, JsonElementToString);
        }

        return targetString;
    }

}
