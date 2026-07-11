
namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 注入检测结果
/// </summary>
public sealed class InjectionDetectionResult
{
    /// <summary>
    /// 是否检测到注入
    /// </summary>
    public bool HasInjection { get; }

    /// <summary>
    /// 检测到的注入类型
    /// </summary>
    public InjectionType Type { get; }

    /// <summary>
    /// 匹配的关键词
    /// </summary>
    public IReadOnlyList<string> MatchedKeywords { get; }

    /// <summary>
    /// 检测详情
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// 被检测的消息内容
    /// </summary>
    public string? DetectedInContent { get; }

    public InjectionDetectionResult(
        bool hasInjection,
        InjectionType type,
        IReadOnlyList<string>? matchedKeywords = null,
        string? details = null,
        string? detectedInContent = null)
    {
        HasInjection = hasInjection;
        Type = type;
        MatchedKeywords = matchedKeywords ?? Array.Empty<string>();
        Details = details ?? string.Empty;
        DetectedInContent = detectedInContent;
    }

    /// <summary>
    /// 无注入的结果
    /// </summary>
    public static InjectionDetectionResult Clean(string? content = null)
    {
        return new InjectionDetectionResult(false, InjectionType.None, Array.Empty<string>(), "未检测到注入", content);
    }

    /// <summary>
    /// 检测到注入的结果
    /// </summary>
    public static InjectionDetectionResult Detected(
        InjectionType type,
        IReadOnlyList<string> keywords,
        string details,
        string? content = null)
    {
        return new InjectionDetectionResult(true, type, keywords, details, content);
    }
}

/// <summary>
/// 注入类型
/// </summary>
public enum InjectionType
{
    /// <summary>
    /// 无注入
    /// </summary>
    None = 0,

    /// <summary>
    /// 系统提示词覆盖
    /// </summary>
    SystemPromptOverride,

    /// <summary>
    /// 角色扮演注入
    /// </summary>
    RolePlayInjection,

    /// <summary>
    /// 指令忽略攻击
    /// </summary>
    IgnoreInstructions,

    /// <summary>
    /// 分隔符注入
    /// </summary>
    DelimiterInjection,

    /// <summary>
    /// 编码/混淆注入
    /// </summary>
    EncodingObfuscation,

    /// <summary>
    /// 其他类型
    /// </summary>
    Other
}

/// <summary>
/// 用户提示词注入验证器
/// 检测用户输入中的注入关键词并验证注入的提示词是否正确添加到请求中
/// </summary>
public sealed class UserPromptInjectionValidator
{
    // 注入检测关键词字典
    private static readonly Dictionary<InjectionType, string[]> InjectionKeywords = new()
    {
        [InjectionType.SystemPromptOverride] = new[]
        {
            "ignore previous instructions",
            "ignore all previous",
            "disregard previous",
            "forget previous",
            "new instructions:",
            "system prompt:",
            "you are now",
            "act as",
            "pretend to be",
            "roleplay as"
        },

        [InjectionType.RolePlayInjection] = new[]
        {
            "let's roleplay",
            "roleplay scenario",
            "imagine you are",
            "pretend you are",
            "you are a",
            "you are an",
            "from now on you are"
        },

        [InjectionType.IgnoreInstructions] = new[]
        {
            "ignore the above",
            "ignore above",
            "do not follow",
            "disobey",
            "override",
            "bypass",
            "jailbreak",
            "DAN mode"
        },

        [InjectionType.DelimiterInjection] = new[]
        {
            "```system",
            "```instructions",
            "[system]",
            "[instructions]",
            "<system>",
            "</system>",
            "<!-- system -->",
            "/* system */"
        },

        [InjectionType.EncodingObfuscation] = new[]
        {
            "base64:",
            "hex:",
            "rot13:",
            "\x00",
            "\u0000",
            "zero-width",
            "invisible characters"
        }
    };

    // 注入标记器 - 用于检测提示词是否已正确注入
    private static readonly string[] InjectionMarkers = new[]
    {
        "[系统提示:",
        "[注入:",
        "[自动注入]",
        "SYSTEM_INJECTION",
        "PROMPT_INJECTION",
        "<!-- injected -->"
    };

    /// <summary>
    /// 检测用户输入中是否包含注入关键词
    /// </summary>
    /// <param name="userInput">用户输入内容</param>
    /// <returns>检测结果</returns>
    public InjectionDetectionResult DetectInjection(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return InjectionDetectionResult.Clean(userInput);
        }

        var lowerInput = userInput.ToLowerInvariant();
        var matchedKeywords = new List<string>();
        InjectionType detectedType = InjectionType.None;

        foreach (var (type, keywords) in InjectionKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (lowerInput.Contains(keyword.ToLowerInvariant()))
                {
                    matchedKeywords.Add(keyword);
                    detectedType = type;
                }
            }
        }

        if (matchedKeywords.Count == 0)
        {
            return InjectionDetectionResult.Clean(userInput);
        }

        var details = $"检测到 {matchedKeywords.Count} 个注入关键词，类型: {detectedType}";
        return InjectionDetectionResult.Detected(detectedType, matchedKeywords, details, userInput);
    }

    /// <summary>
    /// 检测请求中是否包含注入
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <returns>检测结果</returns>
    public InjectionDetectionResult DetectInjection(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
        {
            return InjectionDetectionResult.Clean();
        }

        var userMessages = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList();

        foreach (var message in userMessages)
        {
            var result = DetectInjection(message);
            if (result.HasInjection)
            {
                return result;
            }
        }

        return InjectionDetectionResult.Clean();
    }

    /// <summary>
    /// 检查请求中是否包含注入
    /// </summary>
    public bool ContainsInjection(ChatCompletionRequest? request)
    {
        return DetectInjection(request).HasInjection;
    }

    /// <summary>
    /// 检查用户输入是否包含注入
    /// </summary>
    public bool ContainsInjection(string? userInput)
    {
        return DetectInjection(userInput).HasInjection;
    }

    /// <summary>
    /// 验证注入的提示词是否正确添加到请求中
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="expectedInjectionContent">预期注入的内容</param>
    /// <returns>是否验证通过</returns>
    public bool ValidateInjectionPresent(
        ChatCompletionRequest? request,
        string? expectedInjectionContent = null)
    {
        if (request?.Messages is null)
            return false;

        // 检查系统消息中是否包含注入标记
        var systemMessages = request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();

        var fullSystemPrompt = string.Join("\n", systemMessages);

        // 检查是否有注入标记
        var hasInjectionMarker = InjectionMarkers.Any(marker =>
            fullSystemPrompt.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (!hasInjectionMarker)
            return false;

        // 如果指定了预期内容，检查是否包含
        if (!string.IsNullOrWhiteSpace(expectedInjectionContent))
        {
            return fullSystemPrompt.Contains(expectedInjectionContent, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /// <summary>
    /// 验证特定类型的注入是否存在于请求中
    /// </summary>
    public bool ValidateInjectionTypePresent(ChatCompletionRequest? request, InjectionType type)
    {
        var detectionResult = DetectInjection(request);
        return detectionResult.HasInjection && detectionResult.Type == type;
    }

    /// <summary>
    /// 获取所有检测到的注入
    /// </summary>
    public IReadOnlyList<InjectionDetectionResult> GetAllInjections(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return Array.Empty<InjectionDetectionResult>();

        var results = new List<InjectionDetectionResult>();
        var userMessages = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList();

        foreach (var message in userMessages)
        {
            var result = DetectInjection(message);
            if (result.HasInjection)
            {
                results.Add(result);
            }
        }

        return results;
    }

    /// <summary>
    /// 获取注入统计信息
    /// </summary>
    public InjectionStatistics GetStatistics(ChatCompletionRequest? request)
    {
        var injections = GetAllInjections(request);
        var byType = injections
            .GroupBy(i => i.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new InjectionStatistics(
            injections.Count,
            byType.GetValueOrDefault(InjectionType.SystemPromptOverride),
            byType.GetValueOrDefault(InjectionType.RolePlayInjection),
            byType.GetValueOrDefault(InjectionType.IgnoreInstructions),
            byType.GetValueOrDefault(InjectionType.DelimiterInjection),
            byType.GetValueOrDefault(InjectionType.EncodingObfuscation),
            byType.GetValueOrDefault(InjectionType.Other));
    }

    /// <summary>
    /// 断言请求中不包含注入
    /// </summary>
    /// <exception cref="AssertException">当检测到注入时抛出</exception>
    public void AssertNoInjection(ChatCompletionRequest? request, string? message = null)
    {
        var result = DetectInjection(request);
        if (result.HasInjection)
        {
            var errorMessage = message ??
                $"检测到提示词注入: 类型={result.Type}, 关键词={string.Join(", ", result.MatchedKeywords)}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言请求中包含特定类型的注入
    /// </summary>
    /// <exception cref="AssertException">当未检测到指定类型注入时抛出</exception>
    public void AssertHasInjectionType(
        ChatCompletionRequest? request,
        InjectionType expectedType,
        string? message = null)
    {
        var result = DetectInjection(request);
        if (!result.HasInjection || result.Type != expectedType)
        {
            var errorMessage = message ??
                $"期望检测到类型为 {expectedType} 的注入，但实际: {result.Type}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言注入内容已正确添加到系统提示词
    /// </summary>
    /// <exception cref="AssertException">当注入内容未找到时抛出</exception>
    public void AssertInjectionPresent(
        ChatCompletionRequest? request,
        string? expectedContent = null,
        string? message = null)
    {
        if (!ValidateInjectionPresent(request, expectedContent))
        {
            var errorMessage = message ??
                (expectedContent is null
                    ? "未在系统提示词中找到注入标记"
                    : $"未在系统提示词中找到预期的注入内容: '{expectedContent}'");
            throw new AssertException(errorMessage);
        }
    }
}

/// <summary>
/// 注入统计信息
/// </summary>
public sealed class InjectionStatistics
{
    public int TotalInjections { get; }
    public int SystemPromptOverrideCount { get; }
    public int RolePlayInjectionCount { get; }
    public int IgnoreInstructionsCount { get; }
    public int DelimiterInjectionCount { get; }
    public int EncodingObfuscationCount { get; }
    public int OtherCount { get; }

    public InjectionStatistics(
        int total,
        int systemPromptOverride,
        int rolePlayInjection,
        int ignoreInstructions,
        int delimiterInjection,
        int encodingObfuscation,
        int other)
    {
        TotalInjections = total;
        SystemPromptOverrideCount = systemPromptOverride;
        RolePlayInjectionCount = rolePlayInjection;
        IgnoreInstructionsCount = ignoreInstructions;
        DelimiterInjectionCount = delimiterInjection;
        EncodingObfuscationCount = encodingObfuscation;
        OtherCount = other;
    }

    public override string ToString()
    {
        return $"总注入数: {TotalInjections}, 系统覆盖: {SystemPromptOverrideCount}, " +
               $"角色扮演: {RolePlayInjectionCount}, 指令忽略: {IgnoreInstructionsCount}, " +
               $"分隔符: {DelimiterInjectionCount}, 编码混淆: {EncodingObfuscationCount}, 其他: {OtherCount}";
    }
}
