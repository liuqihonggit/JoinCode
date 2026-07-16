
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Stickers, Description = "获取贴纸", Usage = "/stickers", Category = ChatCommandCategory.Social)]
public sealed class StickersCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var stickerService = ChatCommandBase.GetService<IStickerService>(context);

        if (stickerService is null)
            return ChatCommandResult.Continue();

        var success = await stickerService.OpenStickerPageAsync().ConfigureAwait(false);

        if (success)
        {
            TerminalHelper.WriteLine("正在浏览器中打开贴纸页面...");
        }
        else
        {
            TerminalHelper.WriteLine($"打开浏览器失败，请手动访问: {stickerService.GetStickerPageUrl()}");
        }

        return ChatCommandResult.Continue();
    }
}
