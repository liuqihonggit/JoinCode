namespace JoinCode.ChatCommands;

/// <summary>
/// /profile 命令 — 查看或切换运行时 NodeProfile
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Profile, Description = "查看或切换运行时 Profile", Usage = "/profile [名称|list]", Category = ChatCommandCategory.Config, ArgumentHint = "[profile|list]")]
public sealed class ProfileCommand : ChatCommandBase {
    /// <summary>
    /// 执行 Profile 命令 — 无参/list 时列出全部 Profile 并标记当前，否则切换到目标 Profile
    /// </summary>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var profileManager = ChatCommandBase.GetService<IProfileManager>(context);
        if (profileManager is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}ProfileManager 服务未初始化{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        if (string.IsNullOrEmpty(args) || args is "list" or "ls") {
            ListProfiles(profileManager);
            return ChatCommandResult.Continue();
        }

        await SwitchProfileAsync(profileManager, args, context.CancellationToken).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    private static void ListProfiles(IProfileManager profileManager) {
        var currentName = profileManager.Current.Name;

        TerminalHelper.WriteLine("=== 可用 Profile ===");
        foreach (var name in profileManager.AvailableProfiles) {
            var marker = name.Equals(currentName, StringComparison.OrdinalIgnoreCase) ? " ← 当前" : string.Empty;
            TerminalHelper.WriteLine($"  {name}{marker}");
        }
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Muted}用法: /profile <名称> 切换 Profile{AnsiStyleEnumConstants.Reset}");
    }

    private static async Task SwitchProfileAsync(IProfileManager profileManager, string targetName, CancellationToken ct) {
        if (!profileManager.AvailableProfiles.Contains(targetName)) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未知 Profile: {targetName}。输入 /profile 查看可用列表{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var currentName = profileManager.Current.Name;
        if (targetName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) {
            TerminalHelper.WriteLine($"当前已是 {currentName}，无需切换");
            return;
        }

        try {
            await profileManager.SwitchAsync(targetName, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已切换 Profile: {currentName} → {targetName}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine($"{TerminalColors.Muted}已重建: Task / Stream 中间件管道（订阅了 ProfileSwitching）{AnsiStyleEnumConstants.Reset}");
            // 诚实提示：DI 服务解析、插件加载、工具注册、各条守卫/防御链的顺序都在启动时求值，
            // 换 profile 不会让它们回头。只说「已重建」会让人以为整条链路都换了。
            TerminalHelper.WriteLine($"{TerminalColors.Muted}仍按启动时求值（改动需重启）: 能力（服务/插件/工具表）、命令表面、守卫/修正器/沙箱/防御链顺序{AnsiStyleEnumConstants.Reset}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("Profile 切换", ex);
        }
    }
}
