
namespace Core.Agents;

/// <summary>
/// 验证 Agent - 验证代码正确性
/// </summary>
public sealed class VerificationAgent : BuiltInAgentBase
{
    public override string Name => "VerificationAgent";
    public override string Description => "验证代码的正确性、质量和安全性，识别潜在问题";
    public override BuiltInAgentType AgentType => BuiltInAgentType.Verification;
    public override string SystemPrompt => AgentPrompts.VerificationAgentSystemPrompt;

    public VerificationAgent(
        IChatClient kernel,
        IClockService clock,
        ILogger<VerificationAgent>? logger = null)
        : base(kernel, clock, logger)
    {
    }

    /// <summary>
    /// 验证代码
    /// </summary>
    public async Task<VerificationResult> VerifyCodeAsync(
        VerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildVerificationPrompt(request);
        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new VerificationResult
        {
            Success = true,
            VerificationId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 进行代码审查
    /// </summary>
    public async Task<VerificationResult> CodeReviewAsync(
        string code,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请对以下代码进行详细的代码审查：

{(string.IsNullOrWhiteSpace(language) ? "" : $"## 编程语言\n{language}\n")}
## 代码
```
{code}
```

请从以下维度进行审查：
1. 代码风格和可读性
2. 设计模式和架构
3. 性能和效率
4. 安全性和潜在漏洞
5. 测试覆盖建议
6. 文档和注释

对于每个发现的问题，请说明：
- 严重程度（严重/警告/建议）
- 问题描述
- 改进建议
- 参考示例
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new VerificationResult
        {
            Success = true,
            VerificationId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 验证特定方面
    /// </summary>
    public async Task<VerificationResult> VerifyAspectAsync(
        string code,
        VerificationAspect aspect,
        CancellationToken cancellationToken = default)
    {
        var aspectDescription = aspect switch
        {
            VerificationAspect.Security => "安全性",
            VerificationAspect.Performance => "性能",
            VerificationAspect.Maintainability => "可维护性",
            VerificationAspect.Correctness => "正确性",
            VerificationAspect.Style => "代码风格",
            _ => "综合"
        };

        var prompt = $"""
请专注于验证以下代码的{aspectDescription}方面：

## 代码
```
{code}
```

请重点关注：
{GetAspectFocus(aspect)}

请提供具体的发现和建议。
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new VerificationResult
        {
            Success = true,
            VerificationId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    private static string BuildVerificationPrompt(VerificationRequest request)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请验证以下代码：");
        prompt.AppendLine();
        prompt.AppendLine("## 代码");
        prompt.AppendLine("```");
        prompt.AppendLine(request.Code);
        prompt.AppendLine("```");

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 编程语言\n{request.Language}");
        }

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 上下文\n{request.Context}");
        }

        if (request.Aspects != null && request.Aspects.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 重点验证方面");
            foreach (var aspect in request.Aspects)
            {
                prompt.AppendLine($"- {aspect switch
                {
                    VerificationAspect.Security => "安全性",
                    VerificationAspect.Performance => "性能",
                    VerificationAspect.Maintainability => "可维护性",
                    VerificationAspect.Correctness => "正确性",
                    VerificationAspect.Style => "代码风格",
                    _ => aspect.ToString()
                }}");
            }
        }

        if (request.Requirements != null && request.Requirements.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 需要满足的要求");
            foreach (var requirement in request.Requirements)
            {
                prompt.AppendLine($"- {requirement}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("请按照系统提示词中指定的格式输出验证结果。");

        return prompt.ToString();
    }

    private static string GetAspectFocus(VerificationAspect aspect) => aspect switch
    {
        VerificationAspect.Security => """
            - 输入验证和注入攻击防护
            - 敏感数据处理
            - 认证和授权
            - 加密使用
            - 已知漏洞模式
            """,
        VerificationAspect.Performance => """
            - 算法复杂度
            - 资源使用效率
            - 缓存策略
            - 异步处理
            - 内存管理
            """,
        VerificationAspect.Maintainability => """
            - 代码组织和模块化
            - 命名规范
            - 注释和文档
            - 重复代码
            - 依赖管理
            """,
        VerificationAspect.Correctness => """
            - 逻辑正确性
            - 边界条件处理
            - 错误处理
            - 并发安全
            - 类型安全
            """,
        VerificationAspect.Style => """
            - 代码格式一致性
            - 命名约定
            - 代码结构
            - 最佳实践遵循
            - 团队规范
            """,
        _ => "全面检查所有方面"
    };

    protected override float GetTemperature() => 0.3f;
}

/// <summary>
/// 验证请求
/// </summary>
public sealed record VerificationRequest
{
    public required string Code { get; init; }
    public string? Language { get; init; }
    public string? Context { get; init; }
    public List<VerificationAspect>? Aspects { get; init; }
    public List<string>? Requirements { get; init; }
}

/// <summary>
/// 验证方面
/// </summary>
public enum VerificationAspect
{
    [EnumValue("security")] Security,
    [EnumValue("performance")] Performance,
    [EnumValue("maintainability")] Maintainability,
    [EnumValue("correctness")] Correctness,
    [EnumValue("style")] Style
}

/// <summary>
/// 验证结果
/// </summary>
public sealed record VerificationResult
{
    public required bool Success { get; init; }
    public string? VerificationId { get; init; }
    public string? Content { get; init; }
    public long ExecutionTimeMs { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
}
