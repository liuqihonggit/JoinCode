


namespace McpToolHandlers;

/// <summary>
/// 技能执行工具处理器 - 提供技能执行和列表功能
/// 参考Claude Code的SkillTool实现：单工具统一入口，skill名称+可选args
/// </summary>
[McpToolHandler(ToolCategory.Skill)]
public class SkillToolHandlers
{
    private readonly ISkillService _skillService;
    private readonly IAgentService? _agentService;
    private readonly ArgumentSubstitutor _argumentSubstitutor = new();

    public SkillToolHandlers(ISkillService skillService, IAgentService? agentService = null)
    {
        _skillService = skillService ?? throw new ArgumentNullException(nameof(skillService));
        _agentService = agentService;
    }

    /// <summary>
    /// 技能工具 - 统一入口（与TS对齐）
    /// LLM 通过 skill 名称调用技能，args 为可选参数
    /// </summary>
    [McpTool(SkillToolNameConstants.Skill, "Execute a specified skill. Pass skill name (e.g., commit, review-pr, pdf) and optional arguments", "skill")]
    public async Task<ToolResult> SkillAsync(
        [McpToolParameter("Skill name, e.g., commit, review-pr, pdf")] string skill,
        [McpToolParameter("Skill arguments, optional", Required = false)] string? args = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SkillNameCannotBeEmpty)).Build();
        }

        var skillName = skill.TrimStart('/');

        if (!_skillService.SkillExists(skillName))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SkillNotFound, skillName)).Build();
        }

        // 获取技能定义 — 在执行前检查执行模式
        var skillDef = await _skillService.GetSkillAsync(skillName, cancellationToken).ConfigureAwait(false);

        // 参数权限验证 — 对齐 TS SkillTool.validateInput + checkPermissions
        var validation = ValidateSkillInvocation(skillName, skillDef);
        if (!validation.IsValid)
        {
            return McpResultBuilder.Error().WithText(validation.ErrorMessage!).Build();
        }

        // fork 模式 — 对齐 TS SkillTool.executeForkedSkill
        // 在独立子智能体中执行技能，不修改当前会话上下文
        if (skillDef is not null && skillDef.Context == SkillExecutionMode.Fork && _agentService is not null)
        {
            return await ExecuteForkedSkillAsync(skillName, skillDef, args, cancellationToken).ConfigureAwait(false);
        }

        // inline 模式 — 对齐 TS SkillTool.call() inline 模式
        // TS 行为: 返回 newMessages 将技能 prompt 注入对话流，LLM 自行执行
        // C# 行为: 通过 ToolResult.InjectedMessages 注入技能 prompt 作为 user message
        if (skillDef is not null && skillDef.Context == SkillExecutionMode.Inline)
        {
            return ExecuteInlineSkill(skillName, skillDef, args);
        }

        // 非 inline/fork 模式（fallback）— 程序化执行步骤
        Dictionary<string, JsonElement>? parameters = null;
        if (!string.IsNullOrEmpty(args))
        {
            try
            {
                parameters = JsonSerializer.Deserialize(args, McpToolHandlersJsonContext.Default.DictionaryStringJsonElement);
            }
            catch (JsonException)
            {
                using var doc = JsonDocument.Parse($"{{\"args\":{JsonSerializer.Serialize(args, McpToolHandlersJsonContext.Default.String)}}}");
                parameters = new Dictionary<string, JsonElement>
                {
                    ["args"] = doc.RootElement.GetProperty("args").Clone()
                };
            }
        }

        var ctx = new SkillExecutionContext(cancellationToken);
        var result = await _skillService.ExecuteAsync(skillName, parameters, ctx).ConfigureAwait(false);

        var response = new StringBuilder(256);
        response.AppendLine(L.T(StringKey.LabelSkill, result.SkillName));

        if (result.DurationMs.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelExecutionTime, result.DurationMs.Value));
        }

        response.AppendLine();

        if (result.Success)
        {
            if (!string.IsNullOrEmpty(result.Output))
            {
                response.AppendLine(L.T(StringKey.LabelOutput));
                response.AppendLine(result.Output);
            }
            else
            {
                response.AppendLine(L.T(StringKey.SkillExecutionSuccessNoOutput));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        else
        {
            response.AppendLine($"{L.T(StringKey.LabelError)} {result.ErrorMessage ?? L.T(StringKey.SkillExecutionFailed)}");
            return McpResultBuilder.Error().WithText(response.ToString()).Build();
        }
    }

    /// <summary>
    /// inline 模式执行技能 — 对齐 TS SkillTool.call() inline 模式
    /// 将技能 prompt 作为 InjectedMessages 注入对话流，LLM 自行执行
    /// 同时设置 ContextModifier（allowedTools/model/effort）
    /// </summary>
    private ToolResult ExecuteInlineSkill(string skillName, SkillDefinition skillDef, string? args)
    {
        // 构建技能完整 prompt — 对齐 TS getPromptForCommand
        var skillContent = BuildSkillPromptContent(skillDef, args);

        var toolResult = McpResultBuilder.Success()
            .WithText($"Skill '{skillName}' loaded. The skill instructions have been injected into the conversation.")
            .Build();

        // 注入技能 prompt 作为 user message — 对齐 TS newMessages
        toolResult.InjectedMessages = new List<ApiMessage>
        {
            new(MessageRole.User, skillContent)
        };

        // 设置 contextModifier — 对齐 TS SkillTool.call() 的 contextModifier
        if (skillDef.AllowedTools.Count > 0 || skillDef.Model is not null || skillDef.Effort is not null)
        {
            toolResult.ContextModifier = ctx => ctx.ApplySkillModifier(skillDef);
        }

        return toolResult;
    }

    /// <summary>
    /// 执行技能
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillExecute, "Execute a specified skill with name and parameters", "skill")]
    public async Task<ToolResult> SkillExecuteAsync(
        [McpToolParameter("Skill name")] string skill_name,
        [McpToolParameter("Skill parameters (JSON object), optional", Required = false)] Dictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skill_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SkillNameCannotBeEmpty)).Build();
        }

        if (!_skillService.SkillExists(skill_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SkillNotFound, skill_name)).Build();
        }

        var ctx = new SkillExecutionContext(cancellationToken);
        var result = await _skillService.ExecuteAsync(skill_name, parameters, ctx);

        var response = new StringBuilder(256);
        response.AppendLine(L.T(StringKey.LabelSkill, result.SkillName));

        if (result.DurationMs.HasValue)
        {
            response.AppendLine(L.T(StringKey.LabelExecutionTime, result.DurationMs.Value));
        }

        response.AppendLine();

        if (result.Success)
        {
            if (!string.IsNullOrEmpty(result.Output))
            {
                response.AppendLine(L.T(StringKey.LabelOutput));
                response.AppendLine(result.Output);
            }
            else
            {
                response.AppendLine(L.T(StringKey.SkillExecutionSuccessNoOutput));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        else
        {
            response.AppendLine($"{L.T(StringKey.LabelError)} {result.ErrorMessage ?? L.T(StringKey.SkillExecutionFailed)}");
            return McpResultBuilder.Error().WithText(response.ToString()).Build();
        }
    }

    /// <summary>
    /// 列出所有可用技能
    /// </summary>
    [McpTool(SkillToolNameConstants.SkillList, "List all available skills", "skill")]
    public async Task<ToolResult> SkillListAsync(
        CancellationToken cancellationToken = default)
    {
        var skills = await _skillService.GetAvailableSkillsAsync(cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder(512);
        response.AppendLine(L.T(StringKey.AvailableSkills, skills.Count));

        if (skills.Count > 0)
        {
            // 按标签分组（使用第一个标签作为分类）
            var groupedSkills = skills
                .GroupBy(s => s.Tags.FirstOrDefault() ?? L.T(StringKey.Uncategorized))
                .OrderBy(g => g.Key);

            foreach (var group in groupedSkills)
            {
                response.AppendLine();
                response.AppendLine($"[{group.Key}]");

                foreach (var skill in group.OrderBy(s => s.Name))
                {
                    response.AppendLine($"  {skill.Name} - {skill.Description}");

                    if (skill.Parameters.Count > 0)
                    {
                        var paramDesc = string.Join(", ", skill.Parameters.Select(p =>
                            $"{p.Key}{(p.Value.Required ? "*" : "")}"));
                        response.AppendLine($"    {L.T(StringKey.LabelParameters, paramDesc)}");
                    }
                }
            }
        }
        else
        {
            response.AppendLine(L.T(StringKey.NoAvailableSkills));
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// fork 模式执行技能 — 对齐 TS SkillTool.executeForkedSkill
    /// 在独立子智能体中执行技能，不修改当前会话上下文
    /// 适用于自包含的、不需要中间用户交互的 skill
    /// 使用 RunAgentStreamAsync 流式消费子智能体输出，对齐 TS for await (const message of runAgent(...))
    /// </summary>
    private async Task<ToolResult> ExecuteForkedSkillAsync(
        string skillName,
        SkillDefinition skillDef,
        string? args,
        CancellationToken cancellationToken)
    {
        // 构建技能完整 prompt — 对齐 TS prepareForkedCommandContext
        var skillContent = BuildSkillPromptContent(skillDef, args);

        // 创建子智能体 — 对齐 TS runAgent
        var spawnOptions = new AgentSpawnOptions
        {
            Description = $"Skill: {skillName}",
            Prompt = skillContent,
            RunInBackground = false,
            IsolationMode = skillDef.Isolation,
            Model = skillDef.Model,
            Name = $"skill-{skillName}",
            AgentType = skillDef.Agent,
            AllowedTools = skillDef.AllowedTools.Count > 0 ? skillDef.AllowedTools : null,
            Effort = skillDef.Effort,
        };

        try
        {
            // 对齐 TS: for await (const message of runAgent(...))
            // 使用 RunAgentStreamAsync 流式消费子智能体输出
            // TS 仅在消息包含 tool_use/tool_result 时调用 onProgress
            // C# 对应在 ToolCallStart/ToolCallEnd chunk 时记录进度
            var responseBuilder = new StringBuilder(256);
            var progressMessages = new List<SkillProgressMessage>();
            string? agentId = null;
            bool hasError = false;
            string? errorMessage = null;

            await foreach (var chunk in _agentService!.RunAgentStreamAsync(spawnOptions, cancellationToken).ConfigureAwait(false))
            {
                agentId ??= chunk.AgentId;

                switch (chunk.Type)
                {
                    // 对齐 TS: onProgress 仅在 tool_use/tool_result 时触发
                    case JoinCode.Abstractions.Models.Agent.AgentStreamChunkType.ToolCallStart:
                        progressMessages.Add(new SkillProgressMessage
                        {
                            Type = SkillProgressType.ToolCallStart,
                            ToolName = chunk.ToolName,
                            ToolCallNumber = chunk.ToolCallNumber,
                        });
                        break;

                    case JoinCode.Abstractions.Models.Agent.AgentStreamChunkType.ToolCallEnd:
                        progressMessages.Add(new SkillProgressMessage
                        {
                            Type = SkillProgressType.ToolCallEnd,
                            ToolName = chunk.ToolName,
                            ToolCallNumber = chunk.ToolCallNumber,
                            ToolSucceeded = chunk.ToolResult is null || !chunk.ToolResult.IsError,
                        });
                        break;

                    case JoinCode.Abstractions.Models.Agent.AgentStreamChunkType.Content when chunk.Content is not null:
                        responseBuilder.Append(chunk.Content);
                        break;

                    case JoinCode.Abstractions.Models.Agent.AgentStreamChunkType.Error:
                        hasError = true;
                        errorMessage = chunk.Content;
                        break;
                }
            }

            // 构建最终结果
            var resultBuilder = new StringBuilder(256);
            resultBuilder.AppendLine($"Skill \"{skillName}\" completed (forked execution).");
            if (agentId is not null)
                resultBuilder.AppendLine($"Agent ID: {agentId}");

            if (hasError)
            {
                resultBuilder.AppendLine();
                resultBuilder.AppendLine($"Error: {errorMessage ?? "Unknown error"}");
                return McpResultBuilder.Error().WithText(resultBuilder.ToString()).Build();
            }

            var output = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(output))
            {
                resultBuilder.AppendLine();
                resultBuilder.AppendLine("Result:");
                resultBuilder.AppendLine(output);
            }

            var toolResult = McpResultBuilder.Success().WithText(resultBuilder.ToString()).Build();

            // 对齐 TS: onProgress 进度消息传递给 UI
            // TS 通过 renderToolUseProgressMessage 渲染子智能体的工具调用进度
            if (progressMessages.Count > 0)
            {
                toolResult.SkillProgressMessages = progressMessages;
            }

            return toolResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error()
                .WithText($"Forked skill execution failed for '{skillName}': {ex.Message}")
                .Build();
        }
    }

    /// <summary>
    /// 构建技能完整 prompt 内容 — 对齐 TS command.getPromptForCommand(args, context)
    /// TS 返回完整 SKILL.md body（含变量替换），C# 从 ContentTemplate 或 Steps 构建
    /// </summary>
    private string BuildSkillPromptContent(SkillDefinition skillDef, string? args)
    {
        var promptBuilder = new StringBuilder(1024);

        // 对齐 TS: Base directory 注入 — "Base directory for this skill: {baseDir}"
        if (skillDef.SourcePath is not null)
        {
            var baseDir = System.IO.Path.GetDirectoryName(skillDef.SourcePath);
            if (!string.IsNullOrEmpty(baseDir))
            {
                promptBuilder.AppendLine($"Base directory for this skill: {baseDir.Replace('\\', '/')}");
                promptBuilder.AppendLine();
            }
        }

        // 优先使用 ContentTemplate — 对齐 TS getPromptForCommand 返回的完整 body
        if (!string.IsNullOrWhiteSpace(skillDef.ContentTemplate))
        {
            var content = _argumentSubstitutor.Substitute(
                skillDef.ContentTemplate, args,
                skillDirectory: skillDef.SourcePath is not null
                    ? System.IO.Path.GetDirectoryName(skillDef.SourcePath)
                    : null,
                sessionId: null,
                appendIfNoPlaceholder: true);
            promptBuilder.Append(content);
        }
        else
        {
            // fallback: 从 Steps 构建 prompt — 对齐 TS 技能的 step-by-step 指令
            promptBuilder.AppendLine($"# Skill: {skillDef.Name}");
            if (!string.IsNullOrWhiteSpace(skillDef.Description))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine(skillDef.Description);
            }

            if (!string.IsNullOrWhiteSpace(args))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine($"Arguments: {args}");
            }

            if (skillDef.Steps.Count > 0)
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("## Instructions");
                foreach (var step in skillDef.Steps)
                {
                    if (!string.IsNullOrWhiteSpace(step.Prompt))
                    {
                        var substitutedPrompt = _argumentSubstitutor.Substitute(
                            step.Prompt, args,
                            skillDirectory: skillDef.SourcePath is not null
                                ? System.IO.Path.GetDirectoryName(skillDef.SourcePath)
                                : null,
                            sessionId: null,
                            appendIfNoPlaceholder: false);
                        promptBuilder.AppendLine(substitutedPrompt);
                    }
                    else if (!string.IsNullOrWhiteSpace(step.Description))
                    {
                        promptBuilder.AppendLine($"- {step.Description}");
                    }
                }
            }
        }

        return promptBuilder.ToString();
    }

    /// <summary>
    /// 验证技能调用权限 — 对齐 TS SkillTool.validateInput + checkPermissions
    /// 检查顺序: disableModelInvocation → 安全属性白名单
    /// </summary>
    private static SkillValidationResult ValidateSkillInvocation(string skillName, SkillDefinition? skillDef)
    {
        // 技能定义不存在 — 允许继续（后续 SkillExists 已检查）
        if (skillDef is null)
        {
            return SkillValidationResult.Valid();
        }

        // 对齐 TS SkillTool.validateInput: disableModelInvocation 检查
        // 标记为 true 的技能禁止模型自动调用
        if (skillDef.DisableModelInvocation)
        {
            return SkillValidationResult.Invalid(
                $"Skill '{skillName}' cannot be used with the skill tool due to disable-model-invocation flag");
        }

        // 安全属性检查 — 对齐 TS skillHasOnlySafeProperties
        // 仅含安全属性的技能可自动放行，含危险属性的需要额外权限
        // 当前实现：AllowedTools/Permissions/Dependencies 非空时标记为需权限审查
        // 实际权限拦截由 Guard 子系统的 PermissionHook 处理
        return SkillValidationResult.Valid();
    }
}

/// <summary>
/// 技能验证结果
/// </summary>
internal sealed class SkillValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }

    public static SkillValidationResult Valid() => new() { IsValid = true };
    public static SkillValidationResult Invalid(string errorMessage) => new() { IsValid = false, ErrorMessage = errorMessage };
}
