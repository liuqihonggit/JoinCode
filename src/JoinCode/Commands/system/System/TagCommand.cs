
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Tag, Description = "为当前会话添加或管理标签", Usage = "/tag [add|remove|list] [tag_name]", Category = ChatCommandCategory.System)]
public sealed class TagCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Tag;
    public string Description => "为当前会话添加或管理标签";
    public string Usage => "/tag [add|remove|list] [tag_name]";
    public string[] Aliases => [];
    public string ArgumentHint => "[add|remove|list] [tag]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);
        var tagService = context.Services.SessionTagService;

        if (string.IsNullOrEmpty(args) || args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return ListTagsAsync(context, tagService);
        }

        if (tagService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("会话标签服务未初始化");
            }
            return Task.FromResult(ChatCommandResult.Continue());
        }

        if (args.StartsWith("add", StringComparison.OrdinalIgnoreCase))
        {
            var tagName = args["add".Length..].Trim();
            if (string.IsNullOrEmpty(tagName))
            {
                TerminalHelper.WriteLine("用法: /tag add <tag_name>");
                return Task.FromResult(ChatCommandResult.Continue());
            }

            var added = tagService.AddTag(context.SessionId, tagName);
            if (added)
            {
                TerminalHelper.WriteLine($"已添加标签: {tagName}");
            }
            else
            {
                TerminalHelper.WriteLine($"标签已存在: {tagName}");
            }
        }
        else if (args.StartsWith("remove", StringComparison.OrdinalIgnoreCase))
        {
            var tagName = args["remove".Length..].Trim();
            if (string.IsNullOrEmpty(tagName))
            {
                TerminalHelper.WriteLine("用法: /tag remove <tag_name>");
                return Task.FromResult(ChatCommandResult.Continue());
            }

            var removed = tagService.RemoveTag(context.SessionId, tagName);
            if (removed)
            {
                TerminalHelper.WriteLine($"已移除标签: {tagName}");
            }
            else
            {
                TerminalHelper.WriteLine($"标签不存在: {tagName}");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"未知操作: {args}");
            TerminalHelper.WriteLine("支持: add, remove, list");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static Task<ChatCommandResult> ListTagsAsync(ChatCommandContext context, ISessionTagService? tagService)
    {
        TerminalHelper.WriteLine("会话标签:");

        if (tagService is null)
        {
            TerminalHelper.WriteLine("  会话标签服务未初始化");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var tags = tagService.GetTags(context.SessionId);
        if (tags.Count == 0)
        {
            TerminalHelper.WriteLine("  (暂无标签)");
        }
        else
        {
            foreach (var tag in tags)
            {
                TerminalHelper.WriteLine($"  #{tag}");
            }
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /tag add <name> 添加标签");
        TerminalHelper.WriteLine("使用 /tag remove <name> 移除标签");
        return Task.FromResult(ChatCommandResult.Continue());
    }
}
