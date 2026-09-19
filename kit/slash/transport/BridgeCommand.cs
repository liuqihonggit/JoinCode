
namespace JoinCode.ChatCommands;

/// <summary>
/// /bridge 命令 — 对齐 TS bridge-kick.ts
/// TS 使用命名管道 Bridge 通信，C# 使用 BridgeServer+BridgeClient+BridgeUIService
/// 对齐内容：qr+sessions+status+connect+disconnect 核心操作
/// 架构差异：TS 有 React QR 码渲染，C# 使用终端 ASCII QR
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Bridge, Description = "Bridge 远程控制管理", Usage = "/bridge [qr|sessions|status|connect|disconnect]", Category = ChatCommandCategory.Bridge, Aliases = ["rc"])]
[ChatCommandArg("action", Type = "string", Description = "Bridge 操作: qr=显示二维码, sessions=列出会话, status=状态, connect=连接, disconnect=断开", Enum = new[] { "qr", "sessions", "status", "connect", "disconnect" })]
public sealed class BridgeCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /bridge 命令，根据子操作分发到 QR 码显示、会话列表、状态查询或连接切换
    /// </summary>
    /// <param name="context">命令执行上下文，提供子操作参数与服务提供者</param>
    /// <returns>表示命令执行结果的 <see cref="ChatCommandResult"/>，始终为 Continue</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        switch (action) {
            case BridgeActionEnumConstants.Qr:
            await ShowQrCodeAsync(context);
            break;
            case BridgeActionEnumConstants.Sessions:
            await ShowSessionsAsync(context);
            break;
            case BridgeActionEnumConstants.Status:
            ShowStatus(context);
            break;
            case BridgeActionEnumConstants.Connect:
            await ToggleConnectionAsync(context, ToggleAction.On);
            break;
            case BridgeActionEnumConstants.Disconnect:
            await ToggleConnectionAsync(context, ToggleAction.Off);
            break;
            default:
            TerminalHelper.WriteLine($"{TerminalColors.Error}未知操作: {action}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("可用操作: qr, sessions, status, connect, disconnect");
            break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ShowQrCodeAsync(ChatCommandContext context) {
        var serviceProvider = context.Services;
        if (serviceProvider is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}服务提供者不可用，无法生成 QR 码{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;
        if (bridgeUIService is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge UI 服务未注册，请确认 Bridge 功能已启用{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var endpoint = "ws://localhost:3456";

        var qrData = await bridgeUIService.GenerateQRDataAsync(sessionId, endpoint).ConfigureAwait(false);
        var terminalOutput = bridgeUIService.FormatAsTerminalQR(qrData);

        TerminalHelper.WriteLine(terminalOutput);
        TerminalHelper.WriteLine($"{TerminalColors.Success}使用移动端扫描上方 QR 码以连接 Bridge 会话{AnsiStyleEnumConstants.Reset}");
    }

    private static async Task ShowSessionsAsync(ChatCommandContext context) {
        var serviceProvider = context.Services;
        if (serviceProvider is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}服务提供者不可用，无法获取会话列表{AnsiStyleEnumConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine("=== Bridge 活跃会话 ===\n");

        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;
        if (bridgeUIService is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge UI 服务未注册，请确认 Bridge 功能已启用{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var sessions = await bridgeUIService.GetActiveSessionList(context.CancellationToken).ConfigureAwait(false);

        if (sessions.Count == 0) {
            TerminalHelper.WriteLine("  当前无活跃会话");
        } else {
            foreach (var session in sessions) {
                var connectedTime = DateTimeOffset.FromUnixTimeMilliseconds(session.ConnectedAt)
                    .ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss");
                var statusColor = session.Status == "active" ? TerminalColors.Success : TerminalColors.Warning;
                TerminalHelper.WriteLine($"  {session.SessionId}");
                TerminalHelper.WriteLine($"{statusColor}    状态: {session.Status}{AnsiStyleEnumConstants.Reset}");
                TerminalHelper.WriteLine($"    客户端: {session.ClientName ?? "未知"}");
                TerminalHelper.WriteLine($"    连接时间: {connectedTime}");
                TerminalHelper.NewLine();
            }
        }
    }

    private static void ShowStatus(ChatCommandContext context) {
        TerminalHelper.WriteLine("=== Bridge 状态 ===\n");

        var serviceProvider = context.Services;
        if (serviceProvider is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}服务提供者不可用{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var bridgeServer = serviceProvider.GetService(typeof(Core.Bridge.BridgeServer)) as Core.Bridge.BridgeServer;
        var bridgeClient = serviceProvider.GetService(typeof(Core.Bridge.BridgeClient)) as Core.Bridge.BridgeClient;
        var bridgeUIService = serviceProvider.GetService(typeof(Core.Bridge.BridgeUIService)) as Core.Bridge.BridgeUIService;

        TerminalHelper.WriteLine($"  服务端: {(bridgeServer is not null ? "已注册" : "未注册")}");
        TerminalHelper.WriteLine($"  客户端: {(bridgeClient is not null ? "已注册" : "未注册")}");
        TerminalHelper.WriteLine($"  UI 服务: {(bridgeUIService is not null ? "已注册" : "未注册")}");

        if (bridgeClient is not null) {
            var state = bridgeClient.IsRunning ? BridgeConnectionState.Connected : BridgeConnectionState.Idle;
            TerminalHelper.WriteLine($"  {BridgeStatusIndicator.Render(state)}");
        }
    }

    private static async Task ToggleConnectionAsync(ChatCommandContext context, ToggleAction action) {
        var bridgeClient = context.GetCommandServices().BridgeClient;
        if (bridgeClient is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge 客户端未配置{AnsiStyleEnumConstants.Reset}");
            return;
        }

        if (action == ToggleAction.On) {
            if (bridgeClient.IsRunning) {
                TerminalHelper.WriteLine("Bridge 已连接");
                return;
            }

            TerminalHelper.WriteLine("正在启动 Bridge 客户端...");
            try {
                await bridgeClient.StartAsync().ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}Bridge 客户端已启动{AnsiStyleEnumConstants.Reset}");
            } catch (Exception ex) {
                ChatCommandBase.HandleError("Bridge启动", ex);
            }
        } else {
            if (!bridgeClient.IsRunning) {
                TerminalHelper.WriteLine("Bridge 未连接");
                return;
            }

            try {
                await bridgeClient.StopAsync().ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已断开 Bridge 连接{AnsiStyleEnumConstants.Reset}");
            } catch (Exception ex) {
                ChatCommandBase.HandleError("Bridge断开", ex);
            }
        }
    }
}