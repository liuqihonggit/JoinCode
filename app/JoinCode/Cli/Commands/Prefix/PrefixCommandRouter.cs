namespace JoinCode.Cli.Commands.Prefix;

/// <summary>
/// 前缀命令路由器 — 解析 ! / !! 前缀，路由到对应处理器。
/// 对齐 SlashCommandRunner 模式，但处理前缀命令而非斜杠命令。
/// !! 优先于 ! 解析，避免 !! 被 ! 误匹配。
/// </summary>
public static class PrefixCommandRouter
{
    private static readonly ShellPrefixCommandHandler _shellHandler = new();
    private static readonly SilentShellPrefixCommandHandler _silentHandler = new();

    /// <summary>
    /// 判断输入是否为前缀命令（! 或 !!）。
    /// 委托给 Parse 保证一致性：Parse 能解析出非空命令则返回 true。
    /// </summary>
    public static bool IsPrefixCommand(string input)
        => Parse(input) is not null;

    /// <summary>
    /// 解析前缀命令，返回 (前缀, 命令内容)；非前缀命令返回 null。
    /// </summary>
    public static (string Prefix, string Command)? Parse(string input)
    {
        if (string.IsNullOrEmpty(input) || input[0] != '!')
            return null;

        if (input.Length >= 2 && input[1] == '!')
        {
            var cmd = input[2..].TrimStart();
            if (cmd.Length == 0)
                return null;
            return ("!!", cmd);
        }

        var singleCmd = input[1..].TrimStart();
        if (singleCmd.Length == 0)
            return null;
        return ("!", singleCmd);
    }

    /// <summary>
    /// 执行前缀命令。
    /// </summary>
    /// <param name="input">完整输入（含 ! / !! 前缀）</param>
    /// <param name="context">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<PrefixCommandResult> ExecuteAsync(
        string input,
        PrefixCommandContext context,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(input);
        if (parsed is null)
            return PrefixCommandResult.NotHandled;

        var (prefix, command) = parsed.Value;
        IPrefixCommandHandler? handler = prefix switch
        {
            "!!" => _silentHandler,
            "!" => _shellHandler,
            _ => null,
        };

        if (handler is null)
            return PrefixCommandResult.NotHandled;

        return await handler.ExecuteAsync(command, context, cancellationToken).ConfigureAwait(false);
    }
}
