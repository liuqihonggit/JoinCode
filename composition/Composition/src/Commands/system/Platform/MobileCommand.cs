
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Mobile, Description = "移动端连接", Usage = "/mobile [start|stop|url]", Category = ChatCommandCategory.Platform, Aliases = ["ios", "android"], ArgumentHint = "start|stop|url", IsHidden = true)]
public sealed class MobileCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var mobileService = ChatCommandBase.GetService<IMobileConnectService>(context);

        if (mobileService is null)
            return ChatCommandResult.Continue();

        var arg = context.Arguments?.Trim().ToLowerInvariant();

        switch (arg)
        {
            case PlatformActionConstants.Start:
            case "s":
                await HandleStartAsync(mobileService).ConfigureAwait(false);
                break;
            case PlatformActionConstants.Stop:
            case "d":
                HandleStop(mobileService);
                break;
            case PlatformActionConstants.Url:
            case "u":
                HandleUrl(mobileService);
                break;
            case null:
            case "":
                HandleStatus(mobileService);
                break;
            default:
                TerminalHelper.WriteLine($"未知参数: {context.Arguments}");
                TerminalHelper.WriteLine("用法: /mobile [start|stop|url]");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task HandleStartAsync(IMobileConnectService mobileService)
    {
        if (mobileService.IsServerRunning)
        {
            TerminalHelper.WriteLine("移动端连接服务已在运行中");
            return;
        }

        var port = await mobileService.StartConnectServerAsync().ConfigureAwait(false);
        var url = mobileService.GenerateConnectUrl(port);

        TerminalHelper.WriteLine("移动端连接服务已启动");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"  连接地址: {url}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("  请在移动设备上打开上述地址进行连接");
    }

    private static void HandleStop(IMobileConnectService mobileService)
    {
        if (!mobileService.IsServerRunning)
        {
            TerminalHelper.WriteLine("移动端连接服务未在运行");
            return;
        }

        mobileService.StopConnectServer();
        TerminalHelper.WriteLine("移动端连接服务已停止");
    }

    private static void HandleUrl(IMobileConnectService mobileService)
    {
        if (!mobileService.IsServerRunning)
        {
            TerminalHelper.WriteLine("移动端连接服务未在运行，请先 /mobile start");
            return;
        }

        var url = mobileService.GenerateConnectUrl(0);
        TerminalHelper.WriteLine($"  连接地址: {url}");
    }

    private static void HandleStatus(IMobileConnectService mobileService)
    {
        TerminalHelper.WriteLine($"  服务状态: {(mobileService.IsServerRunning ? "运行中" : "已停止")}");
    }
}
