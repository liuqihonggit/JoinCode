
namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 测试触发器
/// 包含触发器类型和可选参数
/// </summary>
public sealed class TestTrigger
{
    /// <summary>
    /// 触发器类型
    /// </summary>
    public TestTriggerType Type { get; }

    /// <summary>
    /// 触发器参数（可选）
    /// </summary>
    public string? Parameter { get; }

    public TestTrigger(TestTriggerType type, string? parameter = null)
    {
        Type = type;
        Parameter = parameter;
    }

    /// <summary>
    /// 检查触发器是否有参数
    /// </summary>
    public bool HasParameter => !string.IsNullOrWhiteSpace(Parameter);

    /// <summary>
    /// 获取参数值，如果为空则返回默认值
    /// </summary>
    public string GetParameterOrDefault(string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return HasParameter ? Parameter! : defaultValue;
    }

    public override string ToString() => HasParameter ? $"[TEST:{Type.ToTypeString()}:{Parameter}]" : $"[TEST:{Type.ToTypeString()}]";
}

/// <summary>
/// 测试触发器解析器
/// 解析 [TEST:...] 格式的测试指令
/// </summary>
public static partial class TestTriggerParser
{
    // 匹配格式: [TEST:TYPE] 或 [TEST:TYPE:PARAMETER]
    // PARAMETER 可以包含除 ] 之外的任何字符
    [GeneratedRegex(@"\[TEST:(\w+)(?::([^\]]*))?\]", RegexOptions.IgnoreCase)]
    private static partial Regex TestTriggerRegex();

    /// <summary>
    /// 从输入字符串中解析测试触发器
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <returns>解析结果，如果没有找到触发器则返回 null</returns>
    public static TestTrigger? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = TestTriggerRegex().Match(input);
        if (!match.Success)
            return null;

        var typeString = match.Groups[1].Value;
        var parameter = match.Groups.Count > 2 && match.Groups[2].Success ? match.Groups[2].Value : null;

        if (!TestTriggerTypeExtensions.TryParseFromString(typeString, out var type))
            return null;

        return new TestTrigger(type, parameter);
    }

    /// <summary>
    /// 从输入字符串中解析所有测试触发器
    /// </summary>
    public static IReadOnlyList<TestTrigger> ParseAll(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<TestTrigger>();

        var matches = TestTriggerRegex().Matches(input);
        var triggers = new List<TestTrigger>();

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var typeString = match.Groups[1].Value;
            var parameter = match.Groups.Count > 2 && match.Groups[2].Success ? match.Groups[2].Value : null;

            if (TestTriggerTypeExtensions.TryParseFromString(typeString, out var type))
            {
                triggers.Add(new TestTrigger(type, parameter));
            }
        }

        return triggers;
    }

    /// <summary>
    /// 检查输入是否包含测试触发器
    /// </summary>
    public static bool ContainsTrigger(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return TestTriggerRegex().IsMatch(input);
    }

    /// <summary>
    /// 检查输入是否包含指定类型的测试触发器
    /// </summary>
    public static bool ContainsTrigger(string? input, TestTriggerType type)
    {
        var triggers = ParseAll(input);
        return triggers.Any(t => t.Type == type);
    }

    /// <summary>
    /// 从用户消息中提取测试触发器
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <returns>第一个找到的测试触发器，如果没有则返回 null</returns>
    public static TestTrigger? ExtractFromUserMessage(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return null;

        var userMessage = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .FirstOrDefault();

        return Parse(userMessage);
    }

    /// <summary>
    /// 从用户消息中提取所有测试触发器
    /// </summary>
    public static IReadOnlyList<TestTrigger> ExtractAllFromUserMessage(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return Array.Empty<TestTrigger>();

        var userMessage = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .FirstOrDefault();

        return ParseAll(userMessage);
    }

    /// <summary>
    /// 移除输入字符串中的所有测试触发器标记
    /// </summary>
    public static string RemoveTriggers(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return TestTriggerRegex().Replace(input, string.Empty).Trim();
    }

    /// <summary>
    /// 创建测试触发器字符串
    /// </summary>
    public static string CreateTrigger(TestTriggerType type, string? parameter = null)
    {
        return parameter is null
            ? $"[TEST:{type.ToTypeString()}]"
            : $"[TEST:{type.ToTypeString()}:{parameter}]";
    }
}
