namespace Core.Prompts.Testing;

/// <summary>
/// 触发条件映射器 - 基于命名约定推导Section触发条件
/// </summary>
public sealed class TriggerConditionMapper
{
    private static readonly Dictionary<string, TriggerCondition> ParameterMappings;

    static TriggerConditionMapper()
    {
        ParameterMappings = new Dictionary<string, TriggerCondition>(StringComparer.OrdinalIgnoreCase)
        {
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
        };
    }

    public TriggerCondition? GetCondition(string parameterName)
    {
        return ParameterMappings.GetValueOrDefault(parameterName);
    }

    /// <summary>
    /// 从Section名称推导触发条件
    /// </summary>
    public TriggerCondition? DeriveFromSectionName(string sectionName)
    {
        return sectionName.ToLowerInvariant() switch
        {
            // 条件触发的Section
            "brief" => GetCondition("isBriefEnabled"),
            "agent_default" or "agent_notes" => GetCondition("isAgentMode"),
            "repl_mode" => GetCondition("isReplMode"),
            "todo_task" => GetCondition("hasTodoTool"),
            "git_worktree" => GetCondition("isGitWorktree"),
            "mcp_servers" => GetCondition("mcpServers"),
            "scratchpad" => GetCondition("scratchpadPath"),
            "language" => GetCondition("languagePreference"),
            "model_info" => GetCondition("modelId"),
            "version_info" => GetCondition("version"),
            "additional_workdirs" => GetCondition("additionalWorkdirs"),
            "simple_mode" => GetCondition("isSimpleMode"),
            "numeric_length" => GetCondition("enableNumericLength"),
            "project_rules" => GetCondition("projectRules"),
            "external_rules" => GetCondition("externalRules"),

            // feedback特殊处理：总是触发（有默认内容）
            "feedback" => new TriggerCondition("总是触发", _ => true),

            // tool_result_clearing特殊处理：默认启用
            "tool_result_clearing" => new TriggerCondition("工具结果清理", ctx => ctx.Config.ToolResultClearingEnabled),

            // token_budget特殊处理：总是触发（注释说明即使没有预算也保留）
            "token_budget" => new TriggerCondition("总是触发", _ => true),

            // environment特殊处理：总是触发（动态获取环境信息）
            "environment" => new TriggerCondition("总是触发", _ => true),

            // 总是触发的Section
            "intro" or "cyber_risk" or "system" or "system_reminders" or "hooks"
                or "context_compression" or "doing_tasks" or "actions" or "tools"
                or "agent_tool" or "skill" or "discover_skills" or "tone"
                or "output_efficiency" or "communicating"
                or "summarize_tool_results" or "verification" or "proactive"
                or "session_guidance" or "memory" or "shell_info"
                or "coordinator" or "agent_generation" or "agent_summary"
                or "advisor_tool" or "chrome_automation" or "compact" or "compact_prompt"
                or "companion" or "dream_consolidation" or "extract_memories"
                or "magic_docs" or "magic_docs_prompt"
                or "output_style" or "prompt_suggestion" or "session_memory"
                or "session_memory_prompt" or "teammate_prompt"
                => new TriggerCondition("总是触发", _ => true),
            _ => null
        };
    }
}

/// <summary>
/// 触发条件定义
/// </summary>
public sealed record TriggerCondition(
    string Description,
    Func<PromptTestContext, bool> Test
);
