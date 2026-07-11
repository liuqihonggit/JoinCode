
namespace MockServer.E2E.Tests.Triggers;

/// <summary>
/// 工具提示词验证结果
/// </summary>
public sealed class ToolPromptValidationResult
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
    /// 找到的工具名称列表
    /// </summary>
    public IReadOnlyList<string> FoundTools { get; }

    /// <summary>
    /// 未找到的工具名称列表
    /// </summary>
    public IReadOnlyList<string> MissingTools { get; }

    /// <summary>
    /// 工具详情
    /// </summary>
    public IReadOnlyDictionary<string, ToolInfo> ToolDetails { get; }

    public ToolPromptValidationResult(
        bool isValid,
        string message,
        IReadOnlyList<string>? foundTools = null,
        IReadOnlyList<string>? missingTools = null,
        IReadOnlyDictionary<string, ToolInfo>? toolDetails = null)
    {
        IsValid = isValid;
        Message = message;
        FoundTools = foundTools ?? Array.Empty<string>();
        MissingTools = missingTools ?? Array.Empty<string>();
        ToolDetails = toolDetails ?? new Dictionary<string, ToolInfo>();
    }

    /// <summary>
    /// 成功的验证结果
    /// </summary>
    public static ToolPromptValidationResult Success(
        IReadOnlyList<string> foundTools,
        IReadOnlyDictionary<string, ToolInfo>? toolDetails = null)
    {
        return new ToolPromptValidationResult(true, "工具提示词验证通过", foundTools, Array.Empty<string>(), toolDetails);
    }

    /// <summary>
    /// 失败的验证结果
    /// </summary>
    public static ToolPromptValidationResult Failure(
        string message,
        IReadOnlyList<string>? missingTools = null,
        IReadOnlyList<string>? foundTools = null)
    {
        return new ToolPromptValidationResult(false, message, foundTools ?? Array.Empty<string>(), missingTools);
    }
}

/// <summary>
/// 工具信息
/// </summary>
public sealed class ToolInfo
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 工具描述
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// 参数列表
    /// </summary>
    public IReadOnlyList<ToolParameter> Parameters { get; }

    /// <summary>
    /// 是否在系统提示词中找到
    /// </summary>
    public bool IsFound { get; }

    /// <summary>
    /// 在系统提示词中的位置
    /// </summary>
    public int? Position { get; }

    public ToolInfo(
        string name,
        string? description = null,
        IReadOnlyList<ToolParameter>? parameters = null,
        bool isFound = false,
        int? position = null)
    {
        Name = name;
        Description = description;
        Parameters = parameters ?? Array.Empty<ToolParameter>();
        IsFound = isFound;
        Position = position;
    }
}

/// <summary>
/// 工具参数
/// </summary>
public sealed class ToolParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 参数类型
    /// </summary>
    public string? Type { get; }

    /// <summary>
    /// 是否必需
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// 参数描述
    /// </summary>
    public string? Description { get; }

    public ToolParameter(string name, string? type = null, bool isRequired = false, string? description = null)
    {
        Name = name;
        Type = type;
        IsRequired = isRequired;
        Description = description;
    }
}

/// <summary>
/// 工具提示词验证器
/// 验证工具描述是否正确包含在系统提示词中
/// </summary>
public sealed class ToolPromptValidator
{
    // 工具章节标记
    private static readonly string[] ToolSectionMarkers = new[]
    {
        "## Tools", "## 工具", "### Available Tools", "### 可用工具",
        "Tools:", "工具:", "**Tools**", "**工具**",
        "Available functions:", "可用函数:", "Function definitions:"
    };

    // 工具名称前缀模式
    private static readonly string[] ToolNamePrefixes = new[]
    {
        "### ", "## ", "**", "- ", "• ", "→ ", "=> "
    };

    /// <summary>
    /// 验证工具提示词
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="expectedToolNames">预期包含的工具名称列表</param>
    /// <returns>验证结果</returns>
    public ToolPromptValidationResult Validate(
        ChatCompletionRequest? request,
        IEnumerable<string>? expectedToolNames = null)
    {
        var systemPrompt = GetSystemPrompt(request);

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return ToolPromptValidationResult.Failure("未找到系统提示词");
        }

        var toolSection = ExtractToolSection(systemPrompt);

        if (string.IsNullOrWhiteSpace(toolSection))
        {
            return ToolPromptValidationResult.Failure("未在系统提示词中找到工具描述部分");
        }

        if (expectedToolNames is null)
        {
            var foundTools = ExtractToolNames(toolSection);
            return ToolPromptValidationResult.Success(foundTools);
        }

        var expectedList = expectedToolNames.ToList();
        var foundList = new List<string>();
        var missingList = new List<string>();
        var toolDetails = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in expectedList)
        {
            var toolInfo = FindToolInSection(toolSection, toolName);
            toolDetails[toolName] = toolInfo;

            if (toolInfo.IsFound)
            {
                foundList.Add(toolName);
            }
            else
            {
                missingList.Add(toolName);
            }
        }

        if (missingList.Count > 0)
        {
            return ToolPromptValidationResult.Failure(
                $"未找到 {missingList.Count} 个工具: {string.Join(", ", missingList)}",
                missingList,
                foundList);
        }

        return ToolPromptValidationResult.Success(foundList, toolDetails);
    }

    /// <summary>
    /// 按工具名称验证
    /// </summary>
    /// <param name="request">聊天完成请求</param>
    /// <param name="toolName">工具名称</param>
    /// <returns>工具信息，如果未找到则返回 null</returns>
    public ToolInfo? ValidateByName(ChatCompletionRequest? request, string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var systemPrompt = GetSystemPrompt(request);
        if (string.IsNullOrWhiteSpace(systemPrompt))
            return null;

        var toolSection = ExtractToolSection(systemPrompt);
        if (string.IsNullOrWhiteSpace(toolSection))
            return null;

        return FindToolInSection(toolSection, toolName);
    }

    /// <summary>
    /// 验证系统提示词是否包含指定工具
    /// </summary>
    public bool ContainsTool(ChatCompletionRequest? request, string toolName)
    {
        return ValidateByName(request, toolName)?.IsFound ?? false;
    }

    /// <summary>
    /// 验证系统提示词是否包含所有指定工具
    /// </summary>
    public bool ContainsAllTools(ChatCompletionRequest? request, IEnumerable<string> toolNames)
    {
        return toolNames.All(name => ContainsTool(request, name));
    }

    /// <summary>
    /// 验证系统提示词是否包含任意指定工具
    /// </summary>
    public bool ContainsAnyTool(ChatCompletionRequest? request, IEnumerable<string> toolNames)
    {
        return toolNames.Any(name => ContainsTool(request, name));
    }

    /// <summary>
    /// 获取系统提示词中所有工具名称
    /// </summary>
    public IReadOnlyList<string> GetAllToolNames(ChatCompletionRequest? request)
    {
        var systemPrompt = GetSystemPrompt(request);
        if (string.IsNullOrWhiteSpace(systemPrompt))
            return Array.Empty<string>();

        var toolSection = ExtractToolSection(systemPrompt);
        return string.IsNullOrWhiteSpace(toolSection)
            ? Array.Empty<string>()
            : ExtractToolNames(toolSection);
    }

    /// <summary>
    /// 获取工具数量
    /// </summary>
    public int GetToolCount(ChatCompletionRequest? request)
    {
        return GetAllToolNames(request).Count;
    }

    /// <summary>
    /// 验证工具描述是否包含特定内容
    /// </summary>
    public bool ToolDescriptionContains(
        ChatCompletionRequest? request,
        string toolName,
        string expectedContent,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var toolInfo = ValidateByName(request, toolName);
        if (toolInfo?.Description is null)
            return false;

        return toolInfo.Description.Contains(expectedContent, comparison);
    }

    /// <summary>
    /// 验证工具是否包含指定参数
    /// </summary>
    public bool ToolHasParameter(
        ChatCompletionRequest? request,
        string toolName,
        string parameterName)
    {
        var toolInfo = ValidateByName(request, toolName);
        if (toolInfo?.Parameters is null)
            return false;

        return toolInfo.Parameters.Any(p =>
            p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 断言系统提示词包含指定工具
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertContainsTool(ChatCompletionRequest? request, string toolName, string? message = null)
    {
        if (!ContainsTool(request, toolName))
        {
            var availableTools = string.Join(", ", GetAllToolNames(request));
            var errorMessage = message ??
                $"系统提示词应包含工具 '{toolName}'，但找到的工具: {availableTools}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言系统提示词包含所有指定工具
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertContainsAllTools(
        ChatCompletionRequest? request,
        IEnumerable<string> toolNames,
        string? message = null)
    {
        var missingTools = toolNames.Where(name => !ContainsTool(request, name)).ToList();
        if (missingTools.Count > 0)
        {
            var errorMessage = message ??
                $"系统提示词缺少以下工具: {string.Join(", ", missingTools)}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言工具描述包含指定内容
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertToolDescriptionContains(
        ChatCompletionRequest? request,
        string toolName,
        string expectedContent,
        string? message = null)
    {
        if (!ToolDescriptionContains(request, toolName, expectedContent))
        {
            var toolInfo = ValidateByName(request, toolName);
            var errorMessage = message ??
                $"工具 '{toolName}' 的描述应包含 '{expectedContent}'，但实际描述: {toolInfo?.Description ?? "(未找到)"}";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 断言工具包含指定参数
    /// </summary>
    /// <exception cref="AssertException">当断言失败时抛出</exception>
    public void AssertToolHasParameter(
        ChatCompletionRequest? request,
        string toolName,
        string parameterName,
        string? message = null)
    {
        if (!ToolHasParameter(request, toolName, parameterName))
        {
            var errorMessage = message ??
                $"工具 '{toolName}' 应包含参数 '{parameterName}'";
            throw new AssertException(errorMessage);
        }
    }

    /// <summary>
    /// 获取系统提示词
    /// </summary>
    private static string? GetSystemPrompt(ChatCompletionRequest? request)
    {
        if (request?.Messages is null)
            return null;

        var systemMessages = request.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList();

        return systemMessages.Count > 0 ? string.Join("\n\n", systemMessages) : null;
    }

    /// <summary>
    /// 从系统提示词中提取工具章节
    /// </summary>
    private static string? ExtractToolSection(string systemPrompt)
    {
        foreach (var marker in ToolSectionMarkers)
        {
            var startIndex = systemPrompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex >= 0)
            {
                // 查找章节结束位置（下一个 ## 或 ### 或文件末尾）
                var searchStart = startIndex + marker.Length;
                var endIndex = systemPrompt.IndexOf("\n## ", searchStart, StringComparison.Ordinal);

                if (endIndex < 0)
                    endIndex = systemPrompt.IndexOf("\n### ", searchStart, StringComparison.Ordinal);

                if (endIndex < 0)
                    endIndex = systemPrompt.Length;

                return systemPrompt[startIndex..endIndex].Trim();
            }
        }

        // 如果没有找到明确的工具章节标记，尝试查找工具相关关键词
        var toolKeywords = new[] { "function", "tool", "api", "调用" };
        foreach (var keyword in toolKeywords)
        {
            if (systemPrompt.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                // 返回包含工具关键词的部分
                var keywordIndex = systemPrompt.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                var sectionStart = systemPrompt.LastIndexOf("\n\n", keywordIndex, StringComparison.Ordinal);
                if (sectionStart < 0) sectionStart = 0;

                var sectionEnd = systemPrompt.IndexOf("\n\n", keywordIndex, StringComparison.Ordinal);
                if (sectionEnd < 0) sectionEnd = systemPrompt.Length;

                return systemPrompt[sectionStart..sectionEnd].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// 从工具章节中提取工具名称列表
    /// </summary>
    private static IReadOnlyList<string> ExtractToolNames(string toolSection)
    {
        var names = new List<string>();
        var lines = toolSection.Split('\n');

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // 尝试匹配工具名称前缀
            foreach (var prefix in ToolNamePrefixes)
            {
                if (trimmedLine.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var namePart = trimmedLine[prefix.Length..].Trim();
                    // 提取名称（到第一个空格或特殊字符为止）
                    var nameEnd = namePart.IndexOfAny(new[] { ' ', '(', ':', '-', '\n' });
                    var name = nameEnd > 0 ? namePart[..nameEnd].Trim() : namePart;

                    if (!string.IsNullOrWhiteSpace(name) && name.Length > 1)
                    {
                        names.Add(name);
                    }
                    break;
                }
            }
        }

        return names;
    }

    /// <summary>
    /// 在工具章节中查找特定工具
    /// </summary>
    private static ToolInfo FindToolInSection(string toolSection, string toolName)
    {
        var lines = toolSection.Split('\n');
        var inTargetTool = false;
        var description = new List<string>();
        var parameters = new List<ToolParameter>();
        var position = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.Trim();

            // 检测工具标题行
            if (trimmedLine.Contains(toolName, StringComparison.OrdinalIgnoreCase))
            {
                var isToolHeader = ToolNamePrefixes.Any(prefix =>
                    trimmedLine.StartsWith(prefix + toolName, StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith(prefix, StringComparison.Ordinal) &&
                    trimmedLine.Contains(toolName, StringComparison.OrdinalIgnoreCase));

                if (isToolHeader || trimmedLine.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                {
                    inTargetTool = true;
                    position = i;
                    description.Add(trimmedLine);
                    continue;
                }
            }

            // 检测下一个工具开始
            if (inTargetTool)
            {
                var isNextTool = ToolNamePrefixes.Any(prefix =>
                    trimmedLine.StartsWith(prefix, StringComparison.Ordinal) &&
                    !trimmedLine.Contains(toolName, StringComparison.OrdinalIgnoreCase));

                if (isNextTool || trimmedLine.StartsWith("## ") || trimmedLine.StartsWith("### "))
                {
                    break;
                }

                description.Add(line);

                // 尝试解析参数
                var param = TryParseParameter(line);
                if (param is not null)
                {
                    parameters.Add(param);
                }
            }
        }

        var fullDescription = string.Join("\n", description).Trim();

        return new ToolInfo(
            toolName,
            fullDescription,
            parameters,
            inTargetTool,
            position >= 0 ? position : null);
    }

    /// <summary>
    /// 尝试从行文本解析参数
    /// </summary>
    private static ToolParameter? TryParseParameter(string line)
    {
        var trimmed = line.Trim();

        // 匹配常见的参数格式:
        // - paramName: description
        // - paramName (type): description
        // * paramName - description
        // paramName: type - description

        if (trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("•"))
        {
            var content = trimmed[1..].Trim();
            var colonIndex = content.IndexOf(':');
            var parenIndex = content.IndexOf('(');

            if (colonIndex > 0 || parenIndex > 0)
            {
                var nameEnd = colonIndex > 0 && (parenIndex < 0 || colonIndex < parenIndex)
                    ? colonIndex
                    : parenIndex > 0 ? parenIndex : content.IndexOf(' ');

                if (nameEnd > 0)
                {
                    var name = content[..nameEnd].Trim();
                    var rest = content[nameEnd..].Trim();

                    // 提取类型
                    string? type = null;
                    if (rest.StartsWith("(") && rest.Contains(')'))
                    {
                        var typeEnd = rest.IndexOf(')');
                        type = rest[1..typeEnd].Trim();
                        rest = rest[(typeEnd + 1)..].Trim();
                    }

                    // 清理描述
                    var description = rest.TrimStart(':', '-', ' ').Trim();

                    return new ToolParameter(name, type, false, description);
                }
            }
        }

        return null;
    }
}
