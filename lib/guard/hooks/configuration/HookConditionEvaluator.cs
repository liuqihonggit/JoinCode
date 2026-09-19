namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子输入字段名常量
/// </summary>
public static class InputFieldNames {
    /// <summary>
    /// command 字段名
    /// </summary>
    public const string Command = "command";
}

/// <summary>
/// 钩子条件求值器接口 — 对钩子配置中的 if 条件表达式进行求值
/// </summary>
public interface IHookConditionEvaluator {
    /// <summary>
    /// 异步求值条件表达式
    /// </summary>
    /// <param name="condition">条件表达式,支持 &amp;&amp; / || / ! / event: / matcher: / input. 等语法</param>
    /// <param name="input">钩子输入</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>条件成立返回 true,否则返回 false;空条件默认返回 true</returns>
    Task<bool> EvaluateAsync(
        string? condition,
        HookInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子条件求值器 — 解析并求值 if 条件表达式,支持逻辑组合、事件/匹配器/输入字段匹配与正则模式
/// </summary>
[Register(typeof(IHookConditionEvaluator), ServiceLifetime.Singleton)]
public sealed partial class HookConditionEvaluator : ServiceEntity, IHookConditionEvaluator {
    private readonly ILogger<HookConditionEvaluator>? _logger;

    private static readonly ConcurrentDictionary<string, Regex> ConditionPatternCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 构造函数 — 注入可选的日志记录器
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public HookConditionEvaluator(ILogger<HookConditionEvaluator>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> EvaluateAsync(
        string? condition,
        HookInput input,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(condition)) {
            return Task.FromResult(true);
        }

        try {
            var result = EvaluateCondition(condition.Trim(), input);
            _logger?.LogDebug(
                "Condition '{Condition}' evaluated to {Result} for event {Event}",
                condition,
                result,
                input.Event);

            return Task.FromResult(result);
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to evaluate condition: {Condition}", condition);
            return Task.FromResult(true);
        }
    }

    private bool EvaluateCondition(string condition, HookInput input) {
        if (condition.Contains("||")) {
            var parts = condition.Split("||", StringSplitOptions.TrimEntries);
            return parts.Any(p => EvaluateCondition(p, input));
        }

        if (condition.Contains("&&")) {
            var parts = condition.Split("&&", StringSplitOptions.TrimEntries);
            return parts.All(p => EvaluateCondition(p, input));
        }

        if (condition.StartsWith("!")) {
            return !EvaluateCondition(condition[1..].Trim(), input);
        }

        if (condition.StartsWith("(") && condition.EndsWith(")")) {
            return EvaluateCondition(condition[1..^1].Trim(), input);
        }

        return EvaluateSingleCondition(condition, input);
    }

    private bool EvaluateSingleCondition(string condition, HookInput input) {
        if (TryParseToolPattern(condition, out var toolName, out var pattern)) {
            return EvaluateToolPattern(toolName, pattern, input);
        }

        if (condition.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) {
            var eventName = condition[6..].Trim();
            return input.Event.ToEventName().Equals(eventName, StringComparison.OrdinalIgnoreCase);
        }

        if (condition.StartsWith("matcher:", StringComparison.OrdinalIgnoreCase)) {
            var matcherPattern = condition[8..].Trim();
            return MatchesPattern(input.Matcher ?? "", matcherPattern);
        }

        if (condition.StartsWith("input.", StringComparison.OrdinalIgnoreCase)) {
            return EvaluateInputCondition(condition[6..], input);
        }

        return input.ToolName?.Equals(condition, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private bool TryParseToolPattern(string condition, out string toolName, out string? pattern) {
        toolName = condition;
        pattern = null;

        var match = Regex.Match(condition, @"^(\w+)\s*\((.*)\)$");
        if (!match.Success) return false;

        toolName = match.Groups[1].Value;
        pattern = match.Groups[2].Value.Trim();
        return true;
    }

    private bool EvaluateToolPattern(string toolName, string? pattern, HookInput input) {
        if (!string.Equals(input.ToolName, toolName, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (string.IsNullOrEmpty(pattern) || pattern == "*") {
            return true;
        }

        if (!input.Payload.TryGetValue("input", out var inputElement) ||
            inputElement.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (inputElement.TryGetProperty(InputFieldNames.Command, out var cmdElement) &&
            cmdElement.ValueKind == JsonValueKind.String) {
            var command = cmdElement.GetString() ?? "";
            if (MatchesPattern(command, pattern))
                return true;
        }

        foreach (var property in inputElement.EnumerateObject()) {
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.GetRawText();
            if (MatchesPattern(value, pattern)) {
                return true;
            }
        }

        return false;
    }

    private bool EvaluateInputCondition(string condition, HookInput input) {
        var colonIndex = condition.IndexOf(':');
        if (colonIndex < 0) return false;

        var key = condition[..colonIndex].Trim();
        var valuePattern = condition[(colonIndex + 1)..].Trim();

        if (!input.Payload.TryGetValue("input", out var inputElement) ||
            inputElement.ValueKind != JsonValueKind.Object) {
            return false;
        }

        var value = GetNestedValue(inputElement, key);
        return MatchesPattern(value ?? "", valuePattern);
    }

    private string? GetNestedValue(JsonElement element, string path) {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts) {
            if (current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(part, out var next)) {
                current = next;
            } else {
                return null;
            }
        }

        return current.ValueKind switch {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => current.GetRawText()
        };
    }

    private bool MatchesPattern(string value, string pattern) {
        if (pattern == value) return true;
        if (pattern == "*") return true;

        if (pattern.EndsWith('*') && !pattern.StartsWith('*')) {
            var prefix = pattern[..^1];
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*') && !pattern.EndsWith('*')) {
            var suffix = pattern[1..];
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*') && pattern.EndsWith('*')) {
            var substring = pattern[1..^1];
            return value.Contains(substring, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains("^") || pattern.Contains("$") || pattern.Contains(".*")) {
            try {
                var regex = ConditionPatternCache.GetOrAdd(pattern, static p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                return regex.IsMatch(value);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "评估正则模式 {Pattern} 失败", pattern);
            }
        }

        return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 条件求值异常 — 在钩子条件表达式求值过程中发生错误时抛出
/// </summary>
public partial class ConditionEvaluationException : WorkflowException {
    /// <summary>
    /// 构造函数 — 使用指定错误消息初始化
    /// </summary>
    /// <param name="message">错误消息</param>
    public ConditionEvaluationException(string message)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowConditionEvaluation.ToValue(), category: ErrorCategory.Workflow) { }

    /// <summary>
    /// 构造函数 — 使用指定错误消息与内部异常初始化
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public ConditionEvaluationException(string message, Exception innerException)
        : base(message, innerException, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowConditionEvaluation.ToValue(), category: ErrorCategory.Workflow) { }

    /// <summary>
    /// 触发异常的条件表达式
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// 触发异常时的输入事件名称
    /// </summary>
    public string? InputEvent { get; init; }
}