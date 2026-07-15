
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.AddDir, Description = "添加额外的工作目录", Usage = "/add-dir <path> [--remember]", Category = ChatCommandCategory.Code)]
public sealed class AddDirCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.AddDir;
    public string Description => "添加额外的工作目录";
    public string Usage => "/add-dir <path> [--remember]";
    public string[] Aliases => [];
    public string ArgumentHint => "<path> [--remember]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args))
        {
            ShowCurrentDirectories(context);
            return ChatCommandResult.Continue();
        }

        var remember = false;
        var pathPart = args;
        if (args.EndsWith(" --remember", StringComparison.OrdinalIgnoreCase))
        {
            remember = true;
            pathPart = args[..^11].Trim();
        }

        if (!context.Services.FileSystem.DirectoryExists(pathPart))
        {
            TerminalHelper.WriteLine($"目录不存在: {pathPart}");
            return ChatCommandResult.Continue();
        }

        var fullPath = Path.GetFullPath(pathPart);

        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("工作区服务未初始化");
            }
            return ChatCommandResult.Continue();
        }

        var added = workspaceService.AddDirectory(fullPath);

        if (added)
        {
            TrustDirectory(context, fullPath);

            if (remember)
            {
                await PersistDirectoryAsync(context, fullPath).ConfigureAwait(false);
                TerminalHelper.WriteLine($"已添加工作目录: {fullPath} (已保存到本地设置)");
            }
            else
            {
                TerminalHelper.WriteLine($"已添加工作目录: {fullPath} (仅本次会话)");
            }

            TerminalHelper.WriteLine("使用 /permissions workspace 管理工作区目录");
        }
        else
        {
            TerminalHelper.WriteLine($"目录已存在: {fullPath}");
        }

        ShowCurrentDirectories(context);
        return ChatCommandResult.Continue();
    }

    private static void TrustDirectory(ChatCommandContext context, string fullPath)
    {
        var trustManager = ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
        if (trustManager is null) return;

        if (!trustManager.IsTrusted(fullPath))
        {
            trustManager.Trust(fullPath);
        }
    }

    private static async Task PersistDirectoryAsync(ChatCommandContext context, string fullPath)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null) return;

        try
        {
            var existing = await configService.GetAsync("permissions.additionalDirectories",
                context.CancellationToken).ConfigureAwait(false);
            var dirs = string.IsNullOrEmpty(existing) ? [] : existing.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (!dirs.Contains(fullPath))
            {
                dirs = [.. dirs, fullPath];
                await configService.SetAsync("permissions.additionalDirectories",
                    string.Join(";", dirs), context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // 持久化失败不影响主流程
            System.Diagnostics.Trace.WriteLine($"持久化目录设置失败: {ex.Message}");
        }
    }

    private static void ShowCurrentDirectories(ChatCommandContext context)
    {
        var workspaceService = context.Services.WorkspaceService;
        if (workspaceService is null) return;

        var dirs = workspaceService.GetAdditionalDirectories();
        if (dirs.Count > 0)
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("当前额外工作目录:");
            foreach (var dir in dirs)
            {
                TerminalHelper.WriteLine($"  {dir}");
            }
        }
    }
}
