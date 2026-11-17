namespace JoinCode.ChatCommands;

/// <summary>
/// /config 命令 — 管理配置设置
/// 支持获取、设置、列出、移除配置项,提供已知配置项的元数据描述
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Config, Description = "管理配置设置", Usage = "/config [get|set|list|remove] [key] [value]", Category = ChatCommandCategory.Config, ArgumentHint = "[get|set|list|remove]")]
[ChatCommandArg("action", Type = "string", Description = "配置操作", Enum = [ConfigActionEnumConstants.Get, ConfigActionEnumConstants.Set, ConfigActionEnumConstants.List, ConfigActionEnumConstants.Remove])]
[ChatCommandArg("key", Type = "string", Description = "配置键名")]
[ChatCommandArg("value", Type = "string", Description = "配置值（set 操作时需要）")]
public sealed class ConfigCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /config 命令 — 根据子命令分发到对应配置操作
    /// 支持 get/set/list/remove 四种操作,默认 list
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>命令执行结果,始终返回 Continue 表示继续会话</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action) {
            case "get":
            await GetConfigAsync(context, args).ConfigureAwait(false);
            break;
            case "set":
            await SetConfigAsync(context, args).ConfigureAwait(false);
            break;
            case CrudActionEnumConstants.Remove:
            case CrudActionEnumConstants.Delete:
            case CrudActionEnumConstants.Rm:
            await RemoveConfigAsync(context, args).ConfigureAwait(false);
            break;
            case CrudActionEnumConstants.List:
            case CrudActionEnumConstants.Ls:
            default:
            await ListConfigAsync(context).ConfigureAwait(false);
            break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task GetConfigAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config get <key>{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var key = args[1];
        var configService = ResolveConfigService(context);
        // 存储源由配置服务按 SettingSourceMap 决定 —— 与 config_get 工具读同一个文件（TASK089）
        var value = await configService.GetAsync(key, context.CancellationToken).ConfigureAwait(false);
        if (value is not null) {
            TerminalHelper.WriteLine($"{key} = {value}");
        } else {
            TerminalHelper.WriteLine($"配置项 '{key}' 不存在");
        }
    }

    private static async Task SetConfigAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 3) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config set <key> <value>{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var key = args[1];
        var value = string.Join(" ", args[2..]);

        // 说明文字取自唯一清单 —— 与 config_set/config_list 用的是同一份描述
        if (ConfigKeyRegistry.Get(key) is { } setting) {
            TerminalHelper.WriteLine($"  {key}: {setting.Description}");
        } else {
            // 不在清单里的键：不拦（插件/历史键仍可能被别的代码读取），但必须说清楚，
            // 否则打字错误会静默写进 settings.json —— 用户以为改了，运行时读的是另一个键
            // 建议来自唯一清单 —— 与 config_get 的 UnknownSetting 诊断同一份算法
            var hint = string.Join(", ", ConfigKeyRegistry.Suggest(key));
            TerminalHelper.WriteLine(
                $"{TerminalColors.Warning}注意: '{key}' 不在已知配置项里，仍会写入。" +
                (hint.Length > 0 ? $" 你是不是想用: {hint}？" : " 已知项见 /config list。") +
                $"{AnsiStyleEnumConstants.Reset}");
        }

        var configService = ResolveConfigService(context);
        var saved = await configService.SetAsync(key, value, context.CancellationToken).ConfigureAwait(false);
        if (!saved) {
            // 写盘失败（含「路径无法解析，什么都没写」）不能报成功 —— 否则用户以为改了，下次启动还是旧值
            TerminalHelper.WriteLine($"{TerminalColors.Error}设置失败: {key}（未能写入配置文件，原因见上方日志）{AnsiStyleEnumConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine($"{TerminalColors.Success}已设置: {key} = {value}{AnsiStyleEnumConstants.Reset}");
    }

    private static async Task RemoveConfigAsync(ChatCommandContext context, string[] args) {
        if (args.Length < 2) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config remove <key>{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var key = args[1];
        var configService = ResolveConfigService(context);
        var removed = await configService.RemoveAsync(key, context.CancellationToken).ConfigureAwait(false);
        if (!removed) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}配置项 '{key}' 不存在{AnsiStyleEnumConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine($"{TerminalColors.Success}已移除: {key}{AnsiStyleEnumConstants.Reset}");
    }

    private static async Task ListConfigAsync(ChatCommandContext context) {
        var configService = ResolveConfigService(context);
        var config = await configService.GetAllAsync(context.CancellationToken).ConfigureAwait(false);

        var panel = new TabPanel(
            ["当前配置", "已知配置项"],
            tabIndex => tabIndex switch {
                0 => RenderCurrentConfig(config),
                1 => RenderKnownSettings(config),
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private static string RenderCurrentConfig(Dictionary<string, string> config) {
        var sb = new StringBuilder();
        if (config.Count > 0) {
            foreach (var kvp in config.OrderBy(x => x.Key)) {
                var described = ConfigKeyRegistry.Get(kvp.Key);
                var desc = described is null ? "" : $"  ({described.Description})";
                sb.AppendLine($"  {kvp.Key} = {kvp.Value}{desc}");
            }
        } else {
            sb.AppendLine("  当前没有配置项");
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config set <key> <value> 设置配置{AnsiStyleEnumConstants.Reset}");
        return sb.ToString();
    }

    private static string RenderKnownSettings(Dictionary<string, string> config) {
        var sb = new StringBuilder();
        foreach (var (key, setting) in ConfigKeyRegistry.All.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            var marker = config.ContainsKey(key) ? $" {TerminalColors.Success}✓{AnsiStyleEnumConstants.Reset}" : "";
            var optionsHint = ConfigKeyRegistry.OptionsOf(key) is { Length: > 0 } options
                ? $"[{string.Join('/', options)}] "
                : "";
            sb.AppendLine($"  {key}{marker} - {optionsHint}{setting.Description}");
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config set <key> <value> 设置配置{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config remove <key> 移除配置{AnsiStyleEnumConstants.Reset}");
        return sb.ToString();
    }

    private static IConfigurationService ResolveConfigService(ChatCommandContext context) {
        var service = context.Services.GetService(typeof(IConfigurationService)) as IConfigurationService;
        return service ?? throw new InvalidOperationException("[APP004] IConfigurationService 未注册，无法操作配置");
    }
}