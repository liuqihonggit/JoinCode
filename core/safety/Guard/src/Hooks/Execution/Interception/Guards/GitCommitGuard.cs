namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// Git Commit 守卫 — 拦截通过 bash/powershell 直接执行的 git commit,引导使用 /commit 斜杠命令
/// <para>
/// 解决问题:LLM 用 bash 执行 git commit 会绕过 /commit 的安全检查(敏感文件检测、提交信息规范、
/// 减法诚实原则等)。本守卫拦截 git commit,返回 <see cref="CommandDecision.Redirect"/> 软引导到 /commit。
/// </para>
/// <para>
/// 检测范围:git/git.exe + commit 子命令(支持引号包裹路径、.exe 后缀)。
/// 不拦截 git 其他子命令(status/add/push 等),仅 commit。
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class GitCommitGuard : ICommandGuard
{
    /// <summary>
    /// 引导提示文本 — 说明禁止原因和正确做法
    /// </summary>
    private const string RedirectHint =
        "禁止通过 shell 直接执行 git commit。请改用 /commit 斜杠命令创建提交。" +
        "/commit 会自动执行:敏感文件检测、提交信息生成、用户确认、减法诚实原则校验(禁止\"已移除\"括号噪声)。" +
        "调用方式:直接输入 /commit [可选提交信息]。";

    /// <inheritdoc/>
    public string Name => "GitCommitGuard";

    /// <inheritdoc/>
    public int Priority => 1000;

    /// <inheritdoc/>
    public bool CanHandle(string command, IReadOnlyDictionary<string, object> context)
    {
        return IsGitCommitCommand(command);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context)
    {
        return new CommandDecision.Redirect("/commit", RedirectHint);
    }

    /// <summary>
    /// 检测命令是否为 git commit — 对齐 ShellBuildInterceptMiddleware.IsBuildCommand 解析模式
    /// <para>支持:git commit、git.exe commit、"git" commit、带路径的 git commit</para>
    /// </summary>
    /// <param name="command">待检测的命令</param>
    /// <returns>是 git commit 返回 true,否则 false</returns>
    internal static bool IsGitCommitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var trimmed = command.TrimStart();

        // 提取第一个 token(git 可执行路径,支持引号包裹)
        var (firstToken, afterFirst) = ExtractFirstToken(trimmed);
        if (firstToken is null || string.IsNullOrWhiteSpace(afterFirst)) return false;

        // 提取第二个 token(子命令,应为 commit)
        var (subCommand, _) = ExtractFirstToken(afterFirst);
        if (subCommand is null) return false;

        return IsGitCommitSubCommand(firstToken, subCommand);
    }

    /// <summary>
    /// 提取命令文本的第一个 token — 支持引号包裹的路径
    /// </summary>
    /// <param name="text">命令文本(已 TrimStart)</param>
    /// <returns>(token, 剩余文本);token 为 null 表示解析失败</returns>
    private static (string? Token, string Remaining) ExtractFirstToken(string text)
    {
        text = text.TrimStart();
        if (text.Length == 0) return (null, text);

        if (text.StartsWith('"'))
        {
            var closingQuote = text.IndexOf('"', 1);
            if (closingQuote < 0) return (null, text);
            return (text[1..closingQuote], text[(closingQuote + 1)..]);
        }

        var space = text.IndexOf(' ');
        if (space < 0) return (text, string.Empty);
        return (text[..space], text[(space + 1)..]);
    }

    /// <summary>
    /// 判断可执行文件是否为 git 且子命令为 commit
    /// </summary>
    /// <param name="executablePath">可执行文件路径或名称</param>
    /// <param name="subCommand">子命令(如 commit)</param>
    /// <returns>是 git commit 返回 true</returns>
    private static bool IsGitCommitSubCommand(string executablePath, string subCommand)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (!executableName.Equals("git", StringComparison.OrdinalIgnoreCase))
            return false;

        return subCommand.Equals("commit", StringComparison.OrdinalIgnoreCase);
    }
}
