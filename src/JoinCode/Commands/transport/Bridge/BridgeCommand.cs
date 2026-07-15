
namespace JoinCode.ChatCommands;

/// <summary>
/// /bridge 命令 — 对齐 TS bridge-kick.ts
/// TS 使用命名管道 Bridge 通信，C# 使用 BridgeServer+BridgeClient+BridgeUIService
/// 对齐内容：qr+sessions+status+connect+disconnect 核心操作
/// 架构差异：TS 有 React QR 码渲染，C# 使用终端 ASCII QR
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Bridge, Description = "Bridge 远程控制管理", Usage = "/bridge [qr|sessions|status|connect|disconnect]", Category = ChatCommandCategory.Bridge)]
public sealed class BridgeCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Bridge;
    public string Description => "Bridge 远程控制管理";
    public string Usage => "/bridge [qr|sessions|status|connect|disconnect]";
    public string[] Aliases => ["rc"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        switch (action)
        {
            case BridgeActionConstants.Qr:
                await ShowQrCodeAsync(context);
                break;
            case BridgeActionConstants.Sessions:
                await ShowSessionsAsync(context);
                break;
            case BridgeActionConstants.Status:
                ShowStatus(context);
                break;
            case BridgeActionConstants.Connect:
                await ToggleConnectionAsync(context, ToggleAction.On);
                break;
            case BridgeActionConstants.Disconnect:
                await ToggleConnectionAsync(context, ToggleAction.Off);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}未知操作: {action}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine("可用操作: qr, sessions, status, connect, disconnect");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ShowQrCodeAsync(ChatCommandContext context)
    {
        var serviceProvider = context.Services.ServiceProvider;
        if (serviceProvider is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}服务提供者不可用，无法生成 QR 码{AnsiStyleConstants.Reset}");
            return;
        }

        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;
        if (bridgeUIService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge UI 服务未注册，请确认 Bridge 功能已启用{AnsiStyleConstants.Reset}");
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var endpoint = "ws://localhost:3456";

        var qrData = await bridgeUIService.GenerateQRDataAsync(sessionId, endpoint).ConfigureAwait(false);
        var terminalOutput = bridgeUIService.FormatAsTerminalQR(qrData);

        TerminalHelper.WriteLine(terminalOutput);
        TerminalHelper.WriteLine($"{TerminalColors.Success}使用移动端扫描上方 QR 码以连接 Bridge 会话{AnsiStyleConstants.Reset}");
    }

    private static async Task ShowSessionsAsync(ChatCommandContext context)
    {
        var serviceProvider = context.Services.ServiceProvider;
        if (serviceProvider is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}服务提供者不可用，无法获取会话列表{AnsiStyleConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine("=== Bridge 活跃会话 ===\n");

        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;
        if (bridgeUIService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge UI 服务未注册，请确认 Bridge 功能已启用{AnsiStyleConstants.Reset}");
            return;
        }

        var sessions = await bridgeUIService.GetActiveSessionList(context.CancellationToken).ConfigureAwait(false);

        if (sessions.Count == 0)
        {
            TerminalHelper.WriteLine("  当前无活跃会话");
        }
        else
        {
            foreach (var session in sessions)
            {
                var connectedTime = DateTimeOffset.FromUnixTimeMilliseconds(session.ConnectedAt)
                    .ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss");
                var statusColor = session.Status == "active" ? TerminalColors.Success : TerminalColors.Warning;
                TerminalHelper.WriteLine($"  {session.SessionId}");
                TerminalHelper.WriteLine($"{statusColor}    状态: {session.Status}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine($"    客户端: {session.ClientName ?? "未知"}");
                TerminalHelper.WriteLine($"    连接时间: {connectedTime}");
                TerminalHelper.NewLine();
            }
        }
    }

    private static void ShowStatus(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("=== Bridge 状态 ===\n");

        var serviceProvider = context.Services.ServiceProvider;
        if (serviceProvider is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}服务提供者不可用{AnsiStyleConstants.Reset}");
            return;
        }

        var bridgeServer = serviceProvider.GetService(typeof(Core.Bridge.BridgeServer)) as Core.Bridge.BridgeServer;
        var bridgeClient = serviceProvider.GetService(typeof(Core.Bridge.BridgeClient)) as Core.Bridge.BridgeClient;
        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;

        TerminalHelper.WriteLine($"  服务端: {(bridgeServer is not null ? "已注册" : "未注册")}");
        TerminalHelper.WriteLine($"  客户端: {(bridgeClient is not null ? "已注册" : "未注册")}");
        TerminalHelper.WriteLine($"  UI 服务: {(bridgeUIService is not null ? "已注册" : "未注册")}");

        if (bridgeClient is not null)
        {
            var state = bridgeClient.IsRunning ? BridgeConnectionState.Connected : BridgeConnectionState.Idle;
            TerminalHelper.WriteLine($"  {BridgeStatusIndicator.Render(state)}");
        }
    }

    private static async Task ToggleConnectionAsync(ChatCommandContext context, ToggleAction action)
    {
        var bridgeClient = context.Services.BridgeClient;
        if (bridgeClient is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge 客户端未配置{AnsiStyleConstants.Reset}");
            return;
        }

        if (action == ToggleAction.On)
        {
            if (bridgeClient.IsRunning)
            {
                TerminalHelper.WriteLine("Bridge 已连接");
                return;
            }

            TerminalHelper.WriteLine("正在启动 Bridge 客户端...");
            try
            {
                await bridgeClient.StartAsync().ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}Bridge 客户端已启动{AnsiStyleConstants.Reset}");
            }
            catch (Exception ex)
            {
                ChatCommandBase.HandleError("Bridge启动", ex);
            }
        }
        else
        {
            if (!bridgeClient.IsRunning)
            {
                TerminalHelper.WriteLine("Bridge 未连接");
                return;
            }

            try
            {
                await bridgeClient.StopAsync().ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已断开 Bridge 连接{AnsiStyleConstants.Reset}");
            }
            catch (Exception ex)
            {
                ChatCommandBase.HandleError("Bridge断开", ex);
            }
        }
    }
}
