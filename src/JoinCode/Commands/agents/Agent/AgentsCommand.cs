namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Agents, Description = "查看和管理代理", Usage = "/agents [list|info <name>]", Category = ChatCommandCategory.Agent)]
public sealed class AgentsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Agents;
    public string Description => "查看和管理代理";
    public string Usage => "/agents [list|info <name>]";
    public string[] Aliases => [];
    public string ArgumentHint => "[list|info <name>]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
                await ListAgentsAsync(context);
                break;
            case "info":
                await ShowAgentInfoAsync(context, args);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostAgentsUnknownAction, action)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine(L.T(StringKey.HostAgentsAvailableActions));
                break;
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 列出代理（交互式选择器）
    /// 对齐 TS: AgentsMenu — 上下键选择代理+Enter查看详情+Esc取消
    /// </summary>
    private static async Task ListAgentsAsync(ChatCommandContext context)
    {
        var provider = ChatCommandBase.GetService<IAgentDefinitionProvider>(context, typeof(IAgentDefinitionProvider));
        if (provider is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostAgentsProviderUnavailable)}{AnsiStyleConstants.Reset}");
            return;
        }

        var agents = await provider.GetAgentDefinitionsAsync(cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (agents.Count == 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsNoAgents));
            return;
        }

        // 交互模式：使用 Selector 组件
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var selector = new Selector<AgentDefinition>(
                "代理列表",
                [.. agents],
                a => a.AgentType + (a.IsBackground ? " [后台]" : ""),
                a => string.IsNullOrEmpty(a.Description) ? a.WhenToUse : a.Description,
                enableSearch: true);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected is null)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostAgentsCancelled));
                return;
            }

            // 选择后显示详情
            await ShowAgentDetailAsync(result.Selected).ConfigureAwait(false);
            return;
        }

        // 非交互模式回退：纯文本列表
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsListHeader) + "\n");

        var grouped = GroupBySource(agents);

        foreach (var (source, agentList) in grouped)
        {
            TerminalHelper.WriteLine($"  [{source}] ({agentList.Count})");
            foreach (var agent in agentList)
            {
                var bgMarker = agent.IsBackground ? " [后台]" : "";
                var desc = string.IsNullOrEmpty(agent.Description) ? agent.WhenToUse : agent.Description;
                var shortDesc = desc.Length > 60 ? desc[..60] + "..." : desc;
                TerminalHelper.WriteLine($"    {agent.AgentType}{bgMarker}");
                TerminalHelper.WriteLine($"      {shortDesc}");
            }
            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsInfoHint));
    }

    /// <summary>
    /// 显示代理详情（从选择器选择后调用）
    /// </summary>
    private static Task ShowAgentDetailAsync(AgentDefinition agent)
    {
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDetailHeader, agent.AgentType) + "\n");

        if (!string.IsNullOrEmpty(agent.Description))
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDescriptionLabel, agent.Description));

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsWhenToUseLabel, agent.WhenToUse));
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsBackgroundLabel, agent.IsBackground ? "是" : "否"));

        if (!string.IsNullOrEmpty(agent.ModelName))
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsModelLabel, agent.ModelName));

        if (agent.Temperature.HasValue)
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsTemperatureLabel, agent.Temperature.Value));

        if (agent.MaxTokens.HasValue)
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsMaxTokensLabel, agent.MaxTokens.Value));

        if (!string.IsNullOrEmpty(agent.PermissionMode))
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsPermissionModeLabel, agent.PermissionMode));

        if (!string.IsNullOrEmpty(agent.SourcePath))
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsSourceLabel, agent.SourcePath));

        if (agent.Tools?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsAllowedToolsLabel, agent.Tools.Count)}");
            foreach (var tool in agent.Tools)
                TerminalHelper.WriteLine($"  + {tool}");
        }

        if (agent.DisallowedTools?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsDisallowedToolsLabel, agent.DisallowedTools.Count)}");
            foreach (var tool in agent.DisallowedTools)
                TerminalHelper.WriteLine($"  - {tool}");
        }

        return Task.CompletedTask;
    }

    private static async Task ShowAgentInfoAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostAgentsUsageHint)}{AnsiStyleConstants.Reset}");
            return;
        }

        var agentType = args[1];
        var provider = ChatCommandBase.GetService<IAgentDefinitionProvider>(context, typeof(IAgentDefinitionProvider));
        if (provider is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsProviderUnavailable));
            return;
        }

        var agent = await provider.GetAgentDefinitionAsync(agentType, cancellationToken: context.CancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsNotFound, agentType));
            return;
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDetailHeader, agent.AgentType) + "\n");

        if (!string.IsNullOrEmpty(agent.Description))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDescriptionLabel, agent.Description));
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsWhenToUseLabel, agent.WhenToUse));
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsBackgroundLabel, agent.IsBackground ? "是" : "否"));

        if (!string.IsNullOrEmpty(agent.ModelName))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsModelLabel, agent.ModelName));
        }

        if (agent.Temperature.HasValue)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsTemperatureLabel, agent.Temperature.Value));
        }

        if (agent.MaxTokens.HasValue)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsMaxTokensLabel, agent.MaxTokens.Value));
        }

        if (!string.IsNullOrEmpty(agent.PermissionMode))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsPermissionModeLabel, agent.PermissionMode));
        }

        if (!string.IsNullOrEmpty(agent.SourcePath))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsSourceLabel, agent.SourcePath));
        }

        if (agent.Tools?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsAllowedToolsLabel, agent.Tools.Count)}");
            foreach (var tool in agent.Tools)
            {
                TerminalHelper.WriteLine($"  + {tool}");
            }
        }

        if (agent.DisallowedTools?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsDisallowedToolsLabel, agent.DisallowedTools.Count)}");
            foreach (var tool in agent.DisallowedTools)
            {
                TerminalHelper.WriteLine($"  - {tool}");
            }
        }

        if (agent.Skills?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsSkillsLabel)}");
            foreach (var skill in agent.Skills)
            {
                TerminalHelper.WriteLine($"  /{skill}");
            }
        }

        if (agent.McpServers?.Count > 0)
        {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsMcServersLabel)}");
            foreach (var server in agent.McpServers)
            {
                TerminalHelper.WriteLine($"  {server.ServerNameRef ?? "(inline)"}");
            }
        }
    }

    private static List<(string Source, List<AgentDefinition> Agents)> GroupBySource(List<AgentDefinition> agents)
    {
        var groups = new Dictionary<string, List<AgentDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            var source = ClassifySource(agent.SourcePath);
            if (!groups.TryGetValue(source, out var list))
            {
                list = [];
                groups[source] = list;
            }
            list.Add(agent);
        }

        return groups
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Value))
            .ToList();
    }

    private static string ClassifySource(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return "内置";

        var span = sourcePath.AsSpan();

        if (span.Contains(".trae".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            span.Contains(AppDataConstants.AppDataFolder.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return IsInUserProfile(sourcePath) ? "用户级" : "项目级";
        }

        if (span.Contains(".claude".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return IsInUserProfile(sourcePath) ? "用户级 (claude)" : "项目级 (claude)";
        }

        return "其他";
    }

    private static bool IsInUserProfile(string path)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase);
    }
}
