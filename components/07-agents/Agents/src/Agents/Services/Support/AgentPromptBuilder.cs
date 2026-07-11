namespace Core.Agents;

[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentPromptBuilder))]
public sealed partial class AgentPromptBuilder : JoinCode.Abstractions.Interfaces.IAgentPromptBuilder
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider _definitionProvider;
    [Inject] private readonly IServiceProvider? _serviceProvider;
    [Inject] private readonly ILogger<AgentPromptBuilder>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    /// <summary>
    /// 延迟解析 ITeammateInitService，打破循环依赖：
    /// IAgentPromptBuilder → ITeammateInitService → ITeamManager → ITeammateObserver → AgentCoordinator → ITeammateInitService
    /// </summary>
    private ITeammateInitService? ResolvedTeammateInitService =>
        _serviceProvider?.GetService(typeof(ITeammateInitService)) as ITeammateInitService;

    public async Task<string> BuildSystemPromptAsync(
        string? agentType,
        string task,
        IReadOnlyList<string>? context = null,
        CancellationToken cancellationToken = default)
    {
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(agentType))
        {
            definition = await _definitionProvider.GetAgentDefinitionAsync(agentType, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var sb = new StringBuilder();

        if (definition?.SystemPrompt is not null)
        {
            sb.AppendLine(definition.SystemPrompt);
        }
        else
        {
            sb.AppendLine(string.Format(AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage, task));
        }

        if (!string.IsNullOrWhiteSpace(definition?.AgentType))
        {
            sb.AppendLine();
            sb.AppendLine($"你是 {definition.AgentType} 类型的代理。");
        }

        if (!string.IsNullOrWhiteSpace(definition?.Description))
        {
            sb.AppendLine($"角色描述: {definition.Description}");
        }

        var toolsDescription = GetToolsDescription(definition);
        if (toolsDescription is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"可用工具: {toolsDescription}");
        }

        if (context is not null && context.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("上下文信息:");
            foreach (var ctx in context)
            {
                sb.AppendLine($"- {ctx}");
            }
        }

        if (_subAgentContextAccessor.Current is not null && ResolvedTeammateInitService is not null)
        {
            var currentCtx = _subAgentContextAccessor.Current;
            if (!string.IsNullOrWhiteSpace(currentCtx.SessionId) && currentCtx.SessionId != "default")
            {
                try
                {
                    var initContext = await ResolvedTeammateInitService.BuildInitContextAsync(currentCtx.SessionId, currentCtx.AgentId, cancellationToken).ConfigureAwait(false);
                    if (initContext is not null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("=== 团队上下文 ===");
                        sb.AppendLine(initContext.BuildContextSummary());
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[AgentPromptBuilder] 构建团队上下文失败: {AgentId}", currentCtx.AgentId);
                }
            }
        }

        if (definition?.ModelName is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"使用模型: {definition.ModelName}");
        }

        if (definition?.Skills is not null && definition.Skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("预加载技能:");
            foreach (var skill in definition.Skills)
            {
                sb.AppendLine($"- /{skill}");
            }
        }

        return sb.ToString();
    }

    private static string? GetToolsDescription(JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition)
    {
        if (definition is null) return null;

        var hasAllowlist = definition.Tools is not null && definition.Tools.Count > 0;
        var hasDenylist = definition.DisallowedTools is not null && definition.DisallowedTools.Count > 0;

        if (hasAllowlist && hasDenylist)
        {
            var denySet = new HashSet<string>(definition.DisallowedTools!);
            var effectiveTools = definition.Tools!.Where(t => !denySet.Contains(t)).ToList();
            return effectiveTools.Count == 0 ? "无" : string.Join(", ", effectiveTools);
        }

        if (hasAllowlist) return string.Join(", ", definition.Tools!);
        if (hasDenylist) return $"除 {string.Join(", ", definition.DisallowedTools!)} 外的所有工具";

        return null;
    }
}
