namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Mcp, Description = "管理 MCP 服务器", Usage = "/mcp [list|status|add|remove|reconnect|enable|disable] [args]", Category = ChatCommandCategory.Tools)]
public sealed class McpCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Mcp;
    public string Description => "管理 MCP 服务器";
    public string Usage => "/mcp [list|status|add|remove|reconnect|enable|disable] [args]";
    public string[] Aliases => [];
    public string ArgumentHint => "[list|status|add|remove|reconnect|enable|disable]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var actionStr = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (actionStr)
        {
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
                await ListServersAsync(context);
                break;
            case McpActionConstants.Status:
                await ShowStatusAsync(context);
                break;
            case CrudActionConstants.Create:
            case CrudActionConstants.New:
                await AddServerAsync(context, args);
                break;
            case CrudActionConstants.Delete:
            case CrudActionConstants.Rm:
            case CrudActionConstants.Remove:
                await RemoveServerAsync(context, args);
                break;
            case McpActionConstants.Reconnect:
                await ReconnectServerAsync(context, args);
                break;
            case McpActionConstants.Enable:
                await ToggleServerAsync(context, args, ToggleAction.On);
                break;
            case McpActionConstants.Disable:
                await ToggleServerAsync(context, args, ToggleAction.Off);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}未知操作: {actionStr}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine("可用操作: list, status, add, remove, reconnect, enable, disable");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ListServersAsync(ChatCommandContext context)
    {
        var allServers = await ResolveConfigStore(context).GetAllServersAsync(context.CancellationToken).ConfigureAwait(false);
        var mcpRegistry = ResolveMcpRegistry(context);

        // 预收集已配置服务器内容
        var configuredContent = new StringBuilder();
        if (allServers.Count > 0)
        {
            configuredContent.AppendLine("已配置的服务器:");
            foreach (var (name, (scope, entry)) in allServers)
            {
                var transportInfo = entry.Type.Equals(McpTransportType.Stdio.ToValue(), StringComparison.OrdinalIgnoreCase)
                    ? $"stdio: {entry.Command}{(entry.Args is { Count: > 0 } ? " " + string.Join(" ", entry.Args) : "")}"
                    : $"{entry.Type}: {entry.Url}";
                var scopeLabel = scope == AgentMemoryScope.User.ToValue() ? "用户级" : "项目级";
                configuredContent.AppendLine($"  {name} ({scopeLabel})");
                configuredContent.AppendLine($"    {transportInfo}");
            }
        }
        else
        {
            configuredContent.AppendLine("  当前无已配置的 MCP 服务器");
        }

        configuredContent.AppendLine();
        configuredContent.Append($"  {TerminalColors.Muted}使用 /mcp add <name> <command|url> 添加服务器{AnsiStyleConstants.Reset}");

        // 预收集已连接服务器内容
        var connectedContent = new StringBuilder();
        if (mcpRegistry is not null)
        {
            var remoteClients = await mcpRegistry.GetAllRemoteClientsAsync(context.CancellationToken).ConfigureAwait(false);

            if (remoteClients.Count > 0)
            {
                connectedContent.AppendLine("已连接的服务器:");
                foreach (var (clientId, client) in remoteClients)
                {
                    var status = client.IsConnected ? "已连接" : "未连接";
                    var statusColor = client.IsConnected ? TerminalColors.Success : TerminalColors.Error;
                    var serverName = client.ServerInfo?.Name ?? clientId;

                    connectedContent.AppendLine($"  {serverName}");
                    connectedContent.AppendLine($"{statusColor}    状态: {status}{AnsiStyleConstants.Reset}");

                    if (client.ServerInfo is not null)
                    {
                        connectedContent.AppendLine($"    版本: {client.ServerInfo.Version ?? "unknown"}");
                    }

                    try
                    {
                        var tools = await client.ListToolsAsync(context.CancellationToken).ConfigureAwait(false);
                        if (tools.Success && tools.GetData().Count > 0)
                        {
                            connectedContent.AppendLine($"    工具数: {tools.GetData().Count}");
                        }
                    }
                    catch
                    {
                        connectedContent.AppendLine("    工具数: (无法获取)");
                    }

                    connectedContent.AppendLine();
                }
            }
            else if (allServers.Count == 0)
            {
                connectedContent.AppendLine("  当前无已配置或已连接的 MCP 服务器");
            }

            var localCount = await mcpRegistry.GetLocalToolCountAsync(context.CancellationToken).ConfigureAwait(false);
            var remoteCount = await mcpRegistry.GetRemoteClientCountAsync(context.CancellationToken).ConfigureAwait(false);
            connectedContent.AppendLine($"本地工具: {localCount} | 远程服务器: {remoteCount}");
        }
        else
        {
            connectedContent.AppendLine($"{TerminalColors.Warning}MCP 工具注册表不可用{AnsiStyleConstants.Reset}");
        }

        var panel = new TabPanel(
            ["已配置", "已连接"],
            tabIndex => tabIndex switch
            {
                0 => configuredContent.ToString(),
                1 => connectedContent.ToString(),
                _ => string.Empty
            });

        await panel.ShowAsync(context.CancellationToken).ConfigureAwait(false);
    }

    private static async Task ShowStatusAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("=== MCP 状态 ===\n");

        if (context.Services.ToolRegistry is not null)
        {
            TerminalHelper.WriteLine("已注册工具:");
            var tools = await context.Services.ToolRegistry.GetAllToolsAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var tool in tools.Take(20))
            {
                TerminalHelper.WriteLine($"  - {tool.Key}");
            }
            if (tools.Count > 20)
            {
                TerminalHelper.WriteLine($"  ... 还有 {tools.Count - 20} 个工具");
            }
            TerminalHelper.WriteLine($"  总计: {tools.Count} 个工具");
        }
        else
        {
            TerminalHelper.WriteLine("工具注册表不可用。");
        }

        var mcpRegistry = ResolveMcpRegistry(context);
        if (mcpRegistry is not null)
        {
            var localCount = await mcpRegistry.GetLocalToolCountAsync(context.CancellationToken).ConfigureAwait(false);
            var remoteCount = await mcpRegistry.GetRemoteClientCountAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"\n服务器统计:");
            TerminalHelper.WriteLine($"  本地工具数: {localCount}");
            TerminalHelper.WriteLine($"  远程服务器数: {remoteCount}");
        }
    }

    private static async Task AddServerAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /mcp add <name> <command|url> [args...] [-t stdio|sse|http] [-s user|project] [-e KEY=VALUE]{AnsiStyleConstants.Reset}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("示例:");
            TerminalHelper.WriteLine("  /mcp add myserver npx my-mcp-server");
            TerminalHelper.WriteLine("  /mcp add myserver npx my-mcp-server -t stdio -s user");
            TerminalHelper.WriteLine("  /mcp add sentry https://mcp.sentry.dev/mcp -t http -s project");
            TerminalHelper.WriteLine("  /mcp add myserver npx my-server -e API_KEY=xxx");
            return;
        }

        var name = args[1];
        var commandOrUrl = args[2];
        var remainingArgs = new List<string>();
        var transport = McpTransportType.Stdio.ToValue();
        var scope = AgentMemoryScope.Project.ToValue();
        var envVars = new Dictionary<string, string>();

        for (var i = 3; i < args.Length; i++)
        {
            if (args[i] is "-t" or "--transport" && i + 1 < args.Length)
            {
                transport = args[++i].ToLowerInvariant();
            }
            else if (args[i] is "-s" or "--scope" && i + 1 < args.Length)
            {
                scope = args[++i].ToLowerInvariant();
            }
            else if (args[i] is "-e" or "--env" && i + 1 < args.Length)
            {
                var envPair = args[++i];
                var eqIdx = envPair.IndexOf('=');
                if (eqIdx > 0)
                {
                    envVars[envPair[..eqIdx]] = envPair[(eqIdx + 1)..];
                }
            }
            else
            {
                remainingArgs.Add(args[i]);
            }
        }

        if (McpTransportTypeExtensions.FromValue(transport) is null)
        {
            string[] validTransports = [McpTransportTypeConstants.Stdio, McpTransportTypeConstants.Sse, McpTransportTypeConstants.Http, McpTransportTypeConstants.WebSocket];
            TerminalHelper.WriteLine($"{TerminalColors.Error}不支持的传输类型: {transport}，支持: {string.Join(", ", validTransports)}{AnsiStyleConstants.Reset}");
            return;
        }

        if (AgentMemoryScopeExtensions.FromValue(scope) is null)
        {
            string[] validScopes = [AgentMemoryScopeConstants.User, AgentMemoryScopeConstants.Project, AgentMemoryScopeConstants.Local];
            TerminalHelper.WriteLine($"{TerminalColors.Error}不支持的作用域: {scope}，支持: {string.Join(", ", validScopes)}{AnsiStyleConstants.Reset}");
            return;
        }

        var entry = new McpServerConfigEntry
        {
            Type = transport,
            Env = envVars.Count > 0 ? envVars : null
        };

        if (transport == McpTransportType.Stdio.ToValue())
        {
            entry.Command = commandOrUrl;
            entry.Args = remainingArgs.Count > 0 ? remainingArgs : null;
        }
        else
        {
            entry.Url = commandOrUrl;
        }

        try
        {
            var configStore = ResolveConfigStore(context);
            await configStore.AddServerAsync(name, entry, scope, context.CancellationToken).ConfigureAwait(false);
            var configPath = configStore.GetConfigPath(scope);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已添加 {transport.ToUpperInvariant()} MCP 服务器 '{name}' 到 {scope} 配置{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"配置文件: {configPath}");
            TerminalHelper.WriteLine("使用 /mcp reconnect " + name + " 连接服务器");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("添加MCP服务器", ex);
        }
    }

    private static async Task RemoveServerAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /mcp remove <name> [-s user|project]{AnsiStyleConstants.Reset}");
            return;
        }

        var name = args[1];
        var scope = AgentMemoryScope.Project.ToValue();

        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is "-s" or "--scope" && i + 1 < args.Length)
            {
                scope = args[++i].ToLowerInvariant();
            }
        }

        try
        {
            var removed = await ResolveConfigStore(context).RemoveServerAsync(name, scope, context.CancellationToken).ConfigureAwait(false);
            if (removed)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}已从 {scope} 配置中移除 MCP 服务器 '{name}'{AnsiStyleConstants.Reset}");
            }
            else
            {
                TerminalHelper.WriteLine($"{TerminalColors.Warning}在 {scope} 配置中未找到 MCP 服务器 '{name}'{AnsiStyleConstants.Reset}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("移除MCP服务器", ex);
        }
    }

    private static async Task ReconnectServerAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /mcp reconnect <server-name>{AnsiStyleConstants.Reset}");
            return;
        }

        var serverName = args[1];
        var mcpRegistry = ResolveMcpRegistry(context);
        if (mcpRegistry is null)
        {
            TerminalHelper.WriteLine("MCP 工具注册表不可用");
            return;
        }

        var client = await mcpRegistry.GetRemoteClientAsync(serverName, context.CancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到 MCP 服务器: {serverName}{AnsiStyleConstants.Reset}");
            return;
        }

        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(context.CancellationToken).ConfigureAwait(false);
            }

            await client.ConnectAsync(context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已重连 MCP 服务器: {serverName}{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("重连MCP服务器", ex);
        }
    }

    private static async Task ToggleServerAsync(ChatCommandContext context, string[] args, ToggleAction action)
    {
        if (args.Length < 2)
        {
            var actionName = action == ToggleAction.On ? "启用" : "禁用";
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /mcp {actionName} <server-name|all>{AnsiStyleConstants.Reset}");
            return;
        }

        var target = args[1];
        var mcpRegistry = ResolveMcpRegistry(context);
        if (mcpRegistry is null)
        {
            TerminalHelper.WriteLine("MCP 工具注册表不可用");
            return;
        }

        var enable = action == ToggleAction.On;

        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var remoteClients = await mcpRegistry.GetAllRemoteClientsAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var (clientId, client) in remoteClients)
            {
                try
                {
                    if (enable && !client.IsConnected)
                    {
                        await client.ConnectAsync(context.CancellationToken).ConfigureAwait(false);
                    }
                    else if (!enable && client.IsConnected)
                    {
                        await client.DisconnectAsync(context.CancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    ChatCommandBase.HandleError($"{clientId}操作", ex);
                }
            }

            TerminalHelper.WriteLine($"{TerminalColors.Success}已{(enable ? "启用" : "禁用")}所有 MCP 服务器{AnsiStyleConstants.Reset}");
            return;
        }

        var targetClient = await mcpRegistry.GetRemoteClientAsync(target, context.CancellationToken).ConfigureAwait(false);
        if (targetClient is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未找到 MCP 服务器: {target}{AnsiStyleConstants.Reset}");
            return;
        }

        try
        {
            if (enable && !targetClient.IsConnected)
            {
                await targetClient.ConnectAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已启用 MCP 服务器: {target}{AnsiStyleConstants.Reset}");
            }
            else if (!enable && targetClient.IsConnected)
            {
                await targetClient.DisconnectAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已禁用 MCP 服务器: {target}{AnsiStyleConstants.Reset}");
            }
            else
            {
                TerminalHelper.WriteLine($"MCP 服务器 {target} 已{(enable ? "启用" : "禁用")}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("MCP服务器操作", ex);
        }
    }

    private static IMcpServerConfigStore ResolveConfigStore(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IMcpServerConfigStore>(context, typeof(IMcpServerConfigStore))
            ?? throw new InvalidOperationException("MCP 配置存储服务未初始化");
    }

    private static IMcpToolRegistry? ResolveMcpRegistry(ChatCommandContext context)
    {
        if (context.Services.ToolRegistry is IMcpToolRegistry mcpRegistry)
        {
            return mcpRegistry;
        }

        return ChatCommandBase.GetService<IMcpToolRegistry>(context, typeof(IMcpToolRegistry));
    }
}
