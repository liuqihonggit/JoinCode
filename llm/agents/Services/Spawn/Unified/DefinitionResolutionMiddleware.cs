namespace Core.Agents;

/// <summary>
/// 定义解析中间件 — 从 IAgentRoleRegistry 获取角色档案，回退到 IAgentDefinitionProvider
/// 统一管道版本：主代理 no-op，路径 B（SubOptions 模式）no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DefinitionResolutionMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    /// <summary>
    /// 构造 DefinitionResolutionMiddleware 实例，注入角色注册表及可选的定义提供者
    /// </summary>
    public DefinitionResolutionMiddleware(IAgentRoleRegistry roleRegistry, IAgentDefinitionProvider? definitionProvider = null)
    {
        _roleRegistry = roleRegistry;
        _definitionProvider = definitionProvider;
    }
    private readonly IAgentRoleRegistry _roleRegistry;
    private readonly IAgentDefinitionProvider? _definitionProvider;

    /// <summary>中间件错误处理策略：向上传播</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 执行定义解析：主代理或无 SpawnOptions 跳过；否则从角色注册表或定义提供者获取定义
    /// </summary>
    /// <param name="context">统一 Spawn 上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.IsMainAgent || context.SpawnOptions is null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var profile = _roleRegistry.GetProfile(context.SpawnOptions.Role, context.SpawnOptions.Variant);

        if (profile is not null)
        {
            context.Definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
            {
                Role = profile.Role,
                Variant = profile.Variant,
                WhenToUse = profile.WhenToUse,
                Description = profile.Description,
                SystemPrompt = profile.SystemPrompt,
                Tools = profile.AllowedTools?.ToList() ?? [],
                DisallowedTools = profile.DisallowedTools?.ToList() ?? [],
                PermissionMode = profile.PermissionMode,
                IsBackground = profile.IsBackground,
                OmitProjectRules = profile.OmitProjectRules,
                OmitGitStatus = profile.OmitGitStatus,
                ModelName = profile.ModelName,
                Temperature = profile.Temperature,
                MaxTokens = profile.MaxTokens,
                Memory = profile.Memory,
                Skills = profile.Skills?.ToList() ?? [],
                SourcePath = profile.SourcePath,
            };
        }
        else if (_definitionProvider is not null)
        {
            context.Definition = await _definitionProvider.GetAgentDefinitionAsync(
                context.SpawnOptions.Role, context.SpawnOptions.Variant, cancellationToken: ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
