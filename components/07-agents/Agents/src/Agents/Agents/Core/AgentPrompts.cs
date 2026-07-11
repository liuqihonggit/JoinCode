namespace Core.Agents;

/// <summary>
/// 内置 Agent 系统提示词配置 — 委托到 AgentPromptRegistration（由 AgentPromptGenerator 自动生成）
/// </summary>
public static class AgentPrompts
{
    /// <summary>
    /// 计划 Agent 系统提示词
    /// </summary>
    public static string PlanAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.Plan.ToValue()) ?? GeneralPurposeAgentSystemPrompt;

    /// <summary>
    /// 探索 Agent 系统提示词
    /// </summary>
    public static string ExploreAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.Explore.ToValue()) ?? GeneralPurposeAgentSystemPrompt;

    /// <summary>
    /// 验证 Agent 系统提示词
    /// </summary>
    public static string VerificationAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.Verification.ToValue()) ?? GeneralPurposeAgentSystemPrompt;

    /// <summary>
    /// 通用 Agent 系统提示词
    /// </summary>
    public static string GeneralPurposeAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.GeneralPurpose.ToValue()) ?? "你是一个通用的 AI 助手。";

    /// <summary>
    /// Claude Code 引导 Agent 系统提示词
    /// </summary>
    public static string ClaudeCodeGuideAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.ClaudeCodeGuide.ToValue()) ?? GeneralPurposeAgentSystemPrompt;

    /// <summary>
    /// 上下文压缩 Agent 系统提示词
    /// </summary>
    public static string ContextCompressionAgentSystemPrompt => AgentPromptRegistration.GetSystemPrompt(BuiltInAgentType.ContextCompression.ToValue()) ?? GeneralPurposeAgentSystemPrompt;

    /// <summary>
    /// 获取指定 Agent 类型的系统提示词
    /// </summary>
    public static string GetSystemPrompt(BuiltInAgentType agentType) =>
        AgentPromptRegistration.GetSystemPrompt(agentType.ToValue()) ?? GeneralPurposeAgentSystemPrompt;
}
