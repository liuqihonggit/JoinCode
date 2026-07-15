namespace JoinCode.ChatCommands;

/// <summary>
/// /help 命令 - 显示所有可用命令帮助
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Help, Description = "显示可用命令帮助", Usage = "/help", Category = ChatCommandCategory.Info)]
public sealed class HelpCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Help;
    public string Description => "显示可用命令帮助";
    public string Usage => "/help";
    public string[] Aliases => ["?"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services.CommandRegistry is null)
        {
            TerminalHelper.WriteLine("命令注册表不可用。");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var commands = context.Services.CommandRegistry.GetCommandInfos().ToList();

        // 按分类分组 — 直接使用 ChatCommandInfo.Category（特性解耦，无需中央映射表）
        var categories = CategorizeCommands(commands);

        var tabs = categories.Select(c => c.Category.ToValue()).ToArray();

        var panel = new TabPanel(tabs, tabIndex =>
        {
            var cat = categories[tabIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"{AnsiStyleConstants.Bold}{cat.Category.ToValue()}{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            foreach (var cmd in cat.Commands)
            {
                sb.AppendLine($"  {cmd.Usage,-24} {cmd.Description}");
            }
            return sb.ToString();
        });

        return panel.ShowAsync(context.CancellationToken)
            .ContinueWith(_ => ChatCommandResult.Continue(), TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// 使用 ChatCommandInfo.Category 分组 — 特性解耦，每个命令自己声明分类
    /// </summary>
    private static List<CommandCategoryGroup> CategorizeCommands(List<ChatCommandInfo> commands)
    {
        // 按分类枚举顺序分组，保持一致的展示顺序
        var groups = new Dictionary<ChatCommandCategory, List<ChatCommandInfo>>();

        foreach (var cmd in commands)
        {
            if (!groups.TryGetValue(cmd.Category, out var list))
            {
                list = [];
                groups[cmd.Category] = list;
            }
            list.Add(cmd);
        }

        // 按枚举定义顺序输出（会话→模型→代码→...→其他）
        var result = new List<CommandCategoryGroup>();
        foreach (ChatCommandCategory cat in Enum.GetValues<ChatCommandCategory>())
        {
            if (groups.TryGetValue(cat, out var cmds) && cmds.Count > 0)
            {
                result.Add(new CommandCategoryGroup(cat, cmds));
            }
        }

        return result;
    }

    private sealed record CommandCategoryGroup(ChatCommandCategory Category, List<ChatCommandInfo> Commands);
}
