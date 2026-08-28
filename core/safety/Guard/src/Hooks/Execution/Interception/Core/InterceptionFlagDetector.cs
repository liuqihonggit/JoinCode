namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 命令拦截属性检测器 — 一次性检测命令的所有拦截属性,返回 [Flags] 组合
/// <para>
/// 守卫调度前调用,用位运算快速过滤无关守卫,避免每个守卫独立 CanHandle 检查。
/// 对齐 ADR 0039: [Flags] 位标志优化属性检测。
/// </para>
/// </summary>
public static class InterceptionFlagDetector
{
    /// <summary>
    /// 检测命令的拦截属性组合
    /// </summary>
    /// <param name="command">待检测的命令</param>
    /// <param name="context">执行上下文(如 proxy_url 等)</param>
    /// <returns>属性标志组合(位运算表达多个属性)</returns>
    public static InterceptionFlags Detect(string command, IReadOnlyDictionary<string, object> context)
    {
        var flags = InterceptionFlags.None;
        if (string.IsNullOrWhiteSpace(command))
            return flags;

        var span = command.AsSpan().TrimStart();

        if (IsGitCommit(span))
            flags |= InterceptionFlags.IsGitCommit;

        if (HasHeredocSyntax(span))
            flags |= InterceptionFlags.HasHeredoc;

        if (IsGhPrCreate(span))
            flags |= InterceptionFlags.IsGhPrCreate;

        if (IsGhWithTimeout(span))
            flags |= InterceptionFlags.IsGhWithTimeout;

        if (NeedVpnProxy(context))
            flags |= InterceptionFlags.NeedVpn;

        return flags;
    }

    private static bool IsGitCommit(ReadOnlySpan<char> span)
        => span.StartsWith("git commit") || span.StartsWith("git  commit");

    private static bool HasHeredocSyntax(ReadOnlySpan<char> span)
        => span.IndexOf("<<") >= 0 && span.IndexOf("EOF") >= 0;

    private static bool IsGhPrCreate(ReadOnlySpan<char> span)
        => span.StartsWith("gh pr create");

    private static bool IsGhWithTimeout(ReadOnlySpan<char> span)
        => span.StartsWith("gh ") && span.IndexOf("--timeout") < 0;

    private static bool NeedVpnProxy(IReadOnlyDictionary<string, object> context)
        => context.TryGetValue("proxy_url", out var proxy) && proxy is string s && !string.IsNullOrWhiteSpace(s);
}
