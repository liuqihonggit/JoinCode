namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// HEREDOC 守卫 — 检测命令中的 HEREDOC 语法,自动转换为双引号多行字符串(迁移自旧 HeredocRewriter)
/// <para>
/// 解决问题:LLM 在 PowerShell 环境中使用 HEREDOC(cat &lt;&lt;'EOF'...EOF)会失败,
/// 因为 PowerShell 不支持 HEREDOC 语法。本守卫自动检测并转换为等效的双引号字符串。
/// </para>
/// <para>
/// 环境过滤:Bash 原生支持 HEREDOC,不转换;PowerShell/Cmd 不支持,需要转换。
/// </para>
/// <para>
/// 迁移自 HeredocRewriter(Priority=200)。
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class HeredocGuard : ICommandGuard
{
    private readonly ILogger<HeredocGuard>? _logger;

    // 匹配 $(cat <<'DELIMITER'\ncontent\nDELIMITER) 或 $(cat <<DELIMITER\ncontent\nDELIMITER)
    private static readonly Regex HeredocInCommandSubstitution = new(
        @"\$\(\s*cat\s*<<-?['""]?(\w+)['""]?\s*\r?\n(.*?)\r?\n\s*\1\s*\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // 匹配独立的 HEREDOC:<<'DELIMITER'\ncontent\nDELIMITER
    private static readonly Regex StandaloneHeredoc = new(
        @"<<-?['""]?(\w+)['""]?\s*\r?\n(.*?)\r?\n\s*\1",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// 构造 HEREDOC 守卫
    /// </summary>
    /// <param name="logger">日志器(可选)</param>
    public HeredocGuard(ILogger<HeredocGuard>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "HeredocGuard";

    /// <inheritdoc/>
    public int Priority => 200;

    /// <inheritdoc/>
    public bool CanHandle(string command, IReadOnlyDictionary<string, object> context)
    {
        return command.Contains("<<");
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context)
    {
        // Bash 原生支持 HEREDOC,不需要转换
        if (IsBashShell(context))
        {
            return new CommandDecision.Allow();
        }

        var result = command;

        // 先处理 $(cat <<'EOF'...EOF) 模式 — 命令替换内不加外层双引号(避免嵌套)
        result = HeredocInCommandSubstitution.Replace(result, static m =>
        {
            var content = m.Groups[2].Value;
            return EscapeForDoubleQuotedString(content);
        });

        // 再处理独立 HEREDOC 模式 — 加外层双引号
        result = StandaloneHeredoc.Replace(result, static m =>
        {
            var content = m.Groups[2].Value;
            return "\"" + EscapeForDoubleQuotedString(content) + "\"";
        });

        // 最后转义剩余的孤立 << 标记 — PowerShell/Cmd 解析为重定向操作符导致命令失败
        if (result.Contains("<<"))
        {
            result = result.Replace("<<", "`<`<");
            _logger?.LogWarning("检测到孤立的 << 标记,已转义为 PowerShell 安全形式");
        }

        if (result == command)
        {
            return new CommandDecision.Allow();
        }

        _logger?.LogInformation("HEREDOC 已改写为双引号字符串");
        return new CommandDecision.Rewrite(result, "HEREDOC 转换");
    }

    /// <summary>
    /// 判断当前 shell 是否为 Bash — Bash 原生支持 HEREDOC,无需转换
    /// </summary>
    private static bool IsBashShell(IReadOnlyDictionary<string, object> context)
    {
        return context.TryGetValue("ShellKind", out var kindObj)
            && kindObj is SystemActuatorKind kind
            && kind == SystemActuatorKind.Bash;
    }

    /// <summary>
    /// 转义双引号字符串中的特殊字符 — 双引号、反斜杠、$ 需要转义
    /// </summary>
    private static string EscapeForDoubleQuotedString(string content)
    {
        return content
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\n")
            .Trim();
    }
}
