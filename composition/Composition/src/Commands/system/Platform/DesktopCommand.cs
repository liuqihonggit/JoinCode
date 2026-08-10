
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Desktop, Description = "将会话转移到桌面应用", Usage = "/desktop", Category = ChatCommandCategory.Platform, Aliases = ["app"], IsHidden = true)]
public sealed class DesktopCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var handoffService = ChatCommandBase.GetService<IDesktopHandoffService>(context);

        if (handoffService is null)
            return ChatCommandResult.Continue();

        if (!handoffService.IsDesktopAvailable)
        {
            TerminalHelper.WriteLine("未检测到桌面应用。");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("  请安装 jcc-desktop 桌面应用");
            TerminalHelper.WriteLine("  或使用 /bridge 命令通过命名管道连接");
            return ChatCommandResult.Continue();
        }

        var sessionId = context.SessionId ?? "default";
        var success = await handoffService.HandoffToDesktopAsync(sessionId).ConfigureAwait(false);

        if (success)
        {
            TerminalHelper.WriteLine($"会话 {sessionId} 已转移到桌面应用");
            TerminalHelper.WriteLine(handoffService.DesktopConnectionInfo);
        }
        else
        {
            TerminalHelper.WriteLine("会话转移失败，请检查桌面应用是否正在运行");
        }

        return ChatCommandResult.Continue();
    }
}
