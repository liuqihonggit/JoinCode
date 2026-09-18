namespace JoinCode.ChatCommands;

/// <summary>
/// /doctor 命令 — 对齐 TS doctor.tsx + doctorDiagnostic.ts
/// TS 使用 Doctor React 组件 + getDoctorDiagnostic 收集诊断信息
/// 对齐内容：运行时版本+安装路径+工具检查+环境变量+API连接+权限+MCP+搜索工具状态
/// 架构差异：TS 有 npm/native/package-manager 安装类型检测，C# 为 NativeAOT 单文件发布
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Doctor, Description = "诊断环境配置和依赖", Usage = "/doctor", Category = ChatCommandCategory.Config, Aliases = ["dr"])]
public sealed class DoctorCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /doctor 命令 — 收集并展示环境诊断信息
    /// 检查项包括:应用版本、.NET 运行时、Git、常用工具、搜索工具、环境变量、磁盘空间、网络状态、API 连接、权限配置、MCP 服务
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>命令执行结果,始终返回 Continue 表示继续会话</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sb = new StringBuilder();

        // 版本信息 — 对齐 TS DiagnosticInfo.version
        sb.AppendLine("[应用版本]");
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var installPath = System.AppContext.BaseDirectory;
        sb.AppendLine($"  版本: {version}");
        sb.AppendLine($"  安装路径: {installPath}");

        sb.AppendLine("\n[.NET 运行时]");
        var snapshot = JoinCode.Abstractions.Utils.EnvironmentSnapshot.CaptureQuick();
        sb.AppendLine($"  版本: {snapshot.RuntimeVersion}");
        sb.AppendLine($"  框架: {snapshot.FrameworkDescription}");
        sb.AppendLine($"  OS: {snapshot.OsDescription}");
        sb.AppendLine($"  架构: {snapshot.ProcessArchitecture}");

        sb.AppendLine("\n[Git]");
        var gitCheck = await RunCommandAsync("git", ["--version"], context.CancellationToken).ConfigureAwait(false);
        if (gitCheck.success)
        {
            sb.AppendLine($"{TerminalColors.Success}  {gitCheck.output}{AnsiStyleEnumConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"{TerminalColors.Error}  Git 未安装或不在 PATH 中{AnsiStyleEnumConstants.Reset}");
        }

        sb.AppendLine("\n[常用工具]");
        var toolResults = new StringBuilder();
        await AppendToolCheckAsync(toolResults, "dotnet", ["--version"], "dotnet CLI", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "node", ["--version"], "Node.js", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "npm", ["--version"], "npm", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "python", ["--version"], "Python", context.CancellationToken).ConfigureAwait(false);
        sb.Append(toolResults);

        // 搜索工具状态 — 对齐 TS DiagnosticInfo.ripgrepStatus
        sb.AppendLine("\n[搜索工具]");
        var searchResults = new StringBuilder();
        await AppendToolCheckAsync(searchResults, "rg", ["--version"], "ripgrep", context.CancellationToken).ConfigureAwait(false);
        sb.Append(searchResults);

        sb.AppendLine("\n[环境变量]");
        AppendEnvironmentVariable(sb, ProviderEnvVarEnumConstants.OpenAiApiKey, "OpenAI API Key");
        AppendEnvironmentVariable(sb, ProviderEnvVarEnumConstants.AzureOpenAiApiKey, "Azure OpenAI API Key");
        AppendEnvironmentVariable(sb, ProviderEnvVarEnumConstants.AnthropicApiKey, "Anthropic API Key");

        sb.AppendLine("\n[磁盘空间]");
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
        foreach (var drive in drives)
        {
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            var totalSpaceGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            var usedPercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;

            var status = usedPercent > 90 ? "[警告]" : "[正常]";
            sb.AppendLine($"  {drive.Name} {status} - 可用: {freeSpaceGB:F1} GB / 总计: {totalSpaceGB:F1} GB ({usedPercent:F1}% 已用)");
        }

        sb.AppendLine("\n[网络状态]");
        AppendNetworkStatus(sb, context);

        sb.AppendLine("\n[API 连接]");
        await AppendApiConnectionAsync(sb, context).ConfigureAwait(false);

        sb.AppendLine("\n[权限配置]");
        AppendPermissionConfig(sb, context);

        sb.AppendLine("\n[MCP 服务]");
        await AppendMcpServicesAsync(sb, context).ConfigureAwait(false);

        var dialog = new Dialog("环境诊断", sb.ToString(), ["关闭"]);
        await dialog.ShowAsync(context.CancellationToken).ConfigureAwait(false);

        return ChatCommandResult.Continue();
    }

    private async Task AppendToolCheckAsync(StringBuilder sb, string command, string[] args, string name, CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(command, args, cancellationToken).ConfigureAwait(false);
        if (result.success)
        {
            sb.AppendLine($"{TerminalColors.Success}  {name}: {result.output.Trim()}{AnsiStyleEnumConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"  {name}: 未找到");
        }
    }

    private static void AppendEnvironmentVariable(StringBuilder sb, string variableName, string displayName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrEmpty(value))
        {
            sb.AppendLine($"{TerminalColors.Success}  {displayName}: 已设置{AnsiStyleEnumConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"  {displayName}: 未设置");
        }
    }

    private static async Task<(bool success, string output)> RunCommandAsync(string command, string[] args, CancellationToken cancellationToken, IProcessService? processService = null)
    {
        try
        {
            var effectiveProcessService = processService ?? IO.ProcessService.ProcessServiceFactory.Create();
            var options = new ProcessOptions
            {
                FileName = command,
                ArgumentList = args,
                TimeoutMs = 10000
            };

            var result = await effectiveProcessService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
            return (result.Success, string.IsNullOrEmpty(result.StandardOutput) ? result.StandardError : result.StandardOutput);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    private static IProviderDefinition? ResolveProviderDefinition(ChatCommandContext context, string provider)
    {
        var registry = ChatCommandBase.GetService<IProviderDefinitionRegistry>(context, typeof(IProviderDefinitionRegistry));
        return registry?.TryGet(provider);
    }

    private static void AppendNetworkStatus(StringBuilder sb, ChatCommandContext context)
    {
        var networkService = ChatCommandBase.GetService<INetworkConnectivityService>(context, typeof(INetworkConnectivityService));
        if (networkService is null)
        {
            sb.AppendLine("  网络检测服务不可用");
            return;
        }

        var state = networkService.CurrentState;
        var stateText = state switch
        {
            NetworkConnectivityState.Online => $"{TerminalColors.Success}在线{AnsiStyleEnumConstants.Reset}",
            NetworkConnectivityState.OnlineWithVpn => $"{TerminalColors.Success}在线 (VPN){AnsiStyleEnumConstants.Reset}",
            NetworkConnectivityState.OnlineWithProxy => $"{TerminalColors.Success}在线 (代理){AnsiStyleEnumConstants.Reset}",
            _ => $"{TerminalColors.Error}离线{AnsiStyleEnumConstants.Reset}",
        };
        sb.AppendLine($"  状态: {stateText}");
        sb.AppendLine($"  VPN: {(networkService.IsVpnActive() ? "活跃" : "未检测到")}");

        var route = networkService.GetCurrentRoute();
        sb.AppendLine($"  路由: {route.Type}");

        var interfaces = networkService.GetActiveInterfaces();
        if (interfaces.Count > 0)
        {
            sb.AppendLine($"  活跃接口 ({interfaces.Count}):");
            foreach (var iface in interfaces)
            {
                sb.AppendLine($"    {iface.Name} [{iface.Kind}] {(iface.IsUp ? "UP" : "DOWN")}");
            }
        }
    }

    private static async Task AppendApiConnectionAsync(StringBuilder sb, ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null)
        {
            sb.AppendLine("  配置服务不可用");
            return;
        }

        var provider = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.Vendor)
            ?? await configService.GetAsync("profile", context.CancellationToken).ConfigureAwait(false)
            ?? VendorKind.OpenAi.ToValue();

        var endpoint = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.Endpoint);

        var apiKey = ResolveProviderDefinition(context, provider)?.ResolveApiKeyFromEnv();

        if (string.IsNullOrEmpty(apiKey))
        {
            sb.AppendLine($"  {TerminalColors.Warning}Provider: {provider} — API Key 未设置{AnsiStyleEnumConstants.Reset}");
            return;
        }

        sb.AppendLine($"  Provider: {TerminalColors.Success}{provider}{AnsiStyleEnumConstants.Reset}");

        if (!string.IsNullOrEmpty(endpoint))
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await http.GetAsync(endpoint, context.CancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    sb.AppendLine($"  Endpoint: {TerminalColors.Success}可达 ({(int)response.StatusCode}){AnsiStyleEnumConstants.Reset}");
                }
                else
                {
                    sb.AppendLine($"  Endpoint: {TerminalColors.Warning}响应异常 ({(int)response.StatusCode}){AnsiStyleEnumConstants.Reset}");
                }
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine($"  Endpoint: {TerminalColors.Error}连接超时{AnsiStyleEnumConstants.Reset}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Endpoint: {TerminalColors.Error}{ex.Message}{AnsiStyleEnumConstants.Reset}");
            }
        }
        else
        {
            sb.AppendLine("  Endpoint: 使用默认端点");
        }

        sb.AppendLine($"  API Key: {TerminalColors.Success}已设置{AnsiStyleEnumConstants.Reset}");
    }

    private static void AppendPermissionConfig(StringBuilder sb, ChatCommandContext context)
    {
        var trustManager = ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
        if (trustManager is null)
        {
            sb.AppendLine("  信任管理器不可用");
            return;
        }

        var cwd = context.GetCommandServices().FileSystem.GetCurrentDirectory();
        if (trustManager.IsTrusted(cwd))
        {
            sb.AppendLine($"  工作目录: {TerminalColors.Success}已信任 ({cwd}){AnsiStyleEnumConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"  工作目录: {TerminalColors.Warning}未信任 ({cwd}){AnsiStyleEnumConstants.Reset}");
            sb.AppendLine("    使用 /trust add 添加信任");
        }

        var workspaceService = context.GetCommandServices().WorkspaceService;
        if (workspaceService is not null)
        {
            var dirs = workspaceService.GetAdditionalDirectories();
            if (dirs.Any())
            {
                sb.AppendLine($"  额外工作目录: {dirs.Count()} 个");
                foreach (var dir in dirs)
                {
                    var trusted = trustManager.IsTrusted(dir);
                    var status = trusted ? $"{TerminalColors.Success}已信任{AnsiStyleEnumConstants.Reset}" : $"{TerminalColors.Warning}未信任{AnsiStyleEnumConstants.Reset}";
                    sb.AppendLine($"    {dir} — {status}");
                }
            }
            else
            {
                sb.AppendLine("  额外工作目录: 无");
            }
        }
    }

    private static async Task AppendMcpServicesAsync(StringBuilder sb, ChatCommandContext context)
    {
        var registry = ChatCommandBase.GetService<IMcpToolRegistry>(context, typeof(IMcpToolRegistry));
        if (registry is null)
        {
            sb.AppendLine("  MCP 工具注册表不可用");
            return;
        }

        try
        {
            var localCount = await registry.GetLocalToolCountAsync(context.CancellationToken).ConfigureAwait(false);
            var remoteCount = await registry.GetRemoteClientCountAsync(context.CancellationToken).ConfigureAwait(false);

            sb.AppendLine($"  本地工具: {localCount} 个");

            if (remoteCount == 0)
            {
                sb.AppendLine("  远程 MCP 服务器: 无连接");
                return;
            }

            sb.AppendLine($"  远程 MCP 服务器: {remoteCount} 个");

            var clients = await registry.GetAllRemoteClientsAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var (clientId, client) in clients)
            {
                var connected = client.IsConnected;
                var serverName = client.ServerInfo?.Name ?? clientId;
                var status = connected
                    ? $"{TerminalColors.Success}已连接{AnsiStyleEnumConstants.Reset}"
                    : $"{TerminalColors.Error}断开{AnsiStyleEnumConstants.Reset}";

                sb.AppendLine($"    {serverName} — {status}");

                if (connected)
                {
                    try
                    {
                        using var mcpCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                        mcpCts.CancelAfter(TimeSpan.FromSeconds(10));
                        var toolsResult = await client.ListToolsAsync(mcpCts.Token).ConfigureAwait(false);
                        if (toolsResult.Success)
                            sb.AppendLine($"      工具: {toolsResult.GetData().Count} 个");
                    }
                    catch
                    {
                        sb.AppendLine("      工具: 获取失败");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  {TerminalColors.Error}MCP状态检查失败: {ex.Message}{AnsiStyleEnumConstants.Reset}");
        }
    }
}
