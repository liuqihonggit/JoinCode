namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令拦截属性标志 — [Flags] 位标志枚举,表示命令的拦截属性组合
/// <para>
/// 用于优化守卫调度:一次检测多个属性,而非每个守卫独立 CanHandle 检查。
/// 属性组合通过位运算表达,新增属性只需加一个位,不影响现有。
/// </para>
/// <para>
/// 对齐 ADR 0039: 状态机 + 守卫 + [Flags] 位标志降低状态爆炸。
/// </para>
/// </summary>
[Flags]
public enum InterceptionFlags : ushort
{
    /// <summary>无匹配属性 — 命令不需要任何守卫干预</summary>
    None = 0,

    /// <summary>git commit 命令 — 引导到 /commit 斜杠命令</summary>
    IsGitCommit = 1,

    /// <summary>包含 HEREDOC 语法 — 需转换为双引号字符串</summary>
    HasHeredoc = 2,

    /// <summary>需要 VPN 代理 — git/curl 加代理参数</summary>
    NeedVpn = 4,

    /// <summary>gh pr create 命令 — 需补全 --body 参数</summary>
    IsGhPrCreate = 8,

    /// <summary>gh 命令带超时风险 — 需加超时参数</summary>
    IsGhWithTimeout = 16,
}
