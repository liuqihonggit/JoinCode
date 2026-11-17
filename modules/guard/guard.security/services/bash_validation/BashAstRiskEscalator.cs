namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// bash AST 升级器 — 把「分类器判为 Safe、但 AST 看出是构造/混淆写法」的命令升到需要确认。
/// <para>
/// 定位：统一危险分类器（<c>ICommandDangerClassifier</c>）按词法判定，
/// 面对「看起来是良性命令、实际被构造过」的写法会漏；
/// <see cref="IBashSecurityValidator"/> 走 tree-sitter 全树遍历，能看出这些构造。
/// 两者是不同维度，不是新旧替代 —— 本类只负责把后者**衔接**进权限决策。
/// </para>
/// </summary>
public interface IBashAstRiskEscalator {
    /// <summary>
    /// 判定命令是否存在「无法静态信任」的构造。
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="result">升级后的判定；返回 <c>false</c> 时调用方保持原判</param>
    /// <returns>需要升级返回 <c>true</c></returns>
    bool TryEscalate(string command, out DangerClassificationResult? result);
}

/// <summary>
/// bash AST 升级器实现 — 只升级「构造/混淆」类检查，其余检查项的噪声已被实测排除。
/// <para>
/// <b>为什么只升级一部分</b>：这套校验器源自「这条命令能否免确认自动执行」的口径，
/// 对写脚本、写代码这类正常创作行为同样会报警。实测 25 条日常命令（本仓库开发场景），
/// 下列检查项在良性命令上就会触发，若一并升级，用户每次写 heredoc 脚本、写 <c>2&gt;&amp;1</c>、
/// 写 <c>if</c> 语句都要多一次确认弹窗 —— 防护会被当成噪音关掉：
/// </para>
/// <list type="bullet">
/// <item><c>Newlines</c> — <c>cat &gt; x.sh &lt;&lt;EOF … EOF</c>（写脚本）</item>
/// <item><c>MalformedTokenInjection</c> — <c>if [ -f x ]; then …</c>、<c>[ -d x ] &amp;&amp; …</c></item>
/// <item><c>OutputRedirection</c> — <c>cmd 2&gt;&amp;1 | tee log</c></item>
/// <item><c>DangerousVariables</c> — <c>source ~/.bashrc</c></item>
/// <item><c>BackslashEscapedWhitespace</c> — <c>echo a\ b</c></item>
/// <item><c>ObfuscatedFlags</c> — <c>IFS=$'\n'</c></item>
/// <item><c>BraceExpansion</c> — <c>echo {a,b}</c></item>
/// </list>
/// <para>
/// 实测证据见 <c>BashAstEscalationTests</c>（噪音项与升级项各有回归锁）。
/// 需要临时关闭时设环境变量 <c>JCC_BASH_AST_ESCALATE=0</c>。
/// </para>
/// </summary>
[Register(typeof(IBashAstRiskEscalator), ServiceLifetime.Singleton)]
public sealed partial class BashAstRiskEscalator : ServiceEntity, IBashAstRiskEscalator {
    /// <summary>
    /// 升级开关的环境变量名 — 设 <c>0</c> 关闭（排障用，不必改代码）
    /// </summary>
    public const string DisableEnvironmentVariable = "JCC_BASH_AST_ESCALATE";

    private readonly IBashSecurityValidator _validator;
    private readonly bool _enabled;

    /// <summary>构造 AST 升级器</summary>
    public BashAstRiskEscalator(IBashSecurityValidator validator) {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        _enabled = !string.Equals(
            Environment.GetEnvironmentVariable(DisableEnvironmentVariable), "0", StringComparison.Ordinal);
    }

    /// <summary>
    /// 需要升级的检查项 — 全是「被构造/混淆」的信号：正常写法不会命中，命中即说明
    /// 命令的真实内容与字面不同，静态判定不可信。
    /// </summary>
    internal static readonly FrozenSet<BashSecurityCheckId> EscalatingChecks = FrozenSet.Create(
        BashSecurityCheckId.JqSystemFunction,
        BashSecurityCheckId.IfsInjection,
        BashSecurityCheckId.ControlCharacters,
        BashSecurityCheckId.UnicodeWhitespace,
        BashSecurityCheckId.MidWordHash,
        BashSecurityCheckId.BackslashEscapedOperators,
        BashSecurityCheckId.ProcEnvironAccess,
        BashSecurityCheckId.CommentQuoteDesync);

    /// <inheritdoc />
    public bool TryEscalate(string command, out DangerClassificationResult? result) {
        result = null;
        if (!_enabled || string.IsNullOrWhiteSpace(command))
            return false;

        var validation = _validator.Validate(command);
        if (validation.IsSafe || validation.CheckId is not { } checkId || !EscalatingChecks.Contains(checkId))
            return false;

        result = new DangerClassificationResult(
            CommandDangerLevel.Unknown,
            CommandRisk.RemoteExecution,
            $"命令包含无法静态信任的构造（{checkId}）：{validation.Message ?? "AST 校验未通过"}。"
            + "这不是「命令写错了」，而是它实际执行的内容可能与字面不同 —— "
            + "请改用不含该构造的等价写法（例如去掉隐式变量展开、改用显式参数），或切到 Ask 模式确认后执行。");
        return true;
    }
}
