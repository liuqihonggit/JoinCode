namespace Core.Agents;

/// <summary>
/// 上下文构建中间件 — 构建 SubAgentOptions（不含 Spawn 调用，Spawn 移到 LifecycleSpawnMiddleware）
/// 统一管道版本：主代理 no-op，路径 B（SubOptions 已存在）no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class ContextSetupMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public ContextSetupMiddleware(ISubAgentContextAccessor subAgentContextAccessor, IFileStateCache? fileStateCache = null, ISkillService? skillService = null, ILogger<ContextSetupMiddleware>? logger = null)
    {
        _subAgentContextAccessor = subAgentContextAccessor;
        _fileStateCache = fileStateCache;
        _skillService = skillService;
        _logger = logger;
    }
    [Inject] private readonly IFileStateCache? _fileStateCache;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ISkillService? _skillService;
    [Inject] private readonly ILogger<ContextSetupMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.IsMainAgent || context.SpawnOptions is null || context.SubOptions is not null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var cacheSafeParams = BuildFilteredCacheSafeParams(context.Definition);
        context.CacheSafeParams = cacheSafeParams;

        var skills = context.Definition?.Skills;
        MessageList? initialMessageList = null;
        if (skills is not null && skills.Count > 0 && _skillService is not null)
        {
            initialMessageList = await BuildSkillPreloadMessageListAsync(skills, ct).ConfigureAwait(false);
        }

        var subOptions = new SubAgentOptions
        {
            Role = context.SpawnOptions.Role,
            Variant = context.SpawnOptions.Variant,
            AdditionalInstructions = context.SpawnOptions.Prompt,
            ModelName = Environment.GetEnvironmentVariable("JCC_SUBAGENT_MODEL")
                ?? context.SpawnOptions.Model
                ?? context.Definition?.ModelName,
            Temperature = context.Definition?.Temperature ?? 0.7f,
            DisplayName = context.SpawnOptions.Name ?? context.SpawnOptions.Description,
            SystemPrompt = context.SystemPrompt,
            AllowedTools = MergeAllowedTools(context.SpawnOptions.AllowedTools, context.Definition?.Tools),
            DeniedTools = context.Definition?.DisallowedTools,
            PreloadSkills = context.Definition?.Skills,
            PermissionMode = context.Definition?.PermissionMode,
            InitialPrompt = context.Definition?.InitialPrompt,
            InitialMessageList = initialMessageList,
            WorktreePath = context.SpawnOptions.Cwd ?? _subAgentContextAccessor.Current?.WorktreePath,
            SubagentName = context.SpawnOptions.Name ?? context.Definition?.DisplayId,
            IsBuiltIn = !string.IsNullOrEmpty(context.Definition?.SourcePath),
            ProgressTracker = context.ProgressTracker,
            CacheSafeParams = cacheSafeParams,
            ReadFileState = _fileStateCache?.Clone(),
            Effort = context.SpawnOptions.Effort,
            GoalId = context.SpawnOptions.GoalId,
            GraphNodeId = context.SpawnOptions.GraphNodeId,
            TokenBudget = context.SpawnOptions.TokenBudget,
            FreshContext = context.SpawnOptions.FreshContext,
        };

        context.ResolvedSubOptions = subOptions;

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建 skill 预加载消息列表 — 对齐 claude code skills 字段: spawn 时预加载 skill 内容到 initialMessages
    /// </summary>
    private async Task<MessageList> BuildSkillPreloadMessageListAsync(List<string> skills, CancellationToken ct)
    {
        var messageList = new MessageList();
        foreach (var skillName in skills)
        {
            if (string.IsNullOrWhiteSpace(skillName) || _skillService is null)
                continue;

            try
            {
                var skill = await _skillService.GetSkillAsync(skillName, ct).ConfigureAwait(false);
                if (skill is null)
                    continue;

                var content = skill.Steps is not null && skill.Steps.Count > 0
                    ? skill.Steps[0].Prompt
                    : skill.ContentTemplate;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    messageList.AddUserMessage($"[Skill: {skillName}]\n{content}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ContextSetupMiddleware] 预加载 skill {SkillName} 失败", skillName);
            }
        }
        return messageList;
    }

    private CacheSafeParams? BuildFilteredCacheSafeParams(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition)
    {
        var parentParams = _subAgentContextAccessor.Current?.CacheSafeParams;
        if (parentParams is null) return null;

        var cloned = parentParams.Clone();

        var userContext = cloned.UserContext;
        if (definition?.OmitClaudeMd == true && userContext is not null)
        {
            userContext = FilterKey(userContext, "claudeMd");
        }

        var systemContext = cloned.SystemContext;
        if (definition?.OmitGitStatus == true && systemContext is not null)
        {
            systemContext = FilterKey(systemContext, "gitStatus");
        }

        return new CacheSafeParams
        {
            RenderedSystemPrompt = cloned.RenderedSystemPrompt,
            ModelId = cloned.ModelId,
            ToolNames = cloned.ToolNames,
            UserContext = userContext,
            SystemContext = systemContext,
            ContentReplacementState = cloned.ContentReplacementState
        };
    }

    private static Dictionary<string, string> FilterKey(Dictionary<string, string> dict, string key)
    {
        var filtered = new Dictionary<string, string>(dict);
        filtered.Remove(key);
        return filtered;
    }

    private static List<string>? MergeAllowedTools(IEnumerable<string>? callerTools, List<string>? definitionTools)
    {
        if (callerTools is not null && callerTools.Any())
            return callerTools.ToList();
        return definitionTools;
    }
}
