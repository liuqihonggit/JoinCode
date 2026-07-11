
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Hooks, Description = "管理 Hook 配置", Usage = "/hooks [list|add|remove|test] [args]", Category = ChatCommandCategory.Tools)]
public sealed class HooksCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Hooks;
    public string Description => "管理 Hook 配置";
    public string Usage => "/hooks [list|add|remove|test] [args]";
    public string[] Aliases => [];
    public string ArgumentHint => "[list|add|remove|test]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var hookManager = context.Services!.HookConfigurationManager;
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args) || args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return await ListHooksAsync(hookManager);
        }

        if (args.StartsWith("add", StringComparison.OrdinalIgnoreCase))
        {
            return await AddHookAsync(hookManager, args, context.CancellationToken);
        }

        if (args.StartsWith("remove", StringComparison.OrdinalIgnoreCase))
        {
            return await RemoveHookAsync(hookManager, args, context.CancellationToken);
        }

        if (args.StartsWith("test", StringComparison.OrdinalIgnoreCase))
        {
            return TestHook(args);
        }

        TerminalHelper.WriteLine($"未知操作: {args}");
        TerminalHelper.WriteLine("支持的操作: list, add, remove, test");
        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> ListHooksAsync(IHookConfigurationManager? hookManager)
    {
        if (hookManager is null)
        {
            TerminalHelper.WriteLine("Hook 配置管理器未初始化");
            return ChatCommandResult.Continue();
        }

        try
        {
            var group = await hookManager.LoadAllHooksAsync().ConfigureAwait(false);
            var allHooks = group.Groups
                .SelectMany(g => g.Value.SelectMany(m => m.Value))
                .ToList();

            if (allHooks.Count == 0)
            {
                TerminalHelper.WriteLine("Hook 配置列表:");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("  当前无已配置的 Hook");
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("使用 /hooks add <event> <command> 添加 Hook");
                TerminalHelper.WriteLine("支持的事件: PreToolUse, PostToolUse, Notification, Stop, SessionStart");
                return ChatCommandResult.Continue();
            }

            // 交互模式：Selector 选择 Hook 查看详情
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                var selector = new Selector<HookEntry>(
                    "Hook 配置列表",
                    allHooks.Select(h => new HookEntry(h.Source.ToString(), h.Event.ToString() ?? "", h.Matcher ?? "*", h.Command.GetDisplayText())).ToArray(),
                    h => $"[{h.Source}] {h.Event} matcher={h.Matcher}",
                    h => $"命令: {h.Command}");

                var result = await selector.ShowAsync(CancellationToken.None).ConfigureAwait(false);
                if (!result.Cancelled && result.Selected is not null)
                {
                    TerminalHelper.NewLine();
                    TerminalHelper.WriteLine($"  来源: {result.Selected.Source}");
                    TerminalHelper.WriteLine($"  事件: {result.Selected.Event}");
                    TerminalHelper.WriteLine($"  匹配: {result.Selected.Matcher}");
                    TerminalHelper.WriteLine($"  命令: {result.Selected.Command}");
                }
            }
            else
            {
                // 非交互模式：纯文本列表
                TerminalHelper.WriteLine("Hook 配置列表:");
                TerminalHelper.NewLine();
                foreach (var hook in allHooks)
                {
                    var matcher = string.IsNullOrEmpty(hook.Matcher) ? "*" : hook.Matcher;
                    TerminalHelper.WriteLine($"  [{hook.Source}] {hook.Event} matcher={matcher} → {hook.Command.GetDisplayText()}");
                }
            }

            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("使用 /hooks add <event> <command> 添加 Hook");
            TerminalHelper.WriteLine("支持的事件: PreToolUse, PostToolUse, Notification, Stop, SessionStart");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("加载Hook配置", ex);
        }

        return ChatCommandResult.Continue();
    }

    private sealed record HookEntry(string Source, string Event, string Matcher, string Command);

    private static async Task<ChatCommandResult> AddHookAsync(
        IHookConfigurationManager? hookManager,
        string args,
        CancellationToken ct)
    {
        if (hookManager is null)
        {
            TerminalHelper.WriteLine("Hook 配置管理器未初始化");
            return ChatCommandResult.Continue();
        }

        var parts = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            TerminalHelper.WriteLine("用法: /hooks add <event> <command>");
            TerminalHelper.WriteLine("示例: /hooks add PreToolUse 'echo 检查权限'");
            return ChatCommandResult.Continue();
        }

        var hookEvent = HookEventExtensions.FromValue(parts[1]);
        if (hookEvent is not { } evt)
        {
            TerminalHelper.WriteLine($"未知事件: {parts[1]}");
            TerminalHelper.WriteLine("支持的事件: PreToolUse, PostToolUse, Notification, Stop, SessionStart, SubagentStart, SubagentStop, PreCompact, PostCompact");
            return ChatCommandResult.Continue();
        }

        var hook = new BashCommandHook { Command = parts[2] };

        try
        {
            await hookManager.AddHookAsync(HookSource.UserSettings, evt, null, hook, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"已添加 Hook: 事件={evt}, 命令={parts[2]}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("添加Hook", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> RemoveHookAsync(
        IHookConfigurationManager? hookManager,
        string args,
        CancellationToken ct)
    {
        if (hookManager is null)
        {
            TerminalHelper.WriteLine("Hook 配置管理器未初始化");
            return ChatCommandResult.Continue();
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            TerminalHelper.WriteLine("用法: /hooks remove <event> <command>");
            TerminalHelper.WriteLine("示例: /hooks remove PreToolUse 'echo 检查权限'");
            return ChatCommandResult.Continue();
        }

        var removeParts = parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (removeParts.Length < 2)
        {
            TerminalHelper.WriteLine("用法: /hooks remove <event> <command>");
            return ChatCommandResult.Continue();
        }

        var hookEvent = HookEventExtensions.FromValue(removeParts[0]);
        if (hookEvent is not { } evt)
        {
            TerminalHelper.WriteLine($"未知事件: {removeParts[0]}");
            return ChatCommandResult.Continue();
        }

        var hook = new BashCommandHook { Command = removeParts[1] };

        try
        {
            await hookManager.RemoveHookAsync(HookSource.UserSettings, evt, null, hook, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"已移除 Hook: 事件={evt}, 命令={removeParts[1]}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("移除Hook", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static ChatCommandResult TestHook(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            TerminalHelper.WriteLine("用法: /hooks test <event> [matcher]");
            return ChatCommandResult.Continue();
        }

        var hookEvent = HookEventExtensions.FromValue(parts[1]);
        if (hookEvent is null)
        {
            TerminalHelper.WriteLine($"未知事件: {parts[1]}");
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine($"测试 Hook 事件: {hookEvent}");
        TerminalHelper.WriteLine("Hook 测试功能需要通过实际事件触发来验证");
        return ChatCommandResult.Continue();
    }
}
