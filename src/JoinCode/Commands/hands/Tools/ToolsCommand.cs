
namespace JoinCode.ChatCommands;

/// <summary>
/// /tools 命令 - 显示可用工具列表及参数
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Tools, Description = "显示可用工具列表", Usage = "/tools", Category = ChatCommandCategory.Tools)]
public sealed class ToolsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Tools;
    public string Description => "显示可用工具列表";
    public string Usage => "/tools";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services!.ToolRegistry is null)
        {
            TerminalHelper.WriteLine("工具注册表不可用。");
            return ChatCommandResult.Continue();
        }

        var tools = await context.Services!.ToolRegistry.GetAllToolInfosAsync(context.CancellationToken);

        if (tools.Count == 0)
        {
            TerminalHelper.WriteLine("没有注册的工具。");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"=== 可用工具 ({tools.Count}) ===");
        TerminalHelper.NewLine();

        foreach (var tool in tools)
        {
            TerminalHelper.WriteLine($"  {ObjectSymbol.Gear.ToValue()} {tool.Name}");
            TerminalHelper.WriteLine($"     描述: {tool.Description}");

            if (tool.InputSchema.Properties.Count > 0)
            {
                TerminalHelper.WriteLine("     参数:");
                var requiredSet = tool.InputSchema.Required is { Count: > 0 }
                    ? new HashSet<string>(tool.InputSchema.Required)
                    : null;
                foreach (var param in tool.InputSchema.Properties)
                {
                    var requiredTag = requiredSet is not null && requiredSet.Contains(param.Key) ? "(必需)" : "(可选)";
                    TerminalHelper.WriteLine($"       - {param.Key}: {param.Value.Type} {requiredTag}");
                }
            }

            TerminalHelper.NewLine();
        }

        return ChatCommandResult.Continue();
    }
}
