namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 测试触发器类型枚举
/// 定义不同类型的测试触发器，用于验证提示词和请求内容
/// </summary>
public enum TestTriggerType
{
    /// <summary>
    /// 验证系统提示词
    /// </summary>
    SystemPrompt = 0,

    /// <summary>
    /// 验证用户提示词
    /// </summary>
    UserPrompt = 1,

    /// <summary>
    /// 验证工具提示词
    /// </summary>
    ToolPrompt = 2,

    /// <summary>
    /// 验证用户提示词注入
    /// </summary>
    UserInjection = 3,

    /// <summary>
    /// 验证 API Key
    /// </summary>
    ApiKey = 4,

    /// <summary>
    /// 返回完整请求
    /// </summary>
    FullRequest = 5
}

/// <summary>
/// 测试触发器类型扩展方法
/// </summary>
public static class TestTriggerTypeExtensions
{
    private static readonly Dictionary<TestTriggerType, string> TypeNames = new()
    {
        [TestTriggerType.SystemPrompt] = "SYSTEM",
        [TestTriggerType.UserPrompt] = "USER",
        [TestTriggerType.ToolPrompt] = "TOOL",
        [TestTriggerType.UserInjection] = "INJECTION",
        [TestTriggerType.ApiKey] = "APIKEY",
        [TestTriggerType.FullRequest] = "FULL"
    };

    private static readonly Dictionary<string, TestTriggerType> NameToTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYSTEM"] = TestTriggerType.SystemPrompt,
        ["USER"] = TestTriggerType.UserPrompt,
        ["TOOL"] = TestTriggerType.ToolPrompt,
        ["INJECTION"] = TestTriggerType.UserInjection,
        ["APIKEY"] = TestTriggerType.ApiKey,
        ["FULL"] = TestTriggerType.FullRequest
    };

    /// <summary>
    /// 获取触发器类型的字符串标识
    /// </summary>
    public static string ToTypeString(this TestTriggerType type)
    {
        return TypeNames.TryGetValue(type, out var name) ? name : type.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// 从字符串解析触发器类型
    /// </summary>
    public static bool TryParseFromString(string? value, out TestTriggerType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            type = default;
            return false;
        }

        return NameToTypeMap.TryGetValue(value.Trim(), out type);
    }

    /// <summary>
    /// 获取触发器类型的描述
    /// </summary>
    public static string GetDescription(this TestTriggerType type)
    {
        return type switch
        {
            TestTriggerType.SystemPrompt => "验证系统提示词内容",
            TestTriggerType.UserPrompt => "验证用户提示词内容",
            TestTriggerType.ToolPrompt => "验证工具提示词内容",
            TestTriggerType.UserInjection => "验证用户提示词注入",
            TestTriggerType.ApiKey => "验证 API Key",
            TestTriggerType.FullRequest => "返回完整请求 JSON",
            _ => "未知类型"
        };
    }
}
