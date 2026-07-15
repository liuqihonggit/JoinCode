namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Trust, Description = "管理工作区信任目录", Usage = "/trust [add|remove|list|clear]", Category = ChatCommandCategory.Auth)]
public sealed class TrustCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Trust;
    public string Description => "管理工作区信任目录";
    public string Usage => "/trust [add|remove|list|clear]";
    public string[] Aliases => [];
    public string ArgumentHint => "[add|remove|list|clear]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var manager = ChatCommandBase.GetService<ITrustFolderManager>(context, typeof(ITrustFolderManager));
        if (manager is null)
            return Task.FromResult(ChatCommandResult.Continue());

        var workspacePath = context.Services.FileSystem.GetCurrentDirectory();
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args) || args.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus(manager, workspacePath);
        }
        else if (args.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            manager.Trust(workspacePath);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已信任当前工作区: {workspacePath}{AnsiStyleConstants.Reset}");
        }
        else if (args.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            manager.Untrust(workspacePath);
            TerminalHelper.WriteLine($"已移除工作区信任: {workspacePath}");
        }
        else if (args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListAll(manager, context.Services.FileSystem);
        }
        else if (args.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            var count = manager.GetAllTrustedFolders().Count;
            manager.ClearAll();
            TerminalHelper.WriteLine($"已清除所有信任目录 ({count} 个)");
        }
        else
        {
            TerminalHelper.WriteLine($"用法: {Usage}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("  /trust        显示当前工作区信任状态");
            TerminalHelper.WriteLine("  /trust add    信任当前工作区");
            TerminalHelper.WriteLine("  /trust remove 移除当前工作区信任");
            TerminalHelper.WriteLine("  /trust list   列出所有信任目录");
            TerminalHelper.WriteLine("  /trust clear  清除所有信任目录");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void ShowStatus(ITrustFolderManager manager, string workspacePath)
    {
        var isTrusted = manager.IsTrusted(workspacePath);
        if (isTrusted)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}当前工作区已信任: {workspacePath}{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"当前工作区未信任: {workspacePath}");
            TerminalHelper.WriteLine("使用 /trust add 添加信任");
        }
    }

    private static void ListAll(ITrustFolderManager manager, IFileSystem fs)
    {
        var folders = manager.GetAllTrustedFolders();
        if (folders.Count == 0)
        {
            TerminalHelper.WriteLine("暂无信任目录");
            return;
        }

        TerminalHelper.WriteLine($"信任目录 ({folders.Count} 个):");
        TerminalHelper.NewLine();
        foreach (var folder in folders)
        {
            var exists = fs.DirectoryExists(folder);
            var marker = exists ? " " : " [不存在]";
            TerminalHelper.WriteLine($"  {folder}{marker}");
        }
    }
}
