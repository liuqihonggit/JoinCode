
namespace JoinCode.ChatCommands;

/// <summary>
/// /statusline 命令 — 对齐 TS statusline.tsx
/// TS 使用 React StatusBar 组件，C# 使用 IConfigurationService 配置
/// 对齐内容：on+off+format 状态栏控制
/// 架构差异：TS 有 React 实时渲染，C# 为配置驱动
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Statusline, Description = "切换状态栏显示", Usage = "/statusline [on|off|format]", Category = ChatCommandCategory.System)]
public sealed class StatuslineCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Statusline;
    public string Description => "切换状态栏显示";
    public string Usage => "/statusline [on|off|format <template>]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off|format]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        if (args is "on" or "enable")
        {
            if (configService is not null)
                await configService.SetAsync("statusline.enabled", "true", context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("状态栏: 已启用");
        }
        else if (args is "off" or "disable")
        {
            if (configService is not null)
                await configService.SetAsync("statusline.enabled", "false", context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("状态栏: 已禁用");
        }
        else if (args.StartsWith("format"))
        {
            var formatValue = args["format".Length..].Trim();
            if (string.IsNullOrEmpty(formatValue))
            {
                var currentFormat = configService is not null
                    ? await configService.GetAsync("statusline.format", context.CancellationToken).ConfigureAwait(false)
                    : null;
                TerminalHelper.WriteLine($"当前状态栏格式: {currentFormat ?? "(默认)"}");
                TerminalHelper.WriteLine("用法: /statusline format <template>");
                TerminalHelper.WriteLine("  可用变量: {model}, {tokens}, {cost}, {mode}, {time}");
            }
            else
            {
                if (configService is not null)
                    await configService.SetAsync("statusline.format", formatValue, context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"状态栏格式: {formatValue}");
            }
        }
        else
        {
            var enabled = await GetSettingAsync(configService, "statusline.enabled", "true", context.CancellationToken).ConfigureAwait(false);
            var format = configService is not null
                ? await configService.GetAsync("statusline.format", context.CancellationToken).ConfigureAwait(false)
                : null;
            TerminalHelper.WriteLine($"状态栏: {(enabled == "true" ? "已启用" : "已禁用")}");
            if (format is not null)
                TerminalHelper.WriteLine($"  格式: {format}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("使用 /statusline on|off 切换状态栏");
            TerminalHelper.WriteLine("使用 /statusline format <template> 设置格式");
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<string> GetSettingAsync(IConfigurationService? configService, string key, string defaultValue, CancellationToken ct)
    {
        if (configService is null) return defaultValue;
        var value = await configService.GetAsync(key, ct).ConfigureAwait(false);
        return value ?? defaultValue;
    }
}
