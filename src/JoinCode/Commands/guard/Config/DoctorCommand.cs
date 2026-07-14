namespace JoinCode.ChatCommands;

/// <summary>
/// /doctor 命令 — 对齐 TS doctor.tsx + doctorDiagnostic.ts
/// TS 使用 Doctor React 组件 + getDoctorDiagnostic 收集诊断信息
/// 对齐内容：运行时版本+安装路径+工具检查+环境变量+API连接+权限+MCP+搜索工具状态
/// 架构差异：TS 有 npm/native/package-manager 安装类型检测，C# 为 NativeAOT 单文件发布
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Doctor, Description = "诊断环境配置和依赖", Usage = "/doctor", Category = ChatCommandCategory.Config)]
public sealed class DoctorCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Doctor;
    public string Description => "诊断环境配置和依赖";
    public string Usage => "/doctor";
    public string[] Aliases => ["dr"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sb = new StringBuilder();

        // 版本信息 — 对齐 TS DiagnosticInfo.version
        sb.AppendLine("[应用版本]");
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var installPath = System.AppContext.BaseDirectory;
        sb.AppendLine($"  版本: {version}");
        sb.AppendLine($"  安装路径: {installPath}");

        sb.AppendLine("\n[.NET 运行时]");
        var dotnetVersion = Environment.Version;
        sb.AppendLine($"  版本: {dotnetVersion}");
        sb.AppendLine($"  框架: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"  OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"  架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

        sb.AppendLine("\n[Git]");
        var gitCheck = await RunCommandAsync("git", "--version", context.CancellationToken).ConfigureAwait(false);
        if (gitCheck.success)
        {
            sb.AppendLine($"{TerminalColors.Success}  {gitCheck.output}{AnsiStyleConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"{TerminalColors.Error}  Git 未安装或不在 PATH 中{AnsiStyleConstants.Reset}");
        }

        sb.AppendLine("\n[常用工具]");
        var toolResults = new StringBuilder();
        await AppendToolCheckAsync(toolResults, "dotnet", "--version", "dotnet CLI", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "node", "--version", "Node.js", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "npm", "--version", "npm", context.CancellationToken).ConfigureAwait(false);
        await AppendToolCheckAsync(toolResults, "python", "--version", "Python", context.CancellationToken).ConfigureAwait(false);
        sb.Append(toolResults);

        // 搜索工具状态 — 对齐 TS DiagnosticInfo.ripgrepStatus
        sb.AppendLine("\n[搜索工具]");
        var searchResults = new StringBuilder();
        await AppendToolCheckAsync(searchResults, "rg", "--version", "ripgrep", context.CancellationToken).ConfigureAwait(false);
        sb.Append(searchResults);

        sb.AppendLine("\n[环境变量]");
        AppendEnvironmentVariable(sb, ProviderEnvVarConstants.OpenAiApiKey, "OpenAI API Key");
        AppendEnvironmentVariable(sb, ProviderEnvVarConstants.AzureOpenAiApiKey, "Azure OpenAI API Key");
        AppendEnvironmentVariable(sb, ProviderEnvVarConstants.AnthropicApiKey, "Anthropic API Key");

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

    private async Task AppendToolCheckAsync(StringBuilder sb, string command, string args, string name, CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(command, args, cancellationToken).ConfigureAwait(false);
        if (result.success)
        {
            sb.AppendLine($"{TerminalColors.Success}  {name}: {result.output.Trim()}{AnsiStyleConstants.Reset}");
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
            sb.AppendLine($"{TerminalColors.Success}  {displayName}: 已设置{AnsiStyleConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"  {displayName}: 未设置");
        }
    }

    private static async Task<(bool success, string output)> RunCommandAsync(string command, string args, CancellationToken cancellationToken, IProcessService? processService = null)
    {
        try
        {
            if (processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = command,
                    Arguments = args
                };

                var result = await processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
                return (result.Success, string.IsNullOrEmpty(result.StandardOutput) ? result.StandardError : result.StandardOutput);
            }

            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return (process.ExitCode == 0, string.IsNullOrEmpty(output) ? error : output);
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

    private static async Task AppendApiConnectionAsync(StringBuilder sb, ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null)
        {
            sb.AppendLine("  配置服务不可用");
            return;
        }

        var provider = Environment.GetEnvironmentVariable(JccEnvVarConstants.Provider)
            ?? await configService.GetAsync(ConfigKeyConstants.Provider, context.CancellationToken).ConfigureAwait(false)
            ?? ProviderKind.OpenAI.ToValue();

        var endpoint = Environment.GetEnvironmentVariable(JccEnvVarConstants.Endpoint)
            ?? await configService.GetAsync(ConfigKeyConstants.Endpoint, context.CancellationToken).ConfigureAwait(false);

        var apiKey = ResolveProviderDefinition(context, provider)?.ResolveApiKeyFromEnv()
            ?? Environment.GetEnvironmentVariable(JccEnvVarConstants.ApiKey);

        if (string.IsNullOrEmpty(apiKey))
        {
            sb.AppendLine($"  {TerminalColors.Warning}Provider: {provider} — API Key 未设置{AnsiStyleConstants.Reset}");
            return;
        }

        sb.AppendLine($"  Provider: {TerminalColors.Success}{provider}{AnsiStyleConstants.Reset}");

        if (!string.IsNullOrEmpty(endpoint))
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await http.GetAsync(endpoint, context.CancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    sb.AppendLine($"  Endpoint: {TerminalColors.Success}可达 ({(int)response.StatusCode}){AnsiStyleConstants.Reset}");
                }
                else
                {
                    sb.AppendLine($"  Endpoint: {TerminalColors.Warning}响应异常 ({(int)response.StatusCode}){AnsiStyleConstants.Reset}");
                }
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine($"  Endpoint: {TerminalColors.Error}连接超时{AnsiStyleConstants.Reset}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Endpoint: {TerminalColors.Error}{ex.Message}{AnsiStyleConstants.Reset}");
            }
        }
        else
        {
            sb.AppendLine("  Endpoint: 使用默认端点");
        }

        sb.AppendLine($"  API Key: {TerminalColors.Success}已设置{AnsiStyleConstants.Reset}");
    }

    private static void AppendPermissionConfig(StringBuilder sb, ChatCommandContext context)
    {
        var trustManager = ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
        if (trustManager is null)
        {
            sb.AppendLine("  信任管理器不可用");
            return;
        }

        var cwd = context.Services!.FileSystem.GetCurrentDirectory();
        if (trustManager.IsTrusted(cwd))
        {
            sb.AppendLine($"  工作目录: {TerminalColors.Success}已信任 ({cwd}){AnsiStyleConstants.Reset}");
        }
        else
        {
            sb.AppendLine($"  工作目录: {TerminalColors.Warning}未信任 ({cwd}){AnsiStyleConstants.Reset}");
            sb.AppendLine("    使用 /trust add 添加信任");
        }

        var workspaceService = context.Services!.WorkspaceService;
        if (workspaceService is not null)
        {
            var dirs = workspaceService.GetAdditionalDirectories();
            if (dirs.Count > 0)
            {
                sb.AppendLine($"  额外工作目录: {dirs.Count} 个");
                foreach (var dir in dirs)
                {
                    var trusted = trustManager.IsTrusted(dir);
                    var status = trusted ? $"{TerminalColors.Success}已信任{AnsiStyleConstants.Reset}" : $"{TerminalColors.Warning}未信任{AnsiStyleConstants.Reset}";
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
                    ? $"{TerminalColors.Success}已连接{AnsiStyleConstants.Reset}"
                    : $"{TerminalColors.Error}断开{AnsiStyleConstants.Reset}";

                sb.AppendLine($"    {serverName} — {status}");

                if (connected)
                {
                    try
                    {
                        var toolsResult = await client.ListToolsAsync(context.CancellationToken).ConfigureAwait(false);
                        if (toolsResult.Success)
                            sb.AppendLine($"      工具: {toolsResult.Data!.Count} 个");
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
            sb.AppendLine($"  {TerminalColors.Error}MCP状态检查失败: {ex.Message}{AnsiStyleConstants.Reset}");
        }
    }
}
