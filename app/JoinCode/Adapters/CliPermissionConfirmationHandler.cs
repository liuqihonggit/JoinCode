namespace JoinCode.Adapters;

/// <summary>
/// CLI 权限确认处理器 — ^ 提示符交互式确认
/// 输入通过 ReplLoopStep 的 readTask 单通道路由，避免 stdin 竞争
/// </summary>
[Register(typeof(IPermissionConfirmationHandler), ServiceLifetime.Singleton)]
public sealed class CliPermissionConfirmationHandler : IPermissionConfirmationHandler
{
    private readonly IToolPermissionManager? _permissionManager;
    private readonly IConfirmationGate? _confirmationGate;

    public CliPermissionConfirmationHandler(IToolPermissionManager? permissionManager = null, IConfirmationGate? confirmationGate = null)
    {
        _permissionManager = permissionManager;
        _confirmationGate = confirmationGate;
    }

    public PermissionConfirmAction Confirm(string toolName, string confirmationPrompt)
    {
        Cli.TerminalHelper.WriteLine();
        using (Cli.TerminalHelper.SetColor(ConsoleColor.Cyan))
            Cli.TerminalHelper.WriteRaw("^ ");
        using (Cli.TerminalHelper.SetColor(ConsoleColor.Yellow))
            Cli.TerminalHelper.WriteLine($"权限确认: {confirmationPrompt}");

        using (Cli.TerminalHelper.SetColor(ConsoleColor.Cyan))
            Cli.TerminalHelper.WriteRaw("^ ");
        using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
            Cli.TerminalHelper.WriteRaw("(y)允许 / (a)始终允许 / (n)拒绝 [n]: ");

        if (Cli.TerminalHelper.IsInputRedirected || Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            Cli.TerminalHelper.WriteLine("非交互环境，自动拒绝");
            return PermissionConfirmAction.Deny;
        }

        try
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationGate?.SetPending(tcs);

            var input = tcs.Task.GetAwaiter().GetResult();

            _confirmationGate?.Clear();

            Cli.TerminalHelper.NewLine();

            var action = input.Trim().ToLowerInvariant() switch
            {
                "y" or "yes" => PermissionConfirmAction.Allow,
                "a" or "always" => PermissionConfirmAction.AlwaysAllow,
                _ => PermissionConfirmAction.Deny
            };

            if (_permissionManager is not null)
            {
                if (action == PermissionConfirmAction.Allow)
                    _permissionManager.ApproveToolTemporarily(toolName, TimeSpan.FromMinutes(1));
                else if (action == PermissionConfirmAction.AlwaysAllow)
                    _permissionManager.ApproveToolTemporarily(toolName, TimeSpan.FromMinutes(30));

                // 同级别自动通过 — 解析 prompt 中的 levelTag，批准对应等级（会话级非持久化）
                var level = DangerLevelPromptParser.ParseLevelFromPrompt(confirmationPrompt);
                if (level is not null)
                    _permissionManager.ApproveLevelTemporarily(level.Value);
            }

            return action;
        }
        catch
        {
            _confirmationGate?.Clear();
            return PermissionConfirmAction.Deny;
        }
    }
}
