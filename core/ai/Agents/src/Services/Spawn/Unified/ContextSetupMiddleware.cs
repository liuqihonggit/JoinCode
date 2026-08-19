namespace Core.Agents;

/// <summary>
/// 上下文构建中间件 — 构建 SubAgentOptions（不含 Spawn 调用，Spawn 移到 LifecycleSpawnMiddleware）
/// 统一管道版本：主代理 no-op，路径 B（SubOptions 已存在）no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class ContextSetupMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public ContextSetupMiddleware(ISubAgentContextAccessor subAgentContextAccessor, IFileStateCache? fileStateCache = null, ISkillService? skillService = null, IModelConfigLoader? modelConfigLoader = null, ILogger<ContextSetupMiddleware>? logger = null)
    {
        _subAgentContextAccessor = subAgentContextAccessor;
        _fileStateCache = fileStateCache;
        _skillService = skillService;
        _modelConfigLoader = modelConfigLoader;
        _logger = logger;
    }
    [Inject] private readonly IFileStateCache? _fileStateCache;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ISkillService? _skillService;
    [Inject] private readonly IModelConfigLoader? _modelConfigLoader;
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
            ModelName = ResolveSubagentModel(context),
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
    /// 解析子代理最终生效模型 — 对齐 claude code getAgentModel
    /// <para>优先级链: JCC_SUBAGENT_MODEL 环境变量 > SpawnOptions.Model > Definition.ModelName > inherit/父级模型</para>
    /// <para>"inherit" 关键字(不区分大小写)显式继承父线程模型,对齐 claude code getDefaultSubagentModel</para>
    /// <para>null/空 也视为继承父级(隐式 inherit,与 ClaudeCode 默认 'inherit' 语义一致)</para>
    /// <para>Bedrock 跨区域前缀继承: 若父模型有区域前缀且 provider 是 Bedrock,子代理模型继承相同前缀</para>
    /// </summary>
    private string? ResolveSubagentModel(UnifiedSpawnContext context)
    {
        var envModel = Environment.GetEnvironmentVariable("JCC_SUBAGENT_MODEL");
        if (!string.IsNullOrEmpty(envModel))
            return envModel;

        var parentModel = GetParentModel();
        var (parentRegionPrefix, isBedrockProvider) = AnalyzeParentProvider(parentModel);

        return SubAgentModelResolver.ResolveModelWithBedrock(
            context.SpawnOptions?.Model,
            context.Definition?.ModelName,
            parentModel,
            parentRegionPrefix,
            isBedrockProvider);
    }

    /// <summary>
    /// 获取父线程(主代理)模型 ID — 从 SubAgentContext.CacheSafeParams.ModelId 读取
    /// </summary>
    private string? GetParentModel()
    {
        return _subAgentContextAccessor.Current?.CacheSafeParams?.ModelId;
    }

    /// <summary>
    /// 分析父模型对应的 provider — 提取 Bedrock 区域前缀并判断是否是 Bedrock
    /// <para>对齐 claude code getBedrockRegionPrefix(parentModel) + getAPIProvider() === 'bedrock'</para>
    /// <para>无 IModelConfigLoader 或父模型未识别 provider 时,isBedrockProvider=false(不应用前缀)</para>
    /// </summary>
    private (string? parentRegionPrefix, bool isBedrockProvider) AnalyzeParentProvider(string? parentModel)
    {
        if (string.IsNullOrEmpty(parentModel))
            return (null, false);

        var parentRegionPrefix = BedrockModelHelper.GetBedrockRegionPrefix(parentModel);
        if (parentRegionPrefix is null)
            return (null, false);

        if (_modelConfigLoader is null)
            return (parentRegionPrefix, false);

        var providerName = _modelConfigLoader.FindProviderByModelId(parentModel);
        if (string.IsNullOrEmpty(providerName))
            return (parentRegionPrefix, false);

        var vendor = VendorKindExtensions.FromValue(providerName);
        return (parentRegionPrefix, vendor == VendorKind.Bedrock);
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
