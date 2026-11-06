namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// argv hash 校验 node — 独立公共对象，计算命令 argv 的 hash 防意图反推。
/// <para>
/// 逻辑迁移自 GlobalTwoPhaseConfirmGuard（ADR 0012 阶段7），增强为 hash 校验。
/// </para>
/// <para>
/// 防意图反推原理（计划 docs/plan/safety/mtp-perturbation-defense-plan.md 约束第9条）：
/// <list type="bullet">
/// <item>确认串含 argv hash（SHA256 前 6 位 hex）</item>
/// <item>AI 必须真的解析命令才能算出 hash，不能从意图反推</item>
/// <item>MTP 扰动前后两次解析结果不一致 → hash 不匹配 → 自动拦截</item>
/// </list>
/// </para>
/// </summary>
[Register(typeof(ArgvHashNode), ServiceLifetime.Singleton)]
public sealed class ArgvHashNode {
    private const int HashLength = 6;

    /// <summary>
    /// 计算命令的 argv hash（SHA256 前 6 位 hex）。
    /// <para>
    /// argv 由 ShellCommand.Parse 解析得出，用 \0 分隔拼接后哈希。
    /// </para>
    /// </summary>
    /// <param name="command">待计算命令</param>
    /// <returns>argv hash（6 位 hex 字符串）</returns>
    public string ComputeArgvHash(string command) {
        var parsed = ShellCommand.Parse(command);
        var argv = new[] { parsed.CommandName }.Concat(parsed.Arguments);
        var joined = string.Join('\0', argv);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hashBytes).AsSpan(0, HashLength).ToString();
    }

    /// <summary>
    /// 校验 AI 提供的 argv hash 是否匹配命令的解析结果。
    /// </summary>
    /// <param name="command">待校验命令</param>
    /// <param name="providedHash">AI 提供的 hash（null 表示未提供）</param>
    /// <returns>匹配返回 true，不匹配或未提供返回 false</returns>
    public bool ValidateArgvHash(string command, string? providedHash) {
        if (string.IsNullOrEmpty(providedHash))
            return false;
        var normalized = providedHash.TrimStart('#');
        var expected = ComputeArgvHash(command);
        return string.Equals(expected, normalized, StringComparison.OrdinalIgnoreCase);
    }
}