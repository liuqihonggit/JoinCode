
namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 系统提示词验证结果
/// </summary>
public sealed class SystemPromptValidationResult
{
    /// <summary>
    /// 验证是否通过
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// 验证消息
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 找到的系统提示词列表
    /// </summary>
    public IReadOnlyList<string> FoundPrompts { get; }

    /// <summary>
    /// 缺失的预期内容
    /// </summary>
    public IReadOnlyList<string> MissingContents { get; }

    public SystemPromptValidationResult(
        bool isValid,
        string message,
        IReadOnlyList<string>? foundPrompts = null,
        IReadOnlyList<string>? missingContents = null)
    {
        IsValid = isValid;
        Message = message;
        FoundPrompts = foundPrompts ?? Array.Empty<string>();
        MissingContents = missingContents ?? Array.Empty<string>();
    }

    /// <summary>
    /// 成功的验证结果
    /// </summary>
    public static SystemPromptValidationResult Success(IReadOnlyList<string> foundPrompts)
    {
        return new SystemPromptValidationResult(true, "系统提示词验证通过", foundPrompts);
    }

    /// <summary>
    /// 失败的验证结果
    /// </summary>
    public static SystemPromptValidationResult Failure(string message, IReadOnlyList<string>? missingContents = null)
    {
        return new SystemPromptValidationResult(false, message, Array.Empty<string>(), missingContents);
    }
}

/// <summary>
/// 系统提示词验证器
/// 验证 ChatCompletionRequest 中的系统提示词内容
/// </summary>
public sealed class SystemPromptValidator
{
    /// <summary>
    /// 验证系统提示词是否包含预期内容
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="expectedContents">预期包含的内容列表</param>
    /// <param name="comparison">字符串比较方式</param>
    /// <returns>验证结果</returns>
    public SystemPromptValidationResult Validate(
        ChatCompletionRequest? request,
        IEnumerable<string>? expectedContents = null,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var systemPrompts = ExtractSystemPrompts(request);

        if (systemPrompts.Count == 0)
        {
            return SystemPromptValidationResult.Failure("未找到系统提示词");
        }

        var fullSystemPrompt = string.Join("\n", systemPrompts);

        if (expectedContents is null)
        {
            return SystemPromptValidationResult.Success(systemPrompts);
        }

        var missingContents = expectedContents
            .Where(expected => !fullSystemPrompt.Contains(expected, comparison))
            .ToList();

        if (missingContents.Count > 0)
        {
            return SystemPromptValidationResult.Failure(
                $"系统提示词缺少 {missingContents.Count} 项预期内容",
                missingContents);
        }

        return SystemPromptValidationResult.Success(systemPrompts);
    }

    /// <summary>
    /// 验证系统提示词是否包含特定内容
    /// </summary>
    public bool ContainsContent(
        ChatCompletionRequest? request,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(expectedContent))
            return false;

        var systemPrompts = ExtractSystemPrompts(request);
        var fullSystemPrompt = string.Join("\n", systemPrompts);

        return fullSystemPrompt.Contains(expectedContent, comparison);
    }

    /// <summary>
    /// 验证系统提示词是否包含所有指定内容
    /// </summary>
    public bool ContainsAllContents(
        ChatCompletionRequest? request,
        IEnumerable<string> expectedContents,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var systemPrompts = ExtractSystemPrompts(request);
        var fullSystemPrompt = string.Join("\n", systemPrompts);

        return expectedContents.All(content =>
            !string.IsNullOrWhiteSpace(content) &&
            fullSystemPrompt.Contains(content, comparison));
    }

    /// <summary>
    /// 验证系统提示词是否包含任意指定内容
    /// </summary>
    public bool ContainsAnyContent(
        ChatCompletionRequest? request,
        IEnumerable<string> expectedContents,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var systemPrompts = ExtractSystemPrompts(request);
        var fullSystemPrompt = string.Join("\n", systemPrompts);

        return expectedContents.Any(content =>
            !string.IsNullOrWhiteSpace(content) &&
            fullSystemPrompt.Contains(content, comparison));
    }

    /// <summary>
    /// 验证系统提示词是否以指定内容开头
    /// </summary>
    public bool StartsWith(
        ChatCompletionRequest? request,
        string prefix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        var firstSystemPrompt = request?.Messages
            ?.FirstOrDefault(m => m.Role == MessageRoles.System)
            ?.Content;

        return firstSystemPrompt?.StartsWith(prefix, comparison) ?? false;
    }

    /// <summary>
    /// 验证系统提示词是否以指定内容结尾
    /// </summary>
    public bool EndsWith(
        ChatCompletionRequest? request,
        string suffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return false;

        var systemPrompts = ExtractSystemPrompts(request);
        if (systemPrompts.Count == 0)
            return false;

        var lastPrompt = systemPrompts[^1];
        return lastPrompt.EndsWith(suffix, comparison);
    }

    /// <summary>
    /// 验证系统提示词是否匹配指定模式
    /// </summary>
    public bool MatchesPattern(
        ChatCompletionRequest? request,
        SystemPromptPattern pattern)
    {
        return pattern.Matches(request);
    }

    /// <summary>
    /// 获取系统提示词数量
    /// </summary>
    public int GetSystemPromptCount(ChatCompletionRequest? request)
    {
        return ExtractSystemPrompts(request).Count;
    }

    /// <summary>
    /// 获取完整的系统提示词文本
    /// </summary>
    public string GetFullSystemPrompt(ChatCompletionRequest? request, string separator = "\n\n")
    {
        var prompts = ExtractSystemPrompts(request);
        return string.Join(separator, prompts);
    }

    /// <summary>
    /// 断言系统提示词包含指定内容
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertContains(
        ChatCompletionRequest? request,
        string expectedContent,
        string? message = null,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (!ContainsContent(request, expectedContent, comparison))
        {
            var actualContent = GetFullSystemPrompt(request);
            var errorMessage = message ??
                $"系统提示词应包含 '{expectedContent}'，但实际内容:\n{actualContent}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言系统提示词不包含指定内容
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertNotContains(
        ChatCompletionRequest? request,
        string unexpectedContent,
        string? message = null,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (ContainsContent(request, unexpectedContent, comparison))
        {
            var errorMessage = message ??
                $"系统提示词不应包含 '{unexpectedContent}'";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言系统提示词数量符合预期
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertCount(
        ChatCompletionRequest? request,
        int expectedCount,
        string? message = null)
    {
        var actualCount = GetSystemPromptCount(request);
        if (actualCount != expectedCount)
        {
            var errorMessage = message ??
                $"期望系统提示词数量为 {expectedCount}，实际为 {actualCount}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 提取系统提示词列表
    /// </summary>
    private static IReadOnlyList<string> ExtractSystemPrompts(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return Array.Empty<string>();

        return request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();
    }
}

/// <summary>
/// 系统提示词验证模式
/// </summary>
public abstract class SystemPromptPattern
{
    /// <summary>
    /// 检查请求是否匹配此模式
    /// </summary>
    public abstract bool Matches(ChatCompletionRequest? request);

    /// <summary>
    /// 创建包含所有指定内容的模式
    /// </summary>
    public static SystemPromptPattern ContainsAll(params string[] contents)
    {
        return new ContainsAllPattern(contents);
    }

    /// <summary>
    /// 创建包含任意指定内容的模式
    /// </summary>
    public static SystemPromptPattern ContainsAny(params string[] contents)
    {
        return new ContainsAnyPattern(contents);
    }

    /// <summary>
    /// 创建以指定前缀开头的模式
    /// </summary>
    public static SystemPromptPattern StartsWith(string prefix)
    {
        return new StartsWithPattern(prefix);
    }

    private sealed class ContainsAllPattern : SystemPromptPattern
    {
        private readonly string[] _contents;

        public ContainsAllPattern(string[] contents)
        {
            _contents = contents;
        }

        public override bool Matches(ChatCompletionRequest? request)
        {
            var systemPrompts = request?.Messages
                ?.Where(m => m.Role == MessageRoles.System)
                .Select(m => m.Content)
                .ToList() ?? new List<string>();

            var fullPrompt = string.Join("\n", systemPrompts);
            return _contents.All(c => fullPrompt.Contains(c, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class ContainsAnyPattern : SystemPromptPattern
    {
        private readonly string[] _contents;

        public ContainsAnyPattern(string[] contents)
        {
            _contents = contents;
        }

        public override bool Matches(ChatCompletionRequest? request)
        {
            var systemPrompts = request?.Messages
                ?.Where(m => m.Role == MessageRoles.System)
                .Select(m => m.Content)
                .ToList() ?? new List<string>();

            var fullPrompt = string.Join("\n", systemPrompts);
            return _contents.Any(c => fullPrompt.Contains(c, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class StartsWithPattern : SystemPromptPattern
    {
        private readonly string _prefix;

        public StartsWithPattern(string prefix)
        {
            _prefix = prefix;
        }

        public override bool Matches(ChatCompletionRequest? request)
        {
            var firstSystem = request?.Messages
                ?.FirstOrDefault(m => m.Role == MessageRoles.System)
                ?.Content;

            return firstSystem?.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}

/// <summary>
/// 断言异常
/// </summary>
public class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
    public AssertException(string message, Exception innerException) : base(message, innerException) { }
}
