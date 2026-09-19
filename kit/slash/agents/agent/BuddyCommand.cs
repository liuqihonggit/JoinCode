
namespace JoinCode.ChatCommands;

/// <summary>
/// /buddy 命令 — 查看当前用户的伙伴精灵信息
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Buddy, Description = "查看你的伙伴精灵", Usage = "/buddy", Category = ChatCommandCategory.Agent, IsHidden = true)]
public sealed class BuddyCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /buddy 命令，显示当前用户的伙伴精灵 ASCII 艺术、名称、稀有度等信息
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var buddyService = ChatCommandBase.GetService<IBuddyService>(context);

        if (buddyService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        var userId = context.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var buddy = buddyService.GetBuddy(userId);

        TerminalHelper.WriteLine(buddy.AsciiArt);
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"  {buddy.Name} ({buddy.Species})");
        TerminalHelper.WriteLine($"  稀有度: {buddy.Rarity}{(buddy.Shiny ? " ✨闪亮!" : "")}");
        TerminalHelper.WriteLine($"  眼睛: {buddy.Eye}  帽子: {(string.IsNullOrEmpty(buddy.Hat) ? "无" : buddy.Hat)}");

        return Task.FromResult(ChatCommandResult.Continue());
    }
}