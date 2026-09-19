
namespace JoinCode.ChatCommands;

/// <summary>
/// /mobile 命令 — 移动端连接管理
/// 通过 IMobileConnectService 启动/停止连接服务并生成连接 URL
/// 支持别名 /ios、/android
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Mobile, Description = "移动端连接", Usage = "/mobile [start|stop|url]", Category = ChatCommandCategory.Platform, Aliases = ["ios", "android"], ArgumentHint = "start|stop|url", IsHidden = true)]
[ChatCommandArg("action", Type = "string", Description = "移动端操作", Enum = new[] { "start", "stop", "url" })]
public sealed class MobileCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /mobile 命令 — 根据子参数分发启动、停止、查看 URL、状态操作
    /// </summary>
    /// <param name="context">命令执行上下文，包含参数、会话 ID、取消令牌等</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var mobileService = ChatCommandBase.GetService<IMobileConnectService>(context);

        if (mobileService is null)
            return ChatCommandResult.Continue();

        var arg = context.Arguments?.Trim().ToLowerInvariant();

        switch (arg)
        {
            case PlatformActionEnumConstants.Start:
            case "s":
                await HandleStartAsync(mobileService).ConfigureAwait(false);
                break;
            case PlatformActionEnumConstants.Stop:
            case "d":
                HandleStop(mobileService);
                break;
            case PlatformActionEnumConstants.Url:
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
