
namespace JoinCode.ChatCommands;

/// <summary>
/// /ide 命令 — 对齐 TS ide.ts
/// TS 使用 VSCode/JetBrains 扩展集成，C# 使用 IIdeIntegrationService
/// 对齐内容：detect+connect+disconnect+status+open 核心操作
/// 架构差异：TS 有 React 交互式 IDE 选择器，C# 为命令行交互
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Ide, Description = "IDE 集成管理", Usage = "/ide [detect|connect|disconnect|status|open]", Category = ChatCommandCategory.Platform, ArgumentHint = "detect|connect|disconnect|status|open", IsHidden = true)]
public sealed class IdeCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var ideService = ChatCommandBase.GetService<IIdeIntegrationService>(context);

        if (ideService is null)
            return ChatCommandResult.Continue();

        var arg = context.Arguments?.Trim().ToLowerInvariant() ?? "";
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCommand = parts.Length > 0 ? parts[0] : "";

        switch (subCommand)
        {
            case PlatformActionConstants.Detect:
                HandleDetect(ideService);
                break;
            case PlatformActionConstants.Connect:
            case "c":
                await HandleConnectionAsync(ideService, ToggleAction.On);
                break;
            case PlatformActionConstants.Disconnect:
            case "d":
                await HandleConnectionAsync(ideService, ToggleAction.Off);
                break;
            case PlatformActionConstants.Open:
            case "o":
                await HandleOpenAsync(ideService, parts.Length > 1 ? parts[1] : "");
                break;
            case PlatformActionConstants.Status:
            case "s":
            case "":
                HandleStatus(ideService);
                break;
            default:
                TerminalHelper.WriteLine(L.T(StringKey.IdeUnknownArg, context.Arguments));
                TerminalHelper.WriteLine(L.T(StringKey.IdeUsage));
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static void HandleDetect(IIdeIntegrationService ideService)
    {
        TerminalHelper.WriteLine(L.T(StringKey.IdeDetecting));
        TerminalHelper.NewLine();

        var details = ideService.DetectInstalledIdesDetailed();

        if (details.Count == 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeNoneDetected));
            return;
        }

        foreach (var detail in details)
        {
            var status = detail.ExtensionInstalled ? "已安装" : "未安装";
            var running = detail.IsRunning ? " [运行中]" : "";
            var onPath = detail.FoundOnPath ? " (PATH)" : "";

            TerminalHelper.WriteLine(L.T(StringKey.IdeDetail, detail.Name, status + running + onPath));

            if (!string.IsNullOrEmpty(detail.Path))
                TerminalHelper.WriteLine(L.T(StringKey.IdeDetailPath, detail.Path));
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.IdeDetectCount, details.Count(d => d.ExtensionInstalled)));
        TerminalHelper.WriteLine(L.T(StringKey.IdeConnectHint));
    }

    private static async Task HandleConnectionAsync(IIdeIntegrationService ideService, ToggleAction action)
    {
        if (action == ToggleAction.On)
        {
            var ides = ideService.DetectInstalledIdes();

            if (ides.Count == 0)
            {
                TerminalHelper.WriteLine(L.T(StringKey.IdeNoInstalled));
                return;
            }

            // 交互模式：使用 Selector 组件
            // 对齐 TS: RunningIDESelector — 上下键选择IDE+Enter连接+Esc取消
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                var selector = new Selector<IdeInfo>(
                    "选择要连接的 IDE",
                    [.. ides],
                    ide => ide.Name,
                    ide => ide.ExtensionInstalled ? "扩展已安装" : "扩展未安装",
                    enableSearch: false);

                var result = await selector.ShowAsync(CancellationToken.None).ConfigureAwait(false);

                if (result.Cancelled || result.Selected is null)
                {
                    TerminalHelper.WriteLine(L.T(StringKey.IdeCancelled));
                    return;
                }

                var connected = await ideService.ConnectAsync(result.Selected.Type).ConfigureAwait(false);

                if (connected)
                    TerminalHelper.WriteLine(L.T(StringKey.IdeConnected, result.Selected.Name));
                else
                    TerminalHelper.WriteLine(L.T(StringKey.IdeConnectFailed, result.Selected.Name));
                return;
            }

            // 非交互模式回退
            TerminalHelper.WriteLine(L.T(StringKey.IdeDetectedList));
            for (var i = 0; i < ides.Count; i++)
            {
                TerminalHelper.WriteLine(L.T(StringKey.IdeDetectedItem, i + 1, ides[i].Name));
            }
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine(L.T(StringKey.IdeNonInteractive));
        }
        else
        {
            await ideService.DisconnectAsync().ConfigureAwait(false);
            TerminalHelper.WriteLine(L.T(StringKey.IdeDisconnected));
        }
    }

    private static async Task HandleOpenAsync(IIdeIntegrationService ideService, string args)
    {
        if (ideService.CurrentConnection is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeNotConnected));
            return;
        }

        var filePath = args;
        int? line = null;

        var colonIndex = args.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex < args.Length - 1)
        {
            var lineStr = args[(colonIndex + 1)..];
            if (int.TryParse(lineStr, out var parsedLine))
            {
                filePath = args[..colonIndex];
                line = parsedLine;
            }
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeOpenUsage));
            return;
        }

        var success = await ideService.OpenFileAsync(filePath, line).ConfigureAwait(false);

        if (success)
            TerminalHelper.WriteLine(L.T(StringKey.IdeOpened, ideService.CurrentConnection.Name, filePath));
        else
            TerminalHelper.WriteLine(L.T(StringKey.IdeOpenFailed, filePath));
    }

    private static void HandleStatus(IIdeIntegrationService ideService)
    {
        var current = ideService.CurrentConnection;

        if (current is not null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeCurrentConnection, current.Name));
            TerminalHelper.WriteLine(L.T(StringKey.IdeExtensionInstalled, current.ExtensionInstalled ? "是" : "否"));
            TerminalHelper.WriteLine(L.T(StringKey.IdeStatusConnected));

            var currentFile = ideService.CurrentFilePath;
            if (!string.IsNullOrEmpty(currentFile))
                TerminalHelper.WriteLine(L.T(StringKey.IdeCurrentFile, currentFile));
        }
        else
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeNoCurrentConnection));
        }

        TerminalHelper.NewLine();
        var ides = ideService.DetectInstalledIdes();

        if (ides.Count > 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeInstalledList));
            foreach (var ide in ides)
            {
                TerminalHelper.WriteLine(L.T(StringKey.IdeInstalledItem, ide.Name, ide.ExtensionInstalled ? "已安装" : "未安装"));
            }
        }
        else
        {
            TerminalHelper.WriteLine(L.T(StringKey.IdeNoInstalledIdes));
        }
    }
}
