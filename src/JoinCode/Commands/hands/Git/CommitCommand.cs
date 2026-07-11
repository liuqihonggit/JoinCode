namespace JoinCode.ChatCommands;

/// <summary>
/// /commit 命令 - 创建 Git 提交
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Commit, Description = "创建 Git 提交", Usage = "/commit [message]", Category = ChatCommandCategory.Code)]
public sealed class CommitCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Commit;
    public string Description => "创建 Git 提交";
    public string Usage => "/commit [message]";
    public string[] Aliases => [];
    public string ArgumentHint => "[message]";
    public bool IsHidden => false;

    // 对齐 TS: Git Safety Protocol — 禁止提交的文件模式
    private static readonly string[] SecretFilePatterns = [".env", "credentials", "secret", "password", "apikey", "token"];

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine($"{TerminalColors.Muted}正在创建提交...{AnsiStyleConstants.Reset}");

        var fs = context.Services!.FileSystem;
        var processService = ChatCommandBase.GetService<IProcessService>(context)!;
        var status = await RunGitCommandAsync("status --porcelain", context.CancellationToken, fs, processService).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status))
        {
            // 对齐 TS: 不创建空提交
            TerminalHelper.WriteLine("没有要提交的变更");
            return ChatCommandResult.Continue();
        }

        // 对齐 TS: Git Safety Protocol — 检查是否包含敏感文件
        var files = status.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            .ToList();

        var secretFiles = files.Where(f =>
            SecretFilePatterns.Any(p => f.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();

        if (secretFiles.Count > 0)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}警告: 以下文件可能包含敏感信息:{AnsiStyleConstants.Reset}");
            foreach (var f in secretFiles)
                TerminalHelper.WriteLine($"  {TerminalColors.Warning}{f}{AnsiStyleConstants.Reset}");

            if (!(context.Confirm?.Invoke("确认提交这些文件？") ?? false))
            {
                TerminalHelper.WriteLine("取消提交");
                return ChatCommandResult.Continue();
            }
        }

        TerminalHelper.WriteLine("=== 要提交的文件 ===");
        TerminalHelper.WriteLine(status);

        var args = ChatCommandBase.GetSplitArgs(context);
        string message;
        if (args.Length > 0)
        {
            message = context.Arguments;
        }
        else
        {
            message = await GenerateCommitMessageAsync(context.CancellationToken, fs, processService).ConfigureAwait(false);
            TerminalHelper.WriteLine($"\n建议的提交信息: {message}");

            if (!(context.Confirm?.Invoke("使用此提交信息？") ?? false))
            {
                var customMessage = context.Prompt?.Invoke("请输入提交信息: ");
                if (customMessage is null)
                {
                    // 非交互模式或测试环境取消提交
                    if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
                    {
                        TerminalHelper.WriteLine("取消提交（非交互模式）");
                        return ChatCommandResult.Continue();
                    }
                    else
                    {
                        customMessage = TerminalHelper.ReadLine();
                    }
                }
                if (string.IsNullOrWhiteSpace(customMessage))
                {
                    TerminalHelper.WriteLine("取消提交");
                    return ChatCommandResult.Continue();
                }
                message = customMessage;
            }
        }

        if (!(context.Confirm?.Invoke("确认提交这些变更？") ?? false))
        {
            TerminalHelper.WriteLine("取消提交");
            return ChatCommandResult.Continue();
        }

        // 对齐 TS: Git Safety Protocol — 不使用 --no-verify、不使用 --amend
        var addResult = await RunGitCommandAsync("add -A", context.CancellationToken, fs, processService).ConfigureAwait(false);
        var escapedMessage = message.Replace("\"", "\\\"");
        var commitResult = await RunGitCommandAsync($"commit -m \"{escapedMessage}\"", context.CancellationToken, fs, processService).ConfigureAwait(false);

        if (commitResult.Contains("error") || commitResult.Contains("fatal"))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}提交失败: {commitResult}{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}提交成功！{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine(commitResult);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<string> GenerateCommitMessageAsync(CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        try
        {
            var diff = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --cached --stat", cancellationToken, fs, processService).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(diff))
            {
                diff = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --stat", cancellationToken, fs, processService).ConfigureAwait(false);
            }

            var files = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --name-only", cancellationToken, fs, processService).ConfigureAwait(false);
            var fileList = files.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (fileList.Count == 1)
            {
                var file = fileList[0];
                var extension = Path.GetExtension(file);

                if (file.Contains("test", StringComparison.OrdinalIgnoreCase))
                    return $"添加 {Path.GetFileName(file)} 测试";

                if (extension is ".cs" or ".ts" or ".js" or ".py")
                    return $"更新 {Path.GetFileName(file)}";

                if (extension is ".md" or ".txt")
                    return $"更新文档: {Path.GetFileName(file)}";
            }

            var added = fileList.Count(f => f.Contains("new", StringComparison.OrdinalIgnoreCase));
            var deleted = fileList.Count(f => f.Contains("delete", StringComparison.OrdinalIgnoreCase));

            if (added > 0 && deleted == 0)
                return $"添加 {added} 个新文件";

            if (deleted > 0 && added == 0)
                return $"删除 {deleted} 个文件";

            if (fileList.Count <= 3)
                return $"更新: {string.Join(", ", fileList.Select(Path.GetFileName))}";

            return $"更新 {fileList.Count} 个文件";
        }
        catch
        {
            return "更新代码";
        }
    }

    private static async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken, IFileSystem fs, IProcessService processService)
    {
        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = fs.GetCurrentDirectory()
            };

            var result = await processService.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);

            return string.IsNullOrEmpty(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("执行Git命令", ex);
            return string.Empty;
        }
    }
}
