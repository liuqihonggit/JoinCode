namespace JoinCode.ChatCommands;

/// <summary>
/// /agents 命令 — 查看和管理代理，支持列出代理列表和查看代理详情
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Agents, Description = "查看和管理代理", Usage = "/agents [list|info <name>|resume-all]", Category = ChatCommandCategory.Agent, ArgumentHint = "[list|info <name>|resume-all]")]
[ChatCommandArg("action", Type = "string", Description = "操作: list=列出代理, info=查看详情, resume-all=从历史恢复所有被中断的子代理", Enum = new[] { "list", "info", "resume-all" }, Default = "list")]
[ChatCommandArg("name", Type = "string", Description = "代理名称,仅在 action=info 时使用")]
public sealed class AgentsCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /agents 命令，根据操作类型列出代理或显示代理详情
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action) {
            case CrudActionEnumConstants.List:
            case CrudActionEnumConstants.Ls:
            await ListAgentsAsync(context).ConfigureAwait(false);
            break;
            case "info":
            await ShowAgentInfoAsync(context, args).ConfigureAwait(false);
            break;
            case "resume-all":
            await ResumeAllFromTranscriptAsync(context, args).ConfigureAwait(false);
            break;
            default:
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostAgentsUnknownAction, action)}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsAvailableActions));
            break;
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 列出代理（交互式选择器）
    /// 对齐 TS: AgentsMenu — 上下键选择代理+Enter查看详情+Esc取消
    /// </summary>
    private static async Task ListAgentsAsync(ChatCommandContext context) {
        var provider = ChatCommandBase.GetService<IAgentDefinitionProvider>(context, typeof(IAgentDefinitionProvider));
        if (provider is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}{L.T(StringKey.HostAgentsProviderUnavailable)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var agents = await provider.GetAgentDefinitionsAsync(cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (agents.Count == 0) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsNoAgents));
            return;
        }

        // 交互模式：使用 Selector 组件
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            var selector = new Selector<AgentDefinition>(
                "代理列表",
                [.. agents],
                a => a.DisplayId + (a.IsBackground ? " [后台]" : ""),
                a => string.IsNullOrEmpty(a.Description) ? a.WhenToUse : a.Description,
                enableSearch: true);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected is null) {
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

        foreach (var (source, agentList) in grouped) {
            TerminalHelper.WriteLine($"  [{source}] ({agentList.Count})");
            foreach (var agent in agentList) {
                var bgMarker = agent.IsBackground ? " [后台]" : "";
                var desc = string.IsNullOrEmpty(agent.Description) ? agent.WhenToUse : agent.Description;
                var shortDesc = desc.Length > 60 ? desc[..60] + "..." : desc;
                TerminalHelper.WriteLine($"    {agent.DisplayId}{bgMarker}");
                TerminalHelper.WriteLine($"      {shortDesc}");
            }
            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsInfoHint));
    }

    /// <summary>
    /// 显示代理详情（从选择器选择后调用）
    /// </summary>
    private static Task ShowAgentDetailAsync(AgentDefinition agent) {
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDetailHeader, agent.DisplayId) + "\n");

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

        if (agent.Tools?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsAllowedToolsLabel, agent.Tools.Count)}");
            foreach (var tool in agent.Tools)
                TerminalHelper.WriteLine($"  + {tool}");
        }

        if (agent.DisallowedTools?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsDisallowedToolsLabel, agent.DisallowedTools.Count)}");
            foreach (var tool in agent.DisallowedTools)
                TerminalHelper.WriteLine($"  - {tool}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 transcript 恢复所有被中断的子代理 — 加载历史对话+工具记录，重新 spawn 带历史的子代理
    /// </summary>
    private static async Task ResumeAllFromTranscriptAsync(ChatCommandContext context, string[] args) {
        var agentService = ChatCommandBase.GetService<IAgentService>(context, typeof(IAgentService));
        if (agentService is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}代理服务不可用{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var resumePrompt = args.Length > 1 ? string.Join(" ", args[1..]) : "请继续执行你之前被中断的任务";

        TerminalHelper.WriteLine($"{TerminalColors.Info}正在从 transcript 恢复所有被中断的子代理...{AnsiStyleEnumConstants.Reset}");

        try {
            var sessionId = string.IsNullOrEmpty(context.SessionId) ? null : context.SessionId;
            var resumed = await agentService.ResumeAllAgentsAsync(resumePrompt, sessionId, context.CancellationToken).ConfigureAwait(false);
            if (resumed.Count == 0) {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}没有需要恢复的子代理（所有子代理已在运行中或已完成）{AnsiStyleEnumConstants.Reset}");
                return;
            }
            TerminalHelper.WriteLine($"{TerminalColors.Success}已从 transcript 恢复 {resumed.Count} 个子代理:{AnsiStyleEnumConstants.Reset}");
            foreach (var a in resumed)
                TerminalHelper.WriteLine($"  ● {a.Id} — {a.Description}");
        } catch (Exception ex) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}批量恢复失败: {ex.Message}{AnsiStyleEnumConstants.Reset}");
        }
    }

    private static async Task ShowAgentInfoAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostAgentsUsageHint)}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var agentType = args[1];
        var provider = ChatCommandBase.GetService<IAgentDefinitionProvider>(context, typeof(IAgentDefinitionProvider));
        if (provider is null) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsProviderUnavailable));
            return;
        }

        var agent = await provider.GetAgentDefinitionAsync(JoinCode.Abstractions.Models.Agent.AgentRole.Executor, ExecutorVariantExtensions.FromValue(agentType), cancellationToken: context.CancellationToken).ConfigureAwait(false);
        if (agent is null) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsNotFound, agentType));
            return;
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDetailHeader, agent.DisplayId) + "\n");

        if (!string.IsNullOrEmpty(agent.Description)) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsDescriptionLabel, agent.Description));
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsWhenToUseLabel, agent.WhenToUse));
        TerminalHelper.WriteLine(L.T(StringKey.HostAgentsBackgroundLabel, agent.IsBackground ? "是" : "否"));

        if (!string.IsNullOrEmpty(agent.ModelName)) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsModelLabel, agent.ModelName));
        }

        if (agent.Temperature.HasValue) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsTemperatureLabel, agent.Temperature.Value));
        }

        if (agent.MaxTokens.HasValue) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsMaxTokensLabel, agent.MaxTokens.Value));
        }

        if (!string.IsNullOrEmpty(agent.PermissionMode)) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsPermissionModeLabel, agent.PermissionMode));
        }

        if (!string.IsNullOrEmpty(agent.SourcePath)) {
            TerminalHelper.WriteLine(L.T(StringKey.HostAgentsSourceLabel, agent.SourcePath));
        }

        if (agent.Tools?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsAllowedToolsLabel, agent.Tools.Count)}");
            foreach (var tool in agent.Tools) {
                TerminalHelper.WriteLine($"  + {tool}");
            }
        }

        if (agent.DisallowedTools?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsDisallowedToolsLabel, agent.DisallowedTools.Count)}");
            foreach (var tool in agent.DisallowedTools) {
                TerminalHelper.WriteLine($"  - {tool}");
            }
        }

        if (agent.Skills?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsSkillsLabel)}");
            foreach (var skill in agent.Skills) {
                TerminalHelper.WriteLine($"  /{skill}");
            }
        }

        if (agent.McpServers?.Count > 0) {
            TerminalHelper.WriteLine($"\n{L.T(StringKey.HostAgentsMcServersLabel)}");
            foreach (var server in agent.McpServers) {
                TerminalHelper.WriteLine($"  {server.ServerNameRef ?? "(inline)"}");
            }
        }
    }

    private static List<(string Source, List<AgentDefinition> Agents)> GroupBySource(List<AgentDefinition> agents) {
        var groups = new Dictionary<string, List<AgentDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents) {
            var source = ClassifySource(agent.SourcePath);
            if (!groups.TryGetValue(source, out var list)) {
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

    private static string ClassifySource(string? sourcePath) {
        if (string.IsNullOrEmpty(sourcePath)) return "内置";

        var span = sourcePath.AsSpan();

        if (span.Contains(".trae".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            span.Contains(AppDataConstants.AppDataFolder.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
            return IsInUserProfile(sourcePath) ? "用户级" : "项目级";
        }

        if (span.Contains(".claude".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
            return IsInUserProfile(sourcePath) ? "用户级 (claude)" : "项目级 (claude)";
        }

        return "其他";
    }

    private static bool IsInUserProfile(string path) {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase);
    }
}