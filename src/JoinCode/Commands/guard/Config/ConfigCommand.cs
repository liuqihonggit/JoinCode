namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Config, Description = "管理配置设置", Usage = "/config [get|set|list|remove] [key] [value]", Category = ChatCommandCategory.Config)]
public sealed class ConfigCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Config;
    public string Description => "管理配置设置";
    public string Usage => "/config [get|set|list|remove] [key] [value]";
    public string[] Aliases => [];
    public string ArgumentHint => "[get|set|list|remove]";
    public bool IsHidden => false;

    /// <summary>
    /// 已知配置项元数据 — 单一数据源来自 ConfigKey 枚举
    /// 字典键为 ConfigKey 枚举,值为 UI 描述(仅展示用)
    /// </summary>
    private static readonly FrozenDictionary<ConfigKey, string> KnownSettings = new Dictionary<ConfigKey, string>
    {
        [ConfigKey.Theme]                    = "UI 主题 (dark/light/auto)",
        [ConfigKey.EditorMode]               = "按键绑定模式 (vim/emacs/default)",
        [ConfigKey.Verbose]                  = "详细调试输出 (true/false)",
        [ConfigKey.AutoCompactEnabled]       = "自动压缩上下文 (true/false)",
        [ConfigKey.AutoMemoryEnabled]        = "自动记忆 (true/false)",
        [ConfigKey.FileCheckpointingEnabled] = "文件检查点 (true/false)",
        [ConfigKey.ShowTurnDuration]         = "显示轮次耗时 (true/false)",
        [ConfigKey.Model]                    = "覆盖默认模型 (模型 ID)",
        [ConfigKey.AlwaysThinkingEnabled]    = "扩展思考 (true/false)",
        [ConfigKey.PermissionsDefaultMode]   = "默认权限模式 (allow/ask/deny)",
        [ConfigKey.Language]                 = "首选语言 (zh/en/ja/...)",
        [ConfigKey.FastMode]                 = "快速模式 (true/false)",
        [ConfigKey.EffortLevel]              = "推理力度 (low/medium/high/xhigh)",
        [ConfigKey.OutputStyle]              = "输出风格 (concise/verbose/normal)",
    }.ToFrozenDictionary();

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "get":
                await GetConfigAsync(context, args);
                break;
            case "set":
                await SetConfigAsync(context, args);
                break;
            case CrudActionConstants.Remove:
            case CrudActionConstants.Delete:
            case CrudActionConstants.Rm:
                await RemoveConfigAsync(context, args);
                break;
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
            default:
                await ListConfigAsync(context);
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task GetConfigAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config get <key>{AnsiStyleConstants.Reset}");
            return;
        }

        var key = args[1];
        var configService = ResolveConfigService(context);
        var value = await configService.GetAsync(key, context.CancellationToken).ConfigureAwait(false);
        if (value is not null)
        {
            TerminalHelper.WriteLine($"{key} = {value}");
        }
        else
        {
            TerminalHelper.WriteLine($"配置项 '{key}' 不存在");
        }
    }

    private static async Task SetConfigAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config set <key> <value>{AnsiStyleConstants.Reset}");
            return;
        }

        var key = args[1];
        var value = string.Join(" ", args[2..]);

        if (ConfigKeyExtensions.FromValue(key) is { } configKey && KnownSettings.TryGetValue(configKey, out var description))
        {
            TerminalHelper.WriteLine($"  {key}: {description}");
        }

        var configService = ResolveConfigService(context);
        await configService.SetAsync(key, value, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine($"{TerminalColors.Success}已设置: {key} = {value}{AnsiStyleConstants.Reset}");
    }

    private static async Task RemoveConfigAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /config remove <key>{AnsiStyleConstants.Reset}");
            return;
        }

        var key = args[1];
        var configService = ResolveConfigService(context);
        var removed = await configService.RemoveAsync(key, context.CancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}配置项 '{key}' 不存在{AnsiStyleConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine($"{TerminalColors.Success}已移除: {key}{AnsiStyleConstants.Reset}");
    }

    private static async Task ListConfigAsync(ChatCommandContext context)
    {
        var configService = ResolveConfigService(context);
        var config = await configService.GetAllAsync(context.CancellationToken).ConfigureAwait(false);

        var panel = new TabPanel(
            ["当前配置", "已知配置项"],
            tabIndex => tabIndex switch
            {
                0 => RenderCurrentConfig(config),
                1 => RenderKnownSettings(config),
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private static string RenderCurrentConfig(Dictionary<string, string> config)
    {
        var sb = new StringBuilder();
        if (config.Count > 0)
        {
            foreach (var kvp in config.OrderBy(x => x.Key))
            {
                var desc = ConfigKeyExtensions.FromValue(kvp.Key) is { } ck && KnownSettings.TryGetValue(ck, out var d) ? $"  ({d})" : "";
                sb.AppendLine($"  {kvp.Key} = {kvp.Value}{desc}");
            }
        }
        else
        {
            sb.AppendLine("  当前没有配置项");
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config set <key> <value> 设置配置{AnsiStyleConstants.Reset}");
        return sb.ToString();
    }

    private static string RenderKnownSettings(Dictionary<string, string> config)
    {
        var sb = new StringBuilder();
        foreach (var kvp in KnownSettings.OrderBy(x => x.Key))
        {
            var keyStr = kvp.Key.ToValue();
            var hasValue = config.ContainsKey(keyStr);
            var marker = hasValue ? $" {TerminalColors.Success}✓{AnsiStyleConstants.Reset}" : "";
            sb.AppendLine($"  {keyStr}{marker} - {kvp.Value}");
        }

        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config set <key> <value> 设置配置{AnsiStyleConstants.Reset}");
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}使用 /config remove <key> 移除配置{AnsiStyleConstants.Reset}");
        return sb.ToString();
    }

    private static IConfigurationService ResolveConfigService(ChatCommandContext context)
    {
        var service = context.Services?.ServiceProvider?.GetService(typeof(IConfigurationService)) as IConfigurationService;
        return service ?? throw new InvalidOperationException("IConfigurationService 未注册，无法操作配置");
    }
}
