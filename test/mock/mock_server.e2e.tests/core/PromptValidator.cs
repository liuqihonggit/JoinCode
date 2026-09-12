
namespace MockServer.E2E.Tests.Core;

/// <summary>
/// 提示词验证器
/// 用于验证 ChatCompletionRequest 中的系统提示词和用户提示词
/// </summary>
public sealed class PromptValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 从 JSON 字符串解析请求
    /// </summary>
    public static ChatCompletionRequest? ParseRequest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChatCompletionRequest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 验证系统提示词
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <returns>系统提示词列表</returns>
    public static IReadOnlyList<string> ValidateSystemPrompt(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return Array.Empty<string>();

        return request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();
    }

    /// <summary>
    /// 验证系统提示词（从 JSON）
    /// </summary>
    public static IReadOnlyList<string> ValidateSystemPrompt(string json)
    {
        var request = ParseRequest(json);
        return ValidateSystemPrompt(request);
    }

    /// <summary>
    /// 验证用户提示词
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <returns>用户提示词列表</returns>
    public static IReadOnlyList<string> ValidateUserPrompt(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return Array.Empty<string>();

        return request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList();
    }

    /// <summary>
    /// 验证用户提示词（从 JSON）
    /// </summary>
    public static IReadOnlyList<string> ValidateUserPrompt(string json)
    {
        var request = ParseRequest(json);
        return ValidateUserPrompt(request);
    }

    /// <summary>
    /// 检查是否包含指定的系统提示词内容
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="expectedContent">期望的内容</param>
    /// <param name="comparison">字符串比较方式</param>
    /// <returns>是否包含</returns>
    public static bool ContainsSystemPrompt(
        ChatCompletionRequest? request,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (request?.Messages is null || string.IsNullOrEmpty(expectedContent))
            return false;

        return request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Any(m => m.Content.Contains(expectedContent, comparison));
    }

    /// <summary>
    /// 检查是否包含指定的系统提示词内容（从 JSON）
    /// </summary>
    public static bool ContainsSystemPrompt(
        string json,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var request = ParseRequest(json);
        return ContainsSystemPrompt(request, expectedContent, comparison);
    }

    /// <summary>
    /// 检查是否包含指定的用户提示词内容
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="expectedContent">期望的内容</param>
    /// <param name="comparison">字符串比较方式</param>
    /// <returns>是否包含</returns>
    public static bool ContainsUserPrompt(
        ChatCompletionRequest? request,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (request?.Messages is null || string.IsNullOrEmpty(expectedContent))
            return false;

        return request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Any(m => m.Content.Contains(expectedContent, comparison));
    }

    /// <summary>
    /// 检查是否包含指定的用户提示词内容（从 JSON）
    /// </summary>
    public static bool ContainsUserPrompt(
        string json,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var request = ParseRequest(json);
        return ContainsUserPrompt(request, expectedContent, comparison);
    }

    /// <summary>
    /// 获取完整的系统提示词（合并多个 system 消息）
    /// </summary>
    public static string GetFullSystemPrompt(ChatCompletionRequest? request, string separator = "\n\n")
    {
        var prompts = ValidateSystemPrompt(request);
        return string.Join(separator, prompts);
    }

    /// <summary>
    /// 获取完整的用户提示词（合并多个 user 消息）
    /// </summary>
    public static string GetFullUserPrompt(ChatCompletionRequest? request, string separator = "\n\n")
    {
        var prompts = ValidateUserPrompt(request);
        return string.Join(separator, prompts);
    }

    /// <summary>
    /// 检查系统提示词是否以指定内容开头
    /// </summary>
    public static bool SystemPromptStartsWith(
        ChatCompletionRequest? request,
        string prefix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var firstSystem = request?.Messages
            .FirstOrDefault(m => m.Role == MessageRoles.System);

        return firstSystem?.Content.StartsWith(prefix, comparison) ?? false;
    }

    /// <summary>
    /// 检查用户提示词是否以指定内容开头
    /// </summary>
    public static bool UserPromptStartsWith(
        ChatCompletionRequest? request,
        string prefix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        var firstUser = request?.Messages
            .FirstOrDefault(m => m.Role == MessageRoles.User);

        return firstUser?.Content.StartsWith(prefix, comparison) ?? false;
    }
}
