namespace Core.Agents;

using JoinCode.Abstractions.Interfaces;

/// <summary>
/// 定义解析中间件 — 从 IAgentRoleRegistry 获取角色档案，回退到 IAgentDefinitionProvider
/// </summary>
[Register]
public sealed partial class DefinitionResolutionMiddleware : ServiceEntity, IAgentSpawnMiddleware
{
    [Inject] private readonly IAgentRoleRegistry _roleRegistry;
    [Inject] private readonly IAgentDefinitionProvider? _definitionProvider;

    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Propagate;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        var profile = _roleRegistry.GetProfile(context.Options.Role, context.Options.Variant);

        if (profile is not null)
        {
            context.Definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
            {
                Role = profile.Role,
                Variant = profile.Variant,
                WhenToUse = profile.WhenToUse,
                Description = profile.Description,
                SystemPrompt = profile.SystemPrompt,
                Tools = profile.AllowedTools?.ToList(),
                DisallowedTools = profile.DisallowedTools?.ToList(),
                PermissionMode = profile.PermissionMode,
                IsBackground = profile.IsBackground,
                OmitClaudeMd = profile.OmitClaudeMd,
                OmitGitStatus = profile.OmitGitStatus,
                ModelName = profile.ModelName,
                Temperature = profile.Temperature,
                MaxTokens = profile.MaxTokens,
                Memory = profile.Memory,
                Skills = profile.Skills?.ToList(),
                SourcePath = profile.SourcePath,
            };
        }
        else if (_definitionProvider is not null)
        {
            context.Definition = await _definitionProvider.GetAgentDefinitionAsync(
                context.Options.Role, context.Options.Variant, cancellationToken: ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
