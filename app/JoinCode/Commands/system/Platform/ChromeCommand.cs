
namespace JoinCode.ChatCommands;

/// <summary>
/// /chrome 命令 — 对齐 TS chrome/
/// TS 使用 CDP 协议 + Chrome DevTools 进行浏览器自动化
/// 对齐内容：connect+disconnect+install+toggle+status
/// 架构差异：TS 有 React 交互式浏览器控制面板，C# 为命令行操作
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Chrome, Description = "Chrome 浏览器集成", Usage = "/chrome [connect|disconnect|install|toggle|status]", Category = ChatCommandCategory.Platform, ArgumentHint = "connect|disconnect|install|toggle|status", IsHidden = true)]
public sealed class ChromeCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var chromeService = ChatCommandBase.GetService<IChromeIntegrationService>(context);

        if (chromeService is null)
            return ChatCommandResult.Continue();

        var arg = context.Arguments?.Trim().ToLowerInvariant();

        switch (arg)
        {
            case PlatformActionConstants.Connect:
            case "c":
                await HandleConnectionAsync(chromeService, ToggleAction.On).ConfigureAwait(false);
                break;
            case PlatformActionConstants.Disconnect:
            case "d":
                await HandleConnectionAsync(chromeService, ToggleAction.Off).ConfigureAwait(false);
                break;
            case PlatformActionConstants.Install:
            case "i":
                await HandleInstallAsync(chromeService).ConfigureAwait(false);
                break;
            case PlatformActionConstants.Toggle:
            case "t":
                await HandleToggleAsync(chromeService).ConfigureAwait(false);
                break;
            case PlatformActionConstants.Status:
            case "s":
            case null:
            case "":
                HandleStatus(chromeService);
                break;
            default:
                TerminalHelper.WriteLine($"未知参数: {context.Arguments}");
                TerminalHelper.WriteLine("用法: /chrome [connect|disconnect|install|toggle|status]");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task HandleConnectionAsync(IChromeIntegrationService chromeService, ToggleAction action)
    {
        if (action == ToggleAction.On)
        {
            if (!chromeService.IsExtensionInstalled)
            {
                TerminalHelper.WriteLine("Chrome 扩展未检测到，请先安装: /chrome install");
                return;
            }

            var connected = await chromeService.ConnectAsync().ConfigureAwait(false);
            TerminalHelper.WriteLine(connected ? "Chrome 扩展已连接" : "Chrome 扩展连接失败");
        }
        else
        {
            await chromeService.DisconnectAsync().ConfigureAwait(false);
            TerminalHelper.WriteLine("Chrome 扩展已断开");
        }
    }

    private static async Task HandleInstallAsync(IChromeIntegrationService chromeService)
    {
        TerminalHelper.WriteLine("正在打开 Chrome 扩展安装页面...");
        await chromeService.OpenExtensionPageAsync().ConfigureAwait(false);
        TerminalHelper.WriteLine("安装完成后，使用 /chrome connect 连接");
    }

    private static async Task HandleToggleAsync(IChromeIntegrationService chromeService)
    {
        var enabled = await chromeService.ToggleDefaultEnabledAsync().ConfigureAwait(false);
        TerminalHelper.WriteLine($"Chrome 默认启用: {(enabled ? "是" : "否")}");
    }

    private static void HandleStatus(IChromeIntegrationService chromeService)
    {
        TerminalHelper.WriteLine("Chrome 集成状态:");
        TerminalHelper.WriteLine($"  扩展: {(chromeService.IsExtensionInstalled ? "已安装" : "未安装")}");
        TerminalHelper.WriteLine($"  连接: {(chromeService.IsConnected ? "已连接" : "未连接")}");
        TerminalHelper.WriteLine($"  默认启用: {(chromeService.IsDefaultEnabled ? "是" : "否")}");
    }
}
