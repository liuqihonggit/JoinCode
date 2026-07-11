namespace Core.Agents;

/// <summary>
/// 上下文构建中间件 — 构建 SubAgentOptions 并 Spawn 子智能体
/// </summary>
[Register]
public sealed partial class ContextSetupMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IFileStateCache? _fileStateCache;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    /// <summary>上下文构建在提示构建之后</summary>

    /// <summary>上下文构建失败应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Propagate;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        // 对齐 TS: shouldOmitClaudeMd / resolvedSystemContext — 过滤 claudeMd/gitStatus
        var cacheSafeParams = BuildFilteredCacheSafeParams(context.Definition);
        context.CacheSafeParams = cacheSafeParams;

        var subOptions = new SubAgentOptions
        {
            AgentType = context.Options.AgentType,
            AdditionalInstructions = context.Options.Prompt,
            ModelName = context.Options.Model ?? context.Definition?.ModelName,
            Temperature = context.Definition?.Temperature ?? 0.7f,
            DisplayName = context.Options.Name ?? context.Options.Description,
            SystemPrompt = context.SystemPrompt,
            AllowedTools = MergeAllowedTools(context.Options.AllowedTools, context.Definition?.Tools),
            DeniedTools = context.Definition?.DisallowedTools,
            PreloadSkills = context.Definition?.Skills,
            PermissionMode = context.Definition?.PermissionMode,
            WorktreePath = context.Options.Cwd ?? _subAgentContextAccessor.Current?.WorktreePath,
            SubagentName = context.Options.Name ?? context.Definition?.AgentType,
            IsBuiltIn = !string.IsNullOrEmpty(context.Definition?.SourcePath),
            ProgressTracker = context.ProgressTracker,
            CacheSafeParams = cacheSafeParams,
            // 对齐 TS: cloneFileStateCache — 子智能体克隆父级文件读取状态实现隔离
            ReadFileState = _fileStateCache?.Clone(),
            // 对齐 TS executeForkedSkill: 传递 effort 给子智能体
            Effort = context.Options.Effort,
        };

        context.SubOptions = subOptions;

        var subAgent = await _lifecycleManager.SpawnSubAgentAsync(context.Options.Description, subOptions, ct).ConfigureAwait(false);

        if (subAgent.Context is not null)
        {
            subAgent.Context.ParentAgentId = _subAgentContextAccessor.Current?.AgentId;
            subAgent.Context.SessionId = "default";
        }

        context.SubAgent = subAgent;

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建过滤后的 CacheSafeParams — 对齐 TS: shouldOmitClaudeMd / resolvedSystemContext
    /// Explore/Plan 等只读 Agent 省略 claudeMd 和 gitStatus 以节省 token
    /// </summary>
    private JoinCode.Abstractions.LLM.Chat.CacheSafeParams? BuildFilteredCacheSafeParams(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition)
    {
        var parentParams = _subAgentContextAccessor.Current?.CacheSafeParams;
        if (parentParams is null) return null;

        var cloned = parentParams.Clone();

        // 对齐 TS: shouldOmitClaudeMd — 只读 Agent 省略 claudeMd（~5-15 Gtok/周节省）
        var userContext = cloned.UserContext;
        if (definition?.OmitClaudeMd == true && userContext is not null)
        {
            userContext = FilterKey(userContext, "claudeMd");
        }

        // 对齐 TS: resolvedSystemContext — Explore/Plan 省略 gitStatus（~1-3 Gtok/周节省）
        var systemContext = cloned.SystemContext;
        if (definition?.OmitGitStatus == true && systemContext is not null)
        {
            systemContext = FilterKey(systemContext, "gitStatus");
        }

        return new JoinCode.Abstractions.LLM.Chat.CacheSafeParams
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

    /// <summary>
    /// 合并 AllowedTools — 对齐 TS executeForkedSkill: 调用方优先
    /// </summary>
    private static List<string>? MergeAllowedTools(IReadOnlyList<string>? callerTools, List<string>? definitionTools)
    {
        if (callerTools is not null && callerTools.Count > 0)
            return callerTools.ToList();
        return definitionTools;
    }
}
