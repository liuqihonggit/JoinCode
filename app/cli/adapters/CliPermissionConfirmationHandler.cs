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

    /// <summary>初始化 <see cref="CliPermissionConfirmationHandler"/> 实例</summary>
    /// <param name="permissionManager">可选的工具权限管理器，用于临时批准工具或危险等级</param>
    /// <param name="confirmationGate">可选的确认门，用于在 REPL 单通道中路由用户输入</param>
    public CliPermissionConfirmationHandler(IToolPermissionManager? permissionManager = null, IConfirmationGate? confirmationGate = null)
    {
        _permissionManager = permissionManager;
        _confirmationGate = confirmationGate;
    }

    /// <summary>在控制台显示权限确认提示并解析用户响应，按结果临时批准工具或等级</summary>
    /// <param name="toolName">待确认的工具名称</param>
    /// <param name="confirmationPrompt">展示给用户的确认提示文本</param>
    /// <returns>用户选择的确认动作（允许/始终允许/拒绝）；非交互环境自动拒绝</returns>
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
