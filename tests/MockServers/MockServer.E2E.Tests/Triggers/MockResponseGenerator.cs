
namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 模拟响应生成器
/// 根据测试触发器类型生成不同的响应内容
/// </summary>
public sealed class MockResponseGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    /// <summary>
    /// 根据触发器生成响应
    /// </summary>
    /// <param name="trigger">测试触发器</param>
    /// <param name="request">原始请求</param>
    /// <returns>生成的响应内容</returns>
    public string GenerateResponse(TestTrigger trigger, ChatCompletionRequest? request)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Type switch
        {
            TestTriggerType.SystemPrompt => GenerateSystemPromptResponse(request),
            TestTriggerType.UserPrompt => GenerateUserPromptResponse(request),
            TestTriggerType.ToolPrompt => GenerateToolPromptResponse(request, trigger.Parameter),
            TestTriggerType.UserInjection => GenerateUserInjectionResponse(request),
            TestTriggerType.ApiKey => GenerateApiKeyResponse(trigger.Parameter),
            TestTriggerType.FullRequest => GenerateFullRequestResponse(request),
            _ => GenerateDefaultResponse(trigger)
        };
    }

    /// <summary>
    /// 生成系统提示词响应 - 返回系统提示词摘要
    /// </summary>
    private static string GenerateSystemPromptResponse(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return "[系统提示词验证] 请求为空或没有消息";

        var systemMessages = request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();

        if (systemMessages.Count == 0)
            return "[系统提示词验证] 未找到系统提示词";

        var sb = new StringBuilder();
        sb.AppendLine("=== 系统提示词验证结果 ===");
        sb.AppendLine($"找到 {systemMessages.Count} 条系统提示词:");
        sb.AppendLine();

        for (var i = 0; i < systemMessages.Count; i++)
        {
            var content = systemMessages[i];
            var preview = content.Length > 200 ? content[..200] + "..." : content;
            var lineCount = content.Split('\n').Length;
            var charCount = content.Length;

            sb.AppendLine($"--- 提示词 {i + 1} ---");
            sb.AppendLine($"行数: {lineCount}");
            sb.AppendLine($"字符数: {charCount}");
            sb.AppendLine($"预览:");
            sb.AppendLine(preview);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成用户提示词响应 - 返回用户提示词摘要
    /// </summary>
    private static string GenerateUserPromptResponse(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return "[用户提示词验证] 请求为空或没有消息";

        var userMessages = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList();

        if (userMessages.Count == 0)
            return "[用户提示词验证] 未找到用户提示词";

        var sb = new StringBuilder();
        sb.AppendLine("=== 用户提示词验证结果 ===");
        sb.AppendLine($"找到 {userMessages.Count} 条用户消息:");
        sb.AppendLine();

        for (var i = 0; i < userMessages.Count; i++)
        {
            var content = userMessages[i];
            var preview = content.Length > 300 ? content[..300] + "..." : content;
            var lineCount = content.Split('\n').Length;
            var charCount = content.Length;

            sb.AppendLine($"--- 用户消息 {i + 1} ---");
            sb.AppendLine($"行数: {lineCount}");
            sb.AppendLine($"字符数: {charCount}");
            sb.AppendLine($"完整内容:");
            sb.AppendLine(preview);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成工具提示词响应 - 返回工具提示词内容
    /// </summary>
    private static string GenerateToolPromptResponse(ChatCompletionRequest? request, string? toolName)
    {
        if (request?.Messages is null)
            return "[工具提示词验证] 请求为空或没有消息";

        var systemPrompt = request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(systemPrompt))
            return "[工具提示词验证] 未找到系统提示词";

        var sb = new StringBuilder();
        sb.AppendLine("=== 工具提示词验证结果 ===");

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            sb.AppendLine($"查找工具: {toolName}");
            var toolSection = ExtractToolSection(systemPrompt, toolName);

            if (string.IsNullOrWhiteSpace(toolSection))
            {
                sb.AppendLine($"未找到工具 '{toolName}' 的描述");
            }
            else
            {
                sb.AppendLine($"找到工具 '{toolName}' 的描述:");
                sb.AppendLine(toolSection);
            }
        }
        else
        {
            sb.AppendLine("系统提示词中的工具相关部分:");
            var toolsSection = ExtractAllToolsSection(systemPrompt);
            sb.AppendLine(string.IsNullOrWhiteSpace(toolsSection) ? "未找到工具描述部分" : toolsSection);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成用户提示词注入响应 - 返回注入检测确认
    /// </summary>
    private static string GenerateUserInjectionResponse(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return "[注入验证] 请求为空或没有消息";

        var sb = new StringBuilder();
        sb.AppendLine("=== 用户提示词注入验证结果 ===");

        var userMessages = request.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList();

        var systemMessages = request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();

        sb.AppendLine($"用户消息数: {userMessages.Count}");
        sb.AppendLine($"系统消息数: {systemMessages.Count}");
        sb.AppendLine();

        // 检测是否有注入关键词
        var injectionKeywords = new[] { "[系统提示:", "[注入:", "[自动注入]", "INJECTION", "SYSTEM_OVERRIDE" };
        var hasInjectionMarkers = userMessages.Any(msg =>
            injectionKeywords.Any(keyword => msg.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        sb.AppendLine($"检测到注入标记: {(hasInjectionMarkers ? "是" : "否")}");
        sb.AppendLine();

        if (hasInjectionMarkers)
        {
            sb.AppendLine("包含注入标记的用户消息:");
            foreach (var msg in userMessages.Where(m =>
                injectionKeywords.Any(k => m.Contains(k, StringComparison.OrdinalIgnoreCase))))
            {
                sb.AppendLine($"  - {msg[..Math.Min(100, msg.Length)]}...");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成 API Key 验证响应
    /// </summary>
    private static string GenerateApiKeyResponse(string? apiKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== API Key 验证结果 ===");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            sb.AppendLine("状态: 未提供 API Key");
            sb.AppendLine("结果: 跳过验证");
        }
        else
        {
            var maskedKey = apiKey.Length > 8
                ? $"{apiKey[..4]}...{apiKey[^4..]}"
                : "****";

            var isValidFormat = apiKey.StartsWith("sk-") || apiKey.StartsWith("Bearer ") || apiKey.Length >= 32;

            sb.AppendLine($"提供的 API Key: {maskedKey}");
            sb.AppendLine($"格式验证: {(isValidFormat ? "通过" : "失败")}");
            sb.AppendLine($"长度: {apiKey.Length} 字符");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成完整请求响应 - 返回完整请求 JSON
    /// </summary>
    private static string GenerateFullRequestResponse(ChatCompletionRequest? request)
    {
        if (request is null)
            return "[完整请求] 请求为空";

        var sb = new StringBuilder();
        sb.AppendLine("=== 完整请求 JSON ===");
        sb.AppendLine();

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            sb.AppendLine(json);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"序列化失败: {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("=== 请求统计 ===");
        sb.AppendLine($"模型: {request.Model}");
        sb.AppendLine($"消息数: {request.Messages?.Count ?? 0}");
        sb.AppendLine($"流式: {request.Stream}");
        sb.AppendLine($"温度: {request.Temperature}");
        sb.AppendLine($"最大 Token: {request.MaxTokens}");

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 生成默认响应
    /// </summary>
    private static string GenerateDefaultResponse(TestTrigger trigger)
    {
        return $"[测试触发器] 类型: {trigger.Type}, 参数: {trigger.Parameter ?? "(无)"}";
    }

    /// <summary>
    /// 从系统提示词中提取特定工具的章节
    /// </summary>
    private static string? ExtractToolSection(string systemPrompt, string toolName)
    {
        var lines = systemPrompt.Split('\n');
        var sb = new StringBuilder();
        var inTargetTool = false;

        foreach (var line in lines)
        {
            // 检测工具标题行
            if (line.Contains(toolName, StringComparison.OrdinalIgnoreCase) &&
                (line.StartsWith("##") || line.StartsWith("**") || line.Contains("tool")))
            {
                inTargetTool = true;
                sb.AppendLine(line);
                continue;
            }

            // 检测下一个工具开始（假设工具之间有分隔）
            if (inTargetTool && (line.StartsWith("##") || line.StartsWith("---")))
            {
                break;
            }

            if (inTargetTool)
            {
                sb.AppendLine(line);
            }
        }

        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    /// <summary>
    /// 提取系统提示词中的所有工具相关部分
    /// </summary>
    private static string? ExtractAllToolsSection(string systemPrompt)
    {
        var toolSectionMarkers = new[]
        {
            "## Tools", "## 工具", "### Available Tools", "### 可用工具",
            "Tools:", "工具:", "**Tools**", "**工具**"
        };

        foreach (var marker in toolSectionMarkers)
        {
            var index = systemPrompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // 提取从标记开始的一段内容
                var endIndex = systemPrompt.IndexOf("\n## ", index + marker.Length, StringComparison.Ordinal);
                if (endIndex < 0) endIndex = systemPrompt.Length;

                var section = systemPrompt[index..endIndex];
                return section.Length > 2000 ? section[..2000] + "..." : section;
            }
        }

        return null;
    }
}
