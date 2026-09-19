
namespace JoinCode.ChatCommands;

/// <summary>
/// /desktop 命令 — 将当前会话转移到桌面应用
/// 通过 IDesktopHandoffService 把会话状态移交到 jcc-desktop 桌面进程
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Desktop, Description = "将会话转移到桌面应用", Usage = "/desktop", Category = ChatCommandCategory.Platform, Aliases = ["app"], IsHidden = true)]
public sealed class DesktopCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /desktop 命令 — 检测桌面应用可用性并转移当前会话
    /// </summary>
    /// <param name="context">命令执行上下文，包含会话 ID 与取消令牌</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var handoffService = ChatCommandBase.GetService<IDesktopHandoffService>(context);

        if (handoffService is null)
            return ChatCommandResult.Continue();

        if (!handoffService.IsDesktopAvailable) {
            TerminalHelper.WriteLine("未检测到桌面应用。");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("  请安装 jcc-desktop 桌面应用");
            TerminalHelper.WriteLine("  或使用 /bridge 命令通过命名管道连接");
            return ChatCommandResult.Continue();
        }

        var sessionId = context.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        var success = await handoffService.HandoffToDesktopAsync(sessionId).ConfigureAwait(false);

        if (success) {
            TerminalHelper.WriteLine($"会话 {sessionId} 已转移到桌面应用");
            TerminalHelper.WriteLine(handoffService.DesktopConnectionInfo);
        } else {
            TerminalHelper.WriteLine("会话转移失败，请检查桌面应用是否正在运行");
        }

        return ChatCommandResult.Continue();
    }
}