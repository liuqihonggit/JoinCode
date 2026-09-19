namespace Core.Agents;

/// <summary>
/// 代理系统提示词构建器 — 根据代理定义、上下文、团队信息组装系统提示词
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentPromptBuilder), ServiceLifetime.Singleton)]
public sealed partial class AgentPromptBuilder : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentPromptBuilder {

    /// <summary>
    /// 构造 AgentPromptBuilder 实例，注入定义提供者、子代理上下文访问器、服务提供者及日志器
    /// </summary>
    public AgentPromptBuilder(JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider definitionProvider, ISubAgentContextAccessor subAgentContextAccessor, IServiceProvider? serviceProvider = null, ILogger<AgentPromptBuilder>? logger = null) {
        _definitionProvider = definitionProvider;
        _subAgentContextAccessor = subAgentContextAccessor;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    private readonly JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider _definitionProvider;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<AgentPromptBuilder>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    /// <summary>
    /// 延迟解析 ITeammateInitService，打破循环依赖：
    /// IAgentPromptBuilder → ITeammateInitService → ITeamManager → ITeammateObserver → AgentCoordinator → ITeammateInitService
    /// </summary>
    private ITeammateInitService? ResolvedTeammateInitService =>
        _serviceProvider?.GetService(typeof(ITeammateInitService)) as ITeammateInitService;

    /// <summary>
    /// 构建代理系统提示词，合并定义中的系统提示、角色描述、工具列表、上下文与团队信息
    /// </summary>
    /// <param name="agentType">代理类型标识（可选）</param>
    /// <param name="task">任务描述</param>
    /// <param name="context">上下文信息列表（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>组装后的系统提示词</returns>
    public async Task<string> BuildSystemPromptAsync(
        string? agentType,
        string task,
        IReadOnlyList<string>? context = null,
        CancellationToken cancellationToken = default) {
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(agentType)) {
            var role = AgentRole.Executor;
            var variant = ExecutorVariantExtensions.FromValue(agentType);
            definition = await _definitionProvider.GetAgentDefinitionAsync(role, variant, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var sb = new StringBuilder();

        if (definition?.SystemPrompt is not null) {
            sb.AppendLine(definition.SystemPrompt);
        } else {
            sb.AppendLine(string.Format(AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage, task));
        }

        if (!string.IsNullOrWhiteSpace(definition?.DisplayId)) {
            sb.AppendLine();
            sb.AppendLine($"你是 {definition.DisplayId} 类型的代理。");
        }

        if (!string.IsNullOrWhiteSpace(definition?.Description)) {
            sb.AppendLine($"角色描述: {definition.Description}");
        }

        var toolsDescription = GetToolsDescription(definition);
        if (toolsDescription is not null) {
            sb.AppendLine();
            sb.AppendLine($"可用工具: {toolsDescription}");
        }

        if (context is not null && context.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("上下文信息:");
            foreach (var ctx in context) {
                sb.AppendLine($"- {ctx}");
            }
        }

        if (_subAgentContextAccessor.Current is not null && ResolvedTeammateInitService is not null) {
            var currentCtx = _subAgentContextAccessor.Current;
            if (!string.IsNullOrWhiteSpace(currentCtx.SessionId) && currentCtx.SessionId != global::Core.Utils.SessionIdFactory.DefaultSessionId) {
                try {
                    var initContext = await ResolvedTeammateInitService.BuildInitContextAsync(currentCtx.SessionId, currentCtx.AgentId, cancellationToken).ConfigureAwait(false);
                    if (initContext is not null) {
                        sb.AppendLine();
                        sb.AppendLine("=== 团队上下文 ===");
                        sb.AppendLine(initContext.BuildContextSummary());
                    }
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "[AgentPromptBuilder] 构建团队上下文失败: {AgentId}", currentCtx.AgentId);
                }
            }
        }

        if (definition?.ModelName is not null) {
            sb.AppendLine();
            sb.AppendLine($"使用模型: {definition.ModelName}");
        }

        if (definition?.Skills is not null && definition.Skills.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("预加载技能:");
            foreach (var skill in definition.Skills) {
                sb.AppendLine($"- /{skill}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 带运行时上下文的系统提示词构建 — 注入当前 MCP/skills/settings
    /// <para>对齐 TS 原版 getSystemPrompt({ toolUseContext }) 闭包模式</para>
    /// </summary>
    public async Task<string> BuildSystemPromptAsync(
        string? agentType,
        string task,
        IReadOnlyList<string>? context,
        AgentPromptContext? promptContext,
        CancellationToken cancellationToken = default) {
        var basePrompt = await BuildSystemPromptAsync(agentType, task, context, cancellationToken).ConfigureAwait(false);

        if (promptContext is null)
            return basePrompt;

        var sb = new StringBuilder(basePrompt);

        if (promptContext.McpServers is { Count: > 0 } mcpServers) {
            sb.AppendLine();
            sb.AppendLine("=== 当前 MCP 服务器 ===");
            foreach (var server in mcpServers)
                sb.AppendLine($"- {server}");
        }

        if (promptContext.AvailableSkills is { Count: > 0 } availableSkills) {
            sb.AppendLine();
            sb.AppendLine("=== 可用技能 ===");
            foreach (var skill in availableSkills)
                sb.AppendLine($"- /{skill}");
        }

        if (!string.IsNullOrWhiteSpace(promptContext.SettingsSummary)) {
            sb.AppendLine();
            sb.AppendLine("=== 当前配置 ===");
            sb.AppendLine(promptContext.SettingsSummary);
        }

        return sb.ToString();
    }

    private static string? GetToolsDescription(JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition) {
        if (definition is null) return null;

        if (definition.Tools is { Count: > 0 } tools && definition.DisallowedTools is { Count: > 0 } disallowedTools) {
            var denySet = new HashSet<string>(disallowedTools);
            var effectiveTools = tools.Where(t => !denySet.Contains(t)).ToList();
            return effectiveTools.Count == 0 ? "无" : string.Join(", ", effectiveTools);
        }

        if (definition.Tools is { Count: > 0 } toolsOnly) return string.Join(", ", toolsOnly);
        if (definition.DisallowedTools is { Count: > 0 } disallowedOnly) return $"除 {string.Join(", ", disallowedOnly)} 外的所有工具";

        return null;
    }
}