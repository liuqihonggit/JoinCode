namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Skills, Description = "查看可用技能（自定义命令）", Usage = "/skills [info <skill-name>]", Category = ChatCommandCategory.Tools)]
public sealed class SkillsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Skills;
    public string Description => "查看可用技能（自定义命令）";
    public string Usage => "/skills [info <skill-name>]";
    public string[] Aliases => ["skill"];
    public string ArgumentHint => "[info <skill-name>]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);

        if (args.Length > 0 && args[0].Equals("info", StringComparison.OrdinalIgnoreCase))
        {
            ShowSkillInfo(context, args);
            return ChatCommandResult.Continue();
        }

        await ListSkillsAsync(context).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 列出技能（交互式选择器）
    /// 对齐 TS: SkillsMenu — 上下键选择技能+Enter查看详情+Esc取消
    /// </summary>
    private static async Task ListSkillsAsync(ChatCommandContext context)
    {
        var customCommands = GetCustomCommands(context);

        if (customCommands.Count == 0)
        {
            TerminalHelper.WriteLine("  当前无自定义技能");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("  创建技能:");
            TerminalHelper.WriteLine("    项目级: .trae/commands/<skill-name>.md");
            TerminalHelper.WriteLine("    用户级: ~/.jcc/commands/<skill-name>.md");
            return;
        }

        // 交互模式：使用 Selector 组件
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var selector = new Selector<CustomCommand>(
                "可用技能",
                [.. customCommands],
                c => "/" + c.FullName,
                c => c.Description,
                enableSearch: true);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected is null)
            {
                TerminalHelper.WriteLine("已取消");
                return;
            }

            // 选择后显示详情
            ShowSkillDetail(result.Selected);
            return;
        }

        // 非交互模式回退：纯文本列表
        TerminalHelper.WriteLine("=== 可用技能 ===\n");

        var grouped = GroupBySource(customCommands);

        foreach (var (source, commands) in grouped)
        {
            TerminalHelper.WriteLine($"  [{source}]");
            foreach (var cmd in commands)
            {
                var desc = string.IsNullOrEmpty(cmd.Description) ? "" : $" - {cmd.Description}";
                TerminalHelper.WriteLine($"    /{cmd.FullName}{desc}");
            }
            TerminalHelper.NewLine();
        }

        TerminalHelper.WriteLine("使用 /skills info <skill-name> 查看详细信息");
    }

    /// <summary>
    /// 显示技能详情（从选择器选择后调用）
    /// </summary>
    private static void ShowSkillDetail(CustomCommand match)
    {
        TerminalHelper.WriteLine($"名称: {match.FullName}");
        if (!string.IsNullOrEmpty(match.Description))
            TerminalHelper.WriteLine($"描述: {match.Description}");
        TerminalHelper.WriteLine($"来源: {match.SourcePath}");
        TerminalHelper.WriteLine($"禁用模型调用: {(match.DisableModelInvocation ? "是" : "否")}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("内容预览:");
        var preview = match.Content.Length > 200 ? match.Content[..200] + "..." : match.Content;
        TerminalHelper.WriteLine($"  {preview}");
    }

    private static void ShowSkillInfo(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /skills info <skill-name>{AnsiStyleConstants.Reset}");
            return;
        }

        var skillName = args[1];
        var customCommands = GetCustomCommands(context);
        var match = customCommands.Find(c =>
            c.FullName.Equals(skillName, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            TerminalHelper.WriteLine($"未知技能: {skillName}");
            return;
        }

        TerminalHelper.WriteLine($"名称: {match.FullName}");
        if (!string.IsNullOrEmpty(match.Description))
        {
            TerminalHelper.WriteLine($"描述: {match.Description}");
        }
        TerminalHelper.WriteLine($"来源: {match.SourcePath}");
        TerminalHelper.WriteLine($"禁用模型调用: {(match.DisableModelInvocation ? "是" : "否")}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("内容预览:");
        var preview = match.Content.Length > 200 ? match.Content[..200] + "..." : match.Content;
        TerminalHelper.WriteLine($"  {preview}");
    }

    private static List<CustomCommand> GetCustomCommands(ChatCommandContext context)
    {
        var commands = new List<CustomCommand>();
        var registry = context.Services!.CommandRegistry;
        if (registry is null) return commands;

        foreach (var (_, cmd) in registry.GetAllCommands())
        {
            if (cmd is CustomChatCommand customCmd)
            {
                commands.Add(customCmd.Command);
            }
        }

        return commands;
    }

    private static List<(string Source, List<CustomCommand> Commands)> GroupBySource(List<CustomCommand> commands)
    {
        var groups = new Dictionary<string, List<CustomCommand>>(StringComparer.OrdinalIgnoreCase);

        foreach (var cmd in commands)
        {
            var source = ClassifySource(cmd.SourcePath);
            if (!groups.TryGetValue(source, out var list))
            {
                list = [];
                groups[source] = list;
            }
            list.Add(cmd);
        }

        return groups
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Value))
            .ToList();
    }

    private static string ClassifySource(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return "unknown";

        var span = sourcePath.AsSpan();

        if (span.Contains(".trae".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            span.Contains(AppDataConstants.AppDataFolder.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            if (span.Contains("commands".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return IsInUserProfile(sourcePath) ? "用户级" : "项目级";
            }
        }

        if (span.Contains(".claude".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return IsInUserProfile(sourcePath) ? "用户级 (claude)" : "项目级 (claude)";
        }

        if (span.Contains(".codex".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return IsInUserProfile(sourcePath) ? "用户级 (codex)" : "项目级 (codex)";
        }

        return "其他";
    }

    private static bool IsInUserProfile(string path)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase);
    }
}
