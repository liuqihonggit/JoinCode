
namespace JoinCode.ChatCommands;

/// <summary>
/// /plugin 命令 — 对齐 TS plugin.tsx
/// TS 使用 React 交互式界面（Discover/Marketplace/Manage/Trust/Validate）
/// 对齐内容：list+install+uninstall+enable+disable 核心操作
/// 架构差异：TS 有 discover/marketplace/validate/trust-warning 交互式 UI，C# 为命令行操作
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Plugin, Description = "管理插件", Usage = "/plugin [list|install|uninstall|enable|disable] [name]", Category = ChatCommandCategory.Tools)]
public sealed class PluginCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Plugin;
    public string Description => "管理插件";
    public string Usage => "/plugin [list|install|uninstall|enable|disable] [name]";
    public string[] Aliases => ["plugins", "marketplace"];
    public string ArgumentHint => "[list|install|uninstall|enable|disable]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var pluginManager = context.Services!.PluginManager;
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args) || args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return await ListPluginsAsync(pluginManager);
        }

        if (args.StartsWith("install", StringComparison.OrdinalIgnoreCase))
        {
            var path = args["install".Length..].Trim();
            if (string.IsNullOrEmpty(path))
            {
                TerminalHelper.WriteLine("用法: /plugin install <exe-path>");
                return ChatCommandResult.Continue();
            }
            return await InstallPluginAsync(pluginManager, path, context);
        }

        if (args.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            var name = args["uninstall".Length..].Trim();
            if (string.IsNullOrEmpty(name))
            {
                TerminalHelper.WriteLine("用法: /plugin uninstall <name>");
                return ChatCommandResult.Continue();
            }
            return await UnloadPluginAsync(pluginManager, name);
        }

        if (args.StartsWith("enable", StringComparison.OrdinalIgnoreCase))
        {
            var name = args["enable".Length..].Trim();
            if (string.IsNullOrEmpty(name))
            {
                TerminalHelper.WriteLine("用法: /plugin enable <name>");
                return ChatCommandResult.Continue();
            }
            return await TogglePluginAsync(name, enable: true, context);
        }

        if (args.StartsWith("disable", StringComparison.OrdinalIgnoreCase))
        {
            var name = args["disable".Length..].Trim();
            if (string.IsNullOrEmpty(name))
            {
                TerminalHelper.WriteLine("用法: /plugin disable <name>");
                return ChatCommandResult.Continue();
            }
            return await TogglePluginAsync(name, enable: false, context);
        }

        TerminalHelper.WriteLine($"未知操作: {args}");
        TerminalHelper.WriteLine("支持: list, install, uninstall, enable, disable");
        return ChatCommandResult.Continue();
    }

    private static Task<ChatCommandResult> ListPluginsAsync(IPluginManager? pluginManager)
    {
        if (pluginManager is null)
        {
            TerminalHelper.WriteLine("插件管理器未初始化");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var allPlugins = new List<(string Name, string Type, string Status)>();

        foreach (var name in pluginManager.LoadedWorkflowPluginNames)
        {
            allPlugins.Add((name, "工作流", "已加载"));
        }

        foreach (var name in pluginManager.LoadedExternalPluginNames)
        {
            allPlugins.Add((name, "外部", "已加载"));
        }

        if (allPlugins.Count == 0)
        {
            TerminalHelper.WriteLine("  当前无已加载的插件");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("使用 /plugin install <exe-path> 安装插件");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        // 交互模式：PaginatedList
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var list = new PaginatedList<(string Name, string Type, string Status)>(
                "插件列表",
                allPlugins.ToArray(),
                p => $"  {p.Name} ({p.Type}) — {p.Status}",
                pageSize: 10);

            return list.ShowAsync(CancellationToken.None)
                .ContinueWith(_ => ChatCommandResult.Continue(), TaskContinuationOptions.ExecuteSynchronously);
        }

        // 非交互模式：纯文本
        TerminalHelper.WriteLine("插件列表:");
        TerminalHelper.NewLine();
        foreach (var p in allPlugins)
        {
            TerminalHelper.WriteLine($"  {p.Name} ({p.Type}) — {p.Status}");
        }
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /plugin install <exe-path> 安装插件");
        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static async Task<ChatCommandResult> InstallPluginAsync(IPluginManager? pluginManager, string exePath, ChatCommandContext context)
    {
        if (pluginManager is null)
        {
            TerminalHelper.WriteLine("插件管理器未初始化");
            return ChatCommandResult.Continue();
        }

        if (!context.Services!.FileSystem.FileExists(exePath))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}插件路径不存在: {exePath}{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var pluginName = Path.GetFileNameWithoutExtension(exePath);

        try
        {
            var host = await pluginManager.LoadExternalPluginAsync(exePath, pluginName, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已安装并加载插件: {pluginName}{AnsiStyleConstants.Reset}");

            var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
            if (configService is not null)
            {
                var autoLoadJson = await configService.GetAsync("plugins.autoLoadExternalPlugins", context.CancellationToken).ConfigureAwait(false);
                var autoLoad = string.IsNullOrEmpty(autoLoadJson)
                    ? new List<string>()
                    : JsonSerializer.Deserialize(autoLoadJson, CliJsonContext.Default.ListString) ?? new List<string>();
                if (!autoLoad.Contains(pluginName))
                {
                    autoLoad.Add(pluginName);
                    var updatedJson = JsonSerializer.Serialize(autoLoad, CliJsonContext.Default.ListString);
                    await configService.SetAsync("plugins.autoLoadExternalPlugins", updatedJson, context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine($"已添加到自动加载列表: {pluginName}");
                }
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("安装插件", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> TogglePluginAsync(string name, bool enable, ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("配置服务未初始化，无法修改插件状态");
            }
            return ChatCommandResult.Continue();
        }

        try
        {
            var disabledJson = await configService.GetAsync("plugins.disabledPlugins", context.CancellationToken).ConfigureAwait(false);
            var disabled = string.IsNullOrEmpty(disabledJson)
                ? new List<string>()
                : JsonSerializer.Deserialize(disabledJson, CliJsonContext.Default.ListString) ?? new List<string>();

            if (enable)
            {
                if (disabled.Remove(name))
                {
                    var updatedJson = JsonSerializer.Serialize(disabled, CliJsonContext.Default.ListString);
                    await configService.SetAsync("plugins.disabledPlugins", updatedJson, context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine($"{TerminalColors.Success}已启用插件: {name}（重启后生效）{AnsiStyleConstants.Reset}");
                }
                else
                {
                    TerminalHelper.WriteLine($"插件 '{name}' 未被禁用");
                }
            }
            else
            {
                if (!disabled.Contains(name))
                {
                    disabled.Add(name);
                    var updatedJson = JsonSerializer.Serialize(disabled, CliJsonContext.Default.ListString);
                    await configService.SetAsync("plugins.disabledPlugins", updatedJson, context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine($"{TerminalColors.Success}已禁用插件: {name}（重启后生效）{AnsiStyleConstants.Reset}");
                }
                else
                {
                    TerminalHelper.WriteLine($"插件 '{name}' 已被禁用");
                }
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError($"{(enable ? "启用" : "禁用")}插件", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> UnloadPluginAsync(IPluginManager? pluginManager, string name)
    {
        if (pluginManager is null)
        {
            TerminalHelper.WriteLine("插件管理器未初始化");
            return ChatCommandResult.Continue();
        }

        if (!pluginManager.IsPluginLoaded(name))
        {
            TerminalHelper.WriteLine($"插件 '{name}' 未加载");
            return ChatCommandResult.Continue();
        }

        try
        {
            var result = await pluginManager.UnloadPluginAsync(name).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                TerminalHelper.WriteLine($"已卸载插件: {name}");
            }
            else
            {
                TerminalHelper.WriteLine($"卸载插件失败: {name} - {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("卸载插件", ex);
        }

        return ChatCommandResult.Continue();
    }
}
