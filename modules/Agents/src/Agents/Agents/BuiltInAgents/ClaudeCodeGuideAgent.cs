
namespace Core.Agents;

/// <summary>
/// Claude Code 引导 Agent - 提供使用指导
/// </summary>
public sealed class ClaudeCodeGuideAgent : BuiltInAgentBase
{
    public override string Name => "ClaudeCodeGuideAgent";
    public override string Description => "帮助用户了解和使用 Claude Code 的各种功能和最佳实践";
    public override BuiltInAgentType AgentType => BuiltInAgentType.ClaudeCodeGuide;
    public override string SystemPrompt => AgentPrompts.ClaudeCodeGuideAgentSystemPrompt;

    public ClaudeCodeGuideAgent(
        IChatClient kernel,
        IClockService clock,
        ILogger<ClaudeCodeGuideAgent>? logger = null)
        : base(kernel, clock, logger)
    {
    }

    /// <summary>
    /// 提供功能介绍
    /// </summary>
    public async Task<GuideResult> IntroduceFeatureAsync(
        string featureName,
        CancellationToken cancellationToken = default)
    {
        var featureInfo = GetFeatureInfo(featureName);
        var prompt = $"""
请详细介绍以下 Claude Code 功能：

## 功能名称
{featureName}

{(string.IsNullOrWhiteSpace(featureInfo) ? "" : $"## 功能信息\n{featureInfo}\n")}

请提供：
1. 功能概述
2. 使用场景
3. 详细使用步骤
4. 实际示例
5. 注意事项
6. 常见问题
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GuideResult
        {
            Success = true,
            GuideId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 回答使用问题
    /// </summary>
    public async Task<GuideResult> AnswerUsageQuestionAsync(
        string question,
        UserExperienceLevel? experienceLevel = null,
        CancellationToken cancellationToken = default)
    {
        var level = experienceLevel ?? UserExperienceLevel.Intermediate;
        var prompt = $"""
请回答以下关于 Claude Code 使用的问题：

## 用户问题
{question}

## 用户经验水平
{GetExperienceLevelDescription(level)}

请根据用户的经验水平调整回答的深度：
- 初学者：提供详细的基础解释和逐步指导
- 中级：提供实用技巧和常见用法
- 高级：提供高级功能和优化建议

回答应包括：
1. 直接回答
2. 相关背景知识（如需要）
3. 具体示例
4. 相关功能的链接或参考
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GuideResult
        {
            Success = true,
            GuideId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 提供工作流程指导
    /// </summary>
    public async Task<GuideResult> ProvideWorkflowGuidanceAsync(
        WorkflowType workflowType,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请提供以下工作流程的详细指导：

## 工作流程类型
{GetWorkflowDescription(workflowType)}

请提供：
1. 工作流程概述
2. 前置准备
3. 详细步骤
4. 每个步骤的说明和示例
5. 最佳实践
6. 常见陷阱和避免方法
7. 相关工具和功能
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GuideResult
        {
            Success = true,
            GuideId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 提供故障排除帮助
    /// </summary>
    public async Task<GuideResult> TroubleshootAsync(
        string issue,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请帮助解决以下 Claude Code 使用问题：

## 问题描述
{issue}

{(string.IsNullOrWhiteSpace(context) ? "" : $"## 上下文信息\n{context}\n")}

请提供：
1. 问题诊断
2. 可能的原因
3. 解决方案（多种）
4. 逐步排查步骤
5. 预防措施
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GuideResult
        {
            Success = true,
            GuideId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 生成快速入门指南
    /// </summary>
    public async Task<GuideResult> GenerateQuickStartGuideAsync(
        QuickStartTopic topic,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请生成以下主题的快速入门指南：

## 主题
{GetQuickStartTopicDescription(topic)}

指南应包括：
1. 简介（100字以内）
2. 前提条件
3. 5分钟快速开始
4. 常用命令/操作速查表
5. 下一步学习建议
6. 相关资源链接

请保持简洁实用，适合快速上手。
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GuideResult
        {
            Success = true,
            GuideId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    private static string GetFeatureInfo(string featureName) => featureName.ToLowerInvariant() switch
    {
        "agent" or "agent mode" => """
            Agent 模式是 Claude Code 的核心功能，可以自动规划和执行复杂任务。
            特点：自动工具调用、任务分解、上下文管理
            """,
        "plan" or "plan mode" => """
            Plan 模式用于制定详细的任务执行计划。
            特点：结构化规划、步骤分解、依赖分析
            """,
        "spec" or "spec mode" => """
            Spec 模式用于编写详细的技术规范文档。
            特点：模板化、结构化、可追踪
            """,
        "tools" or "tool" => """
            Claude Code 提供多种工具用于不同任务。
            包括：文件操作、代码执行、搜索、Web 访问等
            """,
        "bash" or "shell" => """
            Bash 工具用于执行命令行操作。
            特点：安全检查、权限管理、沙箱支持
            """,
        "ask" or "ask_user" => """
            AskUser 工具用于向用户提问获取信息。
            支持：单选、多选、开放式问题
            """,
        _ => string.Empty
    };

    private static string GetExperienceLevelDescription(UserExperienceLevel level) => level switch
    {
        UserExperienceLevel.Beginner => "初学者 - 刚开始使用 Claude Code",
        UserExperienceLevel.Intermediate => "中级 - 有基本使用经验",
        UserExperienceLevel.Advanced => "高级 - 熟练使用各种功能",
        _ => "中级"
    };

    private static string GetWorkflowDescription(WorkflowType workflowType) => workflowType switch
    {
        WorkflowType.NewFeature => "新功能开发流程",
        WorkflowType.BugFix => "Bug 修复流程",
        WorkflowType.CodeReview => "代码审查流程",
        WorkflowType.Refactoring => "代码重构流程",
        WorkflowType.Documentation => "文档编写流程",
        WorkflowType.Testing => "测试流程",
        WorkflowType.Deployment => "部署流程",
        _ => "通用工作流程"
    };

    private static string GetQuickStartTopicDescription(QuickStartTopic topic) => topic switch
    {
        QuickStartTopic.FirstTime => "首次使用 Claude Code",
        QuickStartTopic.Coding => "使用 Claude Code 编写代码",
        QuickStartTopic.Debugging => "使用 Claude Code 调试问题",
        QuickStartTopic.Refactoring => "使用 Claude Code 重构代码",
        QuickStartTopic.Learning => "学习 Claude Code 高级功能",
        _ => "Claude Code 快速入门"
    };

    protected override float GetTemperature() => 0.6f;
}

/// <summary>
/// 用户经验水平
/// </summary>
public enum UserExperienceLevel
{
    [EnumValue("beginner")] Beginner,
    [EnumValue("intermediate")] Intermediate,
    [EnumValue("advanced")] Advanced
}

/// <summary>
/// 工作流程类型
/// </summary>
public enum WorkflowType
{
    [EnumValue("newFeature")] NewFeature,
    [EnumValue("bugFix")] BugFix,
    [EnumValue("codeReview")] CodeReview,
    [EnumValue("refactoring")] Refactoring,
    [EnumValue("documentation")] Documentation,
    [EnumValue("testing")] Testing,
    [EnumValue("deployment")] Deployment
}

/// <summary>
/// 快速入门主题
/// </summary>
public enum QuickStartTopic
{
    [EnumValue("firstTime")] FirstTime,
    [EnumValue("coding")] Coding,
    [EnumValue("debugging")] Debugging,
    [EnumValue("refactoring")] Refactoring,
    [EnumValue("learning")] Learning
}

/// <summary>
/// 引导结果
/// </summary>
public sealed record GuideResult
{
    public required bool Success { get; init; }
    public string? GuideId { get; init; }
    public string? Content { get; init; }
    public long ExecutionTimeMs { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
}
