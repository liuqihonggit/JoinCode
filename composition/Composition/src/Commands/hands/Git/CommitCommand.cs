namespace JoinCode.ChatCommands;

/// <summary>
/// /commit 命令 - 创建 Git 提交
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Commit, Description = "创建 Git 提交", Usage = "/commit [message]", Category = ChatCommandCategory.Code, ArgumentHint = "[message]")]
public sealed class CommitCommand : ChatCommandBase
{
    // 对齐 TS: Git Safety Protocol — 禁止提交的文件模式（FrozenSet 类型规范，子串匹配仍需遍历）
    private static readonly FrozenSet<string> SecretFilePatterns = FrozenSet.Create(
        ".env", "credentials", "secret", "password", "apikey", "token");

    /// <summary>
    /// 渐进式披露 — 已读说明的会话状态(key: sessionId, value: 确认时间)
    /// <para>首次 /commit 返回说明不执行,二次 /commit(60s 内)确认执行。对齐 sed 两阶段确认模式。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> ReadConfirmedSessions = new(StringComparer.Ordinal);

    /// <summary>
    /// 读说明确认窗口 — 60s 内有效(对齐 sed SedConfirmationWindow)
    /// </summary>
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(60);

    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 渐进式披露:首次调用返回说明不执行,二次确认执行(SessionId 为空时跳过,兼容测试)
        var sessionId = context.SessionId;
        if (!string.IsNullOrEmpty(sessionId))
        {
            if (!IsReadConfirmed(sessionId))
            {
                ShowUsageDisclosure();
                MarkReadConfirmed(sessionId);
                TerminalHelper.WriteLine($"{TerminalColors.Muted}\n再次调用 /commit 确认执行(60s 内有效)。{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }
            // 已读确认,清除状态,继续执行
            ReadConfirmedSessions.TryRemove(sessionId, out _);
        }

        TerminalHelper.WriteLine($"{TerminalColors.Muted}正在创建提交...{AnsiStyleConstants.Reset}");

        var fs = context.GetCommandServices().FileSystem;
        var gitRunner = ChatCommandBase.GetService<IGitCommandRunner>(context)!;
        var status = await RunGitCommandAsync("status --porcelain", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
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
            message = await GenerateCommitMessageAsync(context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
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
        var addResult = await RunGitCommandAsync("add -A", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
        var escapedMessage = message.Replace("\"", "\\\"");
        var commitResult = await RunGitCommandAsync($"commit -m \"{escapedMessage}\"", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);

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

    private static async Task<string> GenerateCommitMessageAsync(CancellationToken cancellationToken, IFileSystem fs, IGitCommandRunner gitRunner)
    {
        try
        {
            var diff = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --cached --stat", cancellationToken, fs, gitRunner).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(diff))
            {
                diff = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --stat", cancellationToken, fs, gitRunner).ConfigureAwait(false);
            }

            var files = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --name-only", cancellationToken, fs, gitRunner).ConfigureAwait(false);
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

    private static async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken, IFileSystem fs, IGitCommandRunner gitRunner)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var result = await gitRunner.ExecuteAsync(arguments, fs.GetCurrentDirectory(), cts.Token).ConfigureAwait(false);
            return string.IsNullOrEmpty(result.Output) ? result.Error : result.Output;
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("执行Git命令", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// 是否已读说明确认(60s 窗口内)
    /// </summary>
    private static bool IsReadConfirmed(string sessionId)
    {
        if (ReadConfirmedSessions.TryGetValue(sessionId, out var confirmedAt))
        {
            if (DateTime.UtcNow - confirmedAt <= ConfirmationWindow)
                return true;
            ReadConfirmedSessions.TryRemove(sessionId, out _);
        }
        return false;
    }

    /// <summary>
    /// 标记已读说明确认
    /// </summary>
    private static void MarkReadConfirmed(string sessionId)
    {
        ReadConfirmedSessions[sessionId] = DateTime.UtcNow;
    }

    /// <summary>
    /// 显示使用说明(渐进式披露)— 包含减法诚实原则
    /// </summary>
    private static void ShowUsageDisclosure()
    {
        TerminalHelper.WriteLine($"{TerminalColors.Warning}=== /commit 使用说明(渐进式披露)==={AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine("/commit 创建 Git 提交,自动执行:");
        TerminalHelper.WriteLine("  1. 敏感文件检测(.env/credentials/secret/password/apikey/token 禁止提交)");
        TerminalHelper.WriteLine("  2. 提交信息生成(基于 git diff)");
        TerminalHelper.WriteLine("  3. 用户确认");
        TerminalHelper.WriteLine("  4. git add -A + git commit -m \"msg\"");
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{TerminalColors.Warning}提交信息规范(减法诚实原则):{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine("  - 禁止\"已移除\"括号噪声:用户明确移除某物后,禁止写\"新增 X(无 Y)\"");
        TerminalHelper.WriteLine("  - 只描述实际新增/修改的内容,不提已消失之物");
        TerminalHelper.WriteLine("  - 格式: 类型: 描述(feat/fix/refactor/docs/test/chore)");
        TerminalHelper.WriteLine("  - 禁止包含分支名、PR/Issue 编号、无意义标记");
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine("用法: /commit [可选提交信息]");
    }
}
