
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Files, Description = "列出当前上下文中的文件", Usage = "/files", Category = ChatCommandCategory.Code)]
public sealed class FilesCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Files;
    public string Description => "列出当前上下文中的文件";
    public string Usage => "/files";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var tracker = context.Services.FileOperationTracker;

        if (tracker is null)
        {
            TerminalHelper.WriteLine("上下文中无文件");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var entries = tracker.GetAllEntries();
        if (entries.Count == 0)
        {
            TerminalHelper.WriteLine("上下文中无文件");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var filePaths = tracker.GetOperatedFilePaths();
        var cwd = context.Services.FileSystem.GetCurrentDirectory();

        TerminalHelper.WriteLine($"上下文中的文件 ({filePaths.Count} 个):");
        TerminalHelper.NewLine();

        foreach (var path in filePaths)
        {
            var relativePath = DirectoryHelper.GetRelativePath(cwd, path);
            var fileEntries = entries.Where(e =>
                string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase)).ToList();

            var operations = string.Join(", ", fileEntries
                .GroupBy(e => e.OperationType)
                .Select(g => $"{g.Key}({g.Count()})"));

            var lastOp = fileEntries.MaxBy(e => e.Timestamp);
            var timeAgo = FormatTimeAgo(lastOp?.Timestamp ?? DateTime.MinValue);

            TerminalHelper.WriteLine($"  {relativePath}");
            TerminalHelper.WriteLine($"    操作: {operations}  最后: {timeAgo}");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static string FormatTimeAgo(DateTime timestamp)
    {
        var diff = DateTime.UtcNow - timestamp;
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}小时前";
        return $"{(int)diff.TotalDays}天前";
    }
}
