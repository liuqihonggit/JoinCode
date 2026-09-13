
namespace JoinCode.ChatCommands;

/// <summary>
/// /stickers 命令 — 打开贴纸页面
/// 通过 IStickerService 在浏览器中打开贴纸获取页面
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Stickers, Description = "获取贴纸", Usage = "/stickers", Category = ChatCommandCategory.Social)]
public sealed class StickersCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /stickers 命令 — 调用贴纸服务打开浏览器页面，失败时输出手动访问 URL
    /// </summary>
    /// <param name="context">命令执行上下文，包含取消令牌</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
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
