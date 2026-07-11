
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.PrivacySettings, Description = "管理隐私设置", Usage = "/privacy-settings [show|telemetry on|off|analytics on|off|crash-reports on|off]", Category = ChatCommandCategory.Auth)]
public sealed class PrivacySettingsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.PrivacySettings;
    public string Description => "管理隐私设置";
    public string Usage => "/privacy-settings [show|telemetry on|off|analytics on|off|crash-reports on|off]";
    public string[] Aliases => [];
    public string ArgumentHint => "[show|telemetry|analytics|crash-reports]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context);
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        if (string.IsNullOrEmpty(args) || args is "show")
        {
            var telemetry = await GetSettingAsync(configService, "privacy.telemetry", "off", context.CancellationToken).ConfigureAwait(false);
            var analytics = await GetSettingAsync(configService, "privacy.analytics", "off", context.CancellationToken).ConfigureAwait(false);
            var crashReports = await GetSettingAsync(configService, "privacy.crashReports", "off", context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine("隐私设置:");
            TerminalHelper.WriteLine($"  遥测:     {(telemetry == "on" ? "已启用" : "未启用")}");
            TerminalHelper.WriteLine($"  分析:     {(analytics == "on" ? "已启用" : "未启用")}");
            TerminalHelper.WriteLine($"  崩溃报告: {(crashReports == "on" ? "已启用" : "未启用")}");
        }
        else if (args.StartsWith("telemetry"))
        {
            await SetPrivacySetting(configService, "privacy.telemetry", args["telemetry".Length..].Trim(), context.CancellationToken).ConfigureAwait(false);
        }
        else if (args.StartsWith("analytics"))
        {
            await SetPrivacySetting(configService, "privacy.analytics", args["analytics".Length..].Trim(), context.CancellationToken).ConfigureAwait(false);
        }
        else if (args.StartsWith("crash-reports"))
        {
            await SetPrivacySetting(configService, "privacy.crashReports", args["crash-reports".Length..].Trim(), context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            TerminalHelper.WriteLine($"未知设置: {args}");
            TerminalHelper.WriteLine("支持: telemetry, analytics, crash-reports");
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<string> GetSettingAsync(IConfigurationService? configService, string key, string defaultValue, CancellationToken ct)
    {
        if (configService is null) return defaultValue;
        var value = await configService.GetAsync(key, ct).ConfigureAwait(false);
        return value ?? defaultValue;
    }

    private static async Task SetPrivacySetting(IConfigurationService? configService, string key, string subArgs, CancellationToken ct)
    {
        if (subArgs is "on")
        {
            if (configService is not null)
                await configService.SetAsync(key, "on", ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{key}: 已启用");
        }
        else if (subArgs is "off")
        {
            if (configService is not null)
                await configService.SetAsync(key, "off", ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{key}: 已禁用");
        }
        else
        {
            TerminalHelper.WriteLine($"用法: /privacy-settings {key.Split('.')[1]} on|off");
        }
    }
}
