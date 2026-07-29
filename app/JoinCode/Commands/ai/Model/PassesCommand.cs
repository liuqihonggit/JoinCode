namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Passes, Description = "已废弃: 请使用 /permissions 管理权限规则", Usage = "/passes", Category = ChatCommandCategory.Model, IsHidden = true)]
public sealed class PassesCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 对齐 TS: /passes 直接重定向到 /permissions 执行
        var registry = context.Services.CommandRegistry;
        if (registry is not null)
        {
            var permissionsCommand = registry.GetCommand("permissions");
            if (permissionsCommand is not null)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}/passes 已废弃，重定向到 /permissions{AnsiStyleConstants.Reset}");
                return await permissionsCommand.ExecuteAsync(context).ConfigureAwait(false);
            }
        }

        // 回退: 无 CommandRegistry 时打印提示
        TerminalHelper.WriteLine("/passes 已废弃。请使用 /permissions 管理权限规则。");
        TerminalHelper.WriteLine("  /permissions list    — 列出权限规则");
        TerminalHelper.WriteLine("  /permissions add     — 添加权限规则");
        TerminalHelper.WriteLine("  /permissions remove  — 移除权限规则");
        TerminalHelper.WriteLine("  /permissions workspace — 管理工作区目录");
        return ChatCommandResult.Continue();
    }
}
