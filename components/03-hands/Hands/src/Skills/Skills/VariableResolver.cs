
namespace Core.Skills;

/// <summary>
/// 变量解析器 - 支持嵌套变量、默认值和表达式
/// </summary>
[Register]
public sealed class VariableResolver : IVariableResolver
{
    private readonly ConcurrentDictionary<string, ParsedVariable> _parseCache = new();
    private readonly ExpressionEvaluator _expressionEvaluator;

    /// <summary>
    /// 变量匹配正则表达式
    /// </summary>
    private static readonly Regex VariablePattern = new(
        @"\{\{(?<content>[^}]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 初始化变量解析器
    /// </summary>
    public VariableResolver()
    {
        _expressionEvaluator = new ExpressionEvaluator();
    }

    /// <summary>
    /// 解析并替换字符串中的变量
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="variables">变量字典</param>
    /// <param name="throwOnMissing">变量不存在时是否抛出异常</param>
    /// <returns>替换后的字符串</returns>
    public string Resolve(string input, Dictionary<string, JsonElement> variables, bool throwOnMissing = false)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = input;
        var maxIterations = 10;
        var iterations = 0;

        while (result.Contains("{{") && iterations < maxIterations)
        {
            var newResult = ResolveSinglePass(result, variables, throwOnMissing);
            if (newResult == result)
            {
                break;
            }
            result = newResult;
            iterations++;
        }

        return result;
    }

    /// <summary>
    /// 单轮变量解析 - 从内到外解析变量
    /// </summary>
    private string ResolveSinglePass(string input, Dictionary<string, JsonElement> variables, bool throwOnMissing)
    {
        var matches = VariablePattern.Matches(input);
        if (matches.Count == 0)
        {
            return input;
        }

        var variableList = matches.Cast<Match>().ToList();
        var innermostVariables = variableList
            .Where(match => !match.Groups["content"].Value.Contains("{{"))
            .ToList();

        var variablesToProcess = innermostVariables.Count > 0 ? innermostVariables : variableList;

        var sb = new StringBuilder(input);

        foreach (Match match in variablesToProcess.OrderByDescending(m => m.Index))
        {
            var variableContent = match.Groups["content"].Value.Trim();
            var parsedVariable = GetOrParseVariable(variableContent);
            var replacement = GetReplacementValue(parsedVariable, variables, throwOnMissing);

            sb.Remove(match.Index, match.Length);
            sb.Insert(match.Index, replacement);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 验证所有变量是否存在
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="variables">变量字典</param>
    /// <returns>验证结果</returns>
    public VariableValidationResult Validate(string input, Dictionary<string, JsonElement> variables)
    {
        var missingVariables = VariablePattern.Matches(input)
            .Cast<Match>()
            .Select(match => (Match: match, Parsed: GetOrParseVariable(match.Groups["content"].Value.Trim())))
            .Where(x => !x.Parsed.HasDefaultValue)
            .SelectMany(x => FindMissingVariables(x.Match, x.Parsed, variables))
            .ToList();

        return new VariableValidationResult
        {
            IsValid = missingVariables.Count == 0,
            MissingVariables = missingVariables
        };
    }

    private static IEnumerable<string> FindMissingVariables(Match match, ParsedVariable parsedVariable, Dictionary<string, JsonElement> variables)
    {
        var nameToCheck = parsedVariable.Name.Contains("{{")
            ? ResolveNestedName(parsedVariable.Name, variables)
            : parsedVariable.Name;

        if (nameToCheck.Contains("{{"))
        {
            return new[] { parsedVariable.Name };
        }

        return GetVariableValue(parsedVariable with { Name = nameToCheck }, variables, false) == null
            ? new[] { nameToCheck }
            : Enumerable.Empty<string>();
    }

    private static string ResolveNestedName(string name, Dictionary<string, JsonElement> variables)
    {
        var resolver = new VariableResolver();
        var result = name;
        for (var i = 0; i < 10 && result.Contains("{{"); i++)
        {
            result = resolver.Resolve(result, variables, false);
        }
        return result;
    }

    /// <summary>
    /// 获取或解析变量
    /// </summary>
    private ParsedVariable GetOrParseVariable(string content)
    {
        return _parseCache.GetOrAdd(content, ParseVariableContent);
    }

    /// <summary>
    /// 解析变量内容
    /// </summary>
    private ParsedVariable ParseVariableContent(string content)
    {
        var result = new ParsedVariable { OriginalContent = content };

        var defaultValueIndex = FindDefaultValueSeparator(content);
        if (defaultValueIndex >= 0)
        {
            result.DefaultValue = content.AsSpan(defaultValueIndex + 1).Trim().ToString();
            result.HasDefaultValue = true;
            content = content.AsSpan(0, defaultValueIndex).Trim().ToString();
        }

        if (IsExpression(content))
        {
            result.IsExpression = true;
            result.Expression = content;
            result.Name = ExtractVariableNameFromExpression(content);
        }
        else
        {
            result.Name = content;
        }

        return result;
    }

    /// <summary>
    /// 查找默认值分隔符位置（不在括号内的第一个冒号）
    /// </summary>
    private int FindDefaultValueSeparator(string content)
    {
        var depth = 0;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (c == ':' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 判断内容是否是表达式
    /// </summary>
    private bool IsExpression(string content)
    {
        if (content.Contains('+') || content.Contains('-') ||
            content.Contains('*') || content.Contains('/'))
        {
            return true;
        }

        if (content.Contains('(') && content.Contains(')'))
        {
            return true;
        }

        if (content.Contains('.') && !content.Contains("{{"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从表达式中提取变量名
    /// </summary>
    private string ExtractVariableNameFromExpression(string expression)
    {
        var match = Regex.Match(expression, @"^[a-zA-Z_][a-zA-Z0-9_]*");
        return match.Success ? match.Value : expression;
    }

    /// <summary>
    /// 获取替换值
    /// </summary>
    private string GetReplacementValue(ParsedVariable variable, Dictionary<string, JsonElement> variables, bool throwOnMissing)
    {
        var resolvedName = ResolveNestedVariables(variable.Name, variables, throwOnMissing);
        var workingVariable = variable with { Name = resolvedName };

        if (workingVariable.IsExpression)
        {
            var resolvedExpression = ResolveNestedVariables(workingVariable.Expression, variables, throwOnMissing);
            return _expressionEvaluator.Evaluate(resolvedExpression, variables);
        }

        var value = GetVariableValue(workingVariable, variables, throwOnMissing);
        if (value != null)
        {
            if (value is JsonElement je)
            {
                return je.ValueKind == JsonValueKind.String
                    ? je.GetString() ?? string.Empty
                    : je.ToString();
            }
            return value.ToString() ?? string.Empty;
        }

        if (workingVariable.HasDefaultValue)
        {
            return ResolveNestedVariables(workingVariable.DefaultValue, variables, throwOnMissing);
        }

        return $"{{{{{workingVariable.OriginalContent}}}}}";
    }

    /// <summary>
    /// 解析嵌套变量
    /// </summary>
    private string ResolveNestedVariables(string content, Dictionary<string, JsonElement> variables, bool throwOnMissing)
    {
        if (!content.Contains("{{"))
        {
            return content;
        }

        var result = content;
        var maxIterations = 10;
        var iterations = 0;

        while (result.Contains("{{") && iterations < maxIterations)
        {
            var newResult = Resolve(result, variables, throwOnMissing);
            if (newResult == result)
            {
                break;
            }

            result = newResult;
            iterations++;
        }

        return result;
    }

    /// <summary>
    /// 获取变量值
    /// </summary>
    private static object? GetVariableValue(ParsedVariable variable, Dictionary<string, JsonElement> variables, bool throwOnMissing)
    {
        var pathParts = variable.Name.Split('.');
        var currentKey = pathParts[0];

        if (!variables.TryGetValue(currentKey, out var value) &&
            !variables.TryGetValue($"{{{{{currentKey}}}}}", out value))
        {
            if (throwOnMissing)
            {
                throw new VariableResolutionException(L.T(StringKey.VariableNotExist, currentKey));
            }

            return null;
        }

        object? currentValue = value;

        for (var i = 1; i < pathParts.Length && currentValue != null; i++)
        {
            currentValue = PropertyAccessor.GetPropertyValue(currentValue, pathParts[i]);
        }

        return currentValue;
    }

    /// <summary>
    /// 清除解析缓存
    /// </summary>
    public void ClearCache()
    {
        _parseCache.Clear();
    }
}

/// <summary>
/// 解析后的变量信息
/// </summary>
[DebuggerDisplay("{Name}, Expression={IsExpression}, HasDefault={HasDefaultValue}")]
internal sealed record ParsedVariable
{
    public string OriginalContent { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsExpression { get; set; }
    public string Expression { get; set; } = string.Empty;
    public bool HasDefaultValue { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
}

/// <summary>
/// 变量验证结果
/// </summary>
public sealed class VariableValidationResult
{
    public bool IsValid { get; set; }
    public List<string> MissingVariables { get; set; } = new();
}

/// <summary>
/// 变量解析异常
/// </summary>
public class VariableResolutionException : WorkflowException
{
    public VariableResolutionException(string message)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ValidationVariableResolution.ToValue(), category: ErrorCategory.Validation) { }

    public VariableResolutionException(string message, Exception inner)
        : base(message, inner, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ValidationVariableResolution.ToValue(), category: ErrorCategory.Validation) { }
}
