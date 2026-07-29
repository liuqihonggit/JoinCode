
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Buddy, Description = "查看你的伙伴精灵", Usage = "/buddy", Category = ChatCommandCategory.Agent, IsHidden = true)]
public sealed class BuddyCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var buddyService = ChatCommandBase.GetService<IBuddyService>(context);

        if (buddyService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        var userId = context.SessionId ?? "default";
        var buddy = buddyService.GetBuddy(userId);

        TerminalHelper.WriteLine(buddy.AsciiArt);
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"  {buddy.Name} ({buddy.Species})");
        TerminalHelper.WriteLine($"  稀有度: {buddy.Rarity}{(buddy.Shiny ? " ✨闪亮!" : "")}");
        TerminalHelper.WriteLine($"  眼睛: {buddy.Eye}  帽子: {(string.IsNullOrEmpty(buddy.Hat) ? "无" : buddy.Hat)}");

        return Task.FromResult(ChatCommandResult.Continue());
    }
}
