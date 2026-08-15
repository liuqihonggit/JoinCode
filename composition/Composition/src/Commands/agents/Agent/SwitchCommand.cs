namespace JoinCode.ChatCommands;

/// <summary>
/// /switch 命令 — 切换前台输出显示模式
/// /switch agentName — 只看指定子代理输出
/// /switch all — 切回显示全部
/// /switch — 显示当前模式和活跃子代理列表
/// </summary>
[ChatCommand(
    Name = ChatCommandNameConstants.Switch,
    Description = "切换查看指定子代理输出",
    Usage = "/switch [agentName|all]",
    Category = ChatCommandCategory.Agent,
    ArgumentHint = "[agentName|all]")]
public sealed class SwitchCommand : ChatCommandBase
{
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var outputManager = GetService<JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager>(context);
        if (outputManager is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}IAgentOutputChannelManager 服务未初始化{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var args = GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args))
        {
            ShowCurrentMode(outputManager);
            return ChatCommandResult.Continue();
        }

        if (string.Equals(args, "all", StringComparison.OrdinalIgnoreCase))
        {
            outputManager.SetDisplayMode(null);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已切换到显示全部子代理输出{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var agentService = GetService<JoinCode.Abstractions.Interfaces.IAgentService>(context);
        if (agentService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}IAgentService 服务未初始化{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var agentId = await agentService.FindAgentIdByNameAsync(args, context.CancellationToken).ConfigureAwait(false);
        if (agentId is not null)
        {
            outputManager.SetDisplayMode(agentId);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已切换到只看子代理 {args} 的输出{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}未找到子代理 {args}{AnsiStyleConstants.Reset}");
            ShowCurrentMode(outputManager);
        }

        return ChatCommandResult.Continue();
    }

    private static void ShowCurrentMode(JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager outputManager)
    {
        var current = outputManager.GetDisplayMode();
        if (current is null)
        {
            TerminalHelper.WriteLine("当前模式: 显示全部子代理输出");
        }
        else
        {
            TerminalHelper.WriteLine($"当前模式: 只看子代理 {current} 的输出");
        }

        var agents = outputManager.GetActiveAgents();
        if (agents.Count == 0)
        {
            TerminalHelper.WriteLine("当前无活跃子代理");
        }
        else
        {
            TerminalHelper.WriteLine("活跃子代理:");
            foreach (var agent in agents)
            {
                var marker = string.Equals(agent.AgentId, current, StringComparison.OrdinalIgnoreCase) ? " *" : "";
                TerminalHelper.WriteLine($"  {agent.DisplayName ?? agent.AgentId}{marker}");
            }
            TerminalHelper.WriteLine("  (* = 当前选中)");
        }
    }
}
