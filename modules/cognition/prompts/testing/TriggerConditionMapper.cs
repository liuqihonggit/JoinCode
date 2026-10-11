namespace Core.Prompts.Testing;

/// <summary>
/// 触发条件映射器 - 基于命名约定推导Section触发条件
/// </summary>
public sealed class TriggerConditionMapper {
    private static readonly FrozenDictionary<string, TriggerCondition> ParameterMappings;

    static TriggerConditionMapper() {
        ParameterMappings = new Dictionary<string, TriggerCondition>(StringComparer.OrdinalIgnoreCase) {
            ["isBriefEnabled"] = new("简洁模式", ctx => ctx.Config.IsBriefEnabled),
            ["isAgentMode"] = new("Agent模式", ctx => ctx.Config.IsAgentMode),
            ["isReplMode"] = new("REPL模式", ctx => ctx.Config.IsReplMode),
            ["hasTodoTool"] = new("有Todo工具", ctx => ctx.Config.HasTodoTool),
            ["hasTaskTool"] = new("有Task工具", ctx => ctx.Config.HasTaskTool),
            ["enableNumericLength"] = new("启用数字长度", ctx => ctx.Config.EnableNumericLength),
            ["hasTokenBudget"] = new("有Token预算", ctx => ctx.Config.HasTokenBudget),
            ["isGitWorktree"] = new("Git工作区", ctx => ctx.Config.IsGitWorktree),
            ["customIntro"] = new("自定义介绍", ctx => !string.IsNullOrEmpty(ctx.Config.CustomIntro)),
            ["projectRules"] = new("项目规则", ctx => !string.IsNullOrEmpty(ctx.Config.ProjectRules)),
            ["externalRules"] = new("外部规则", ctx => ctx.Config.ExternalRules?.Any() == true),
            ["mcpServers"] = new("MCP服务器", ctx => ctx.Config.McpServers?.Any() == true),
            ["enabledTools"] = new("启用的工具", ctx => ctx.Config.EnabledTools?.Any() == true),
            ["scratchpadPath"] = new("草稿板路径", ctx => !string.IsNullOrEmpty(ctx.Config.ScratchpadPath)),
            ["languagePreference"] = new("语言偏好", ctx => !string.IsNullOrEmpty(ctx.Config.LanguagePreference)),
            ["modelId"] = new("模型ID", ctx => !string.IsNullOrEmpty(ctx.Config.ModelId)),
            ["modelName"] = new("模型名称", ctx => !string.IsNullOrEmpty(ctx.Config.ModelName)),
            ["version"] = new("版本", ctx => !string.IsNullOrEmpty(ctx.Config.Version)),
            ["buildTime"] = new("构建时间", ctx => !string.IsNullOrEmpty(ctx.Config.BuildTime)),
            ["issuesExplainer"] = new("问题说明", ctx => !string.IsNullOrEmpty(ctx.Config.IssuesExplainer)),
            ["feedbackChannel"] = new("反馈渠道", ctx => !string.IsNullOrEmpty(ctx.Config.FeedbackChannel)),
            ["additionalWorkdirs"] = new("额外工作目录", ctx => ctx.Config.AdditionalWorkdirs?.Any() == true),
            ["additionalEnvInfo"] = new("额外环境信息", ctx => !string.IsNullOrEmpty(ctx.Config.AdditionalEnvInfo)),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据参数名获取对应的触发条件。
    /// </summary>
    /// <param name="parameterName">参数名。</param>
    /// <returns>匹配的触发条件；若未找到则返回 null。</returns>
    public TriggerCondition? GetCondition(string parameterName) {
        return ParameterMappings.GetValueOrDefault(parameterName);
    }

    /// <summary>总是触发条件（复用同一实例）。</summary>
    private static readonly TriggerCondition AlwaysTrigger = new("总是触发", _ => true);

    /// <summary>Section 名称 → 触发条件映射（忽略大小写）。</summary>
    private static readonly FrozenDictionary<string, TriggerCondition> SectionConditionMap = new Dictionary<string, TriggerCondition>(StringComparer.OrdinalIgnoreCase) {
        // 条件触发
        ["brief"]              = new("简洁模式", ctx => ctx.Config.IsBriefEnabled),
        ["agent_default"]      = new("Agent模式", ctx => ctx.Config.IsAgentMode),
        ["agent_notes"]        = new("Agent模式", ctx => ctx.Config.IsAgentMode),
        ["repl_mode"]          = new("REPL模式", ctx => ctx.Config.IsReplMode),
        ["todo_task"]          = new("有Todo工具", ctx => ctx.Config.HasTodoTool),
        ["git_worktree"]       = new("Git工作区", ctx => ctx.Config.IsGitWorktree),
        ["mcp_servers"]        = new("MCP服务器", ctx => ctx.Config.McpServers?.Any() == true),
        ["scratchpad"]         = new("草稿板路径", ctx => !string.IsNullOrEmpty(ctx.Config.ScratchpadPath)),
        ["language"]           = new("语言偏好", ctx => !string.IsNullOrEmpty(ctx.Config.LanguagePreference)),
        ["model_info"]         = new("模型ID", ctx => !string.IsNullOrEmpty(ctx.Config.ModelId)),
        ["version_info"]       = new("版本", ctx => !string.IsNullOrEmpty(ctx.Config.Version)),
        ["additional_workdirs"] = new("额外工作目录", ctx => ctx.Config.AdditionalWorkdirs?.Any() == true),
        ["numeric_length"]     = new("启用数字长度", ctx => ctx.Config.EnableNumericLength),
        ["project_rules"]      = new("项目规则", ctx => !string.IsNullOrEmpty(ctx.Config.ProjectRules)),
        ["external_rules"]     = new("外部规则", ctx => ctx.Config.ExternalRules?.Any() == true),
        // 特殊处理
        ["feedback"]           = AlwaysTrigger,
        ["tool_result_clearing"] = new("工具结果清理", ctx => ctx.Config.ToolResultClearingEnabled),
        ["token_budget"]       = AlwaysTrigger,
        ["environment"]        = AlwaysTrigger,
        // 总是触发
        ["intro"] = AlwaysTrigger, ["cyber_risk"] = AlwaysTrigger, ["system"] = AlwaysTrigger,
        ["system_reminders"] = AlwaysTrigger, ["hooks"] = AlwaysTrigger,
        ["context_compression"] = AlwaysTrigger, ["doing_tasks"] = AlwaysTrigger,
        ["actions"] = AlwaysTrigger, ["tools"] = AlwaysTrigger, ["agent_tool"] = AlwaysTrigger,
        ["skill"] = AlwaysTrigger, ["discover_skills"] = AlwaysTrigger, ["tone"] = AlwaysTrigger,
        ["output_efficiency"] = AlwaysTrigger, ["communicating"] = AlwaysTrigger,
        ["summarize_tool_results"] = AlwaysTrigger, ["verification"] = AlwaysTrigger,
        ["proactive"] = AlwaysTrigger, ["session_guidance"] = AlwaysTrigger,
        ["memory"] = AlwaysTrigger, ["shell_info"] = AlwaysTrigger,
        ["coordinator"] = AlwaysTrigger, ["agent_generation"] = AlwaysTrigger,
        ["agent_summary"] = AlwaysTrigger, ["advisor_tool"] = AlwaysTrigger,
        ["chrome_automation"] = AlwaysTrigger, ["compact"] = AlwaysTrigger,
        ["compact_prompt"] = AlwaysTrigger, ["companion"] = AlwaysTrigger,
        ["dream_consolidation"] = AlwaysTrigger, ["extract_memories"] = AlwaysTrigger,
        ["magic_docs"] = AlwaysTrigger, ["magic_docs_prompt"] = AlwaysTrigger,
        ["output_style"] = AlwaysTrigger, ["prompt_suggestion"] = AlwaysTrigger,
        ["session_memory"] = AlwaysTrigger, ["session_memory_prompt"] = AlwaysTrigger,
        ["teammate_prompt"] = AlwaysTrigger,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 从Section名称推导触发条件
    /// </summary>
    public TriggerCondition? DeriveFromSectionName(string sectionName)
        => SectionConditionMap.TryGetValue(sectionName, out var cond) ? cond : null;
}

/// <summary>
/// 触发条件定义
/// </summary>
public sealed record TriggerCondition(
    string Description,
    Func<PromptTestContext, bool> Test
);