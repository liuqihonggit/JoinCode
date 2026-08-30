namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 危险分类结果 — 统一的命令危险分级结果，作为权限决策的唯一依据
/// </summary>
/// <param name="Level">危险等级（决策依据）</param>
/// <param name="RiskType">风险类型（消息构建依据，保留细粒度描述）</param>
/// <param name="Details">检测详情</param>
public sealed record DangerClassificationResult(
    CommandDangerLevel Level,
    CommandRisk RiskType = CommandRisk.None,
    string? Details = null)
{
    /// <summary>
    /// 是否需要拦截（非 Safe 级别都需要拦截）
    /// </summary>
    public bool RequiresIntervention => Level != CommandDangerLevel.Safe;

    /// <summary>
    /// 是否绝对禁止（AI 不可执行，必须用户手动执行）
    /// </summary>
    public bool IsForbidden => Level == CommandDangerLevel.Forbidden;

    /// <summary>
    /// 安全结果（无危险）
    /// </summary>
    public static DangerClassificationResult SafeResult { get; } = new(CommandDangerLevel.Safe, CommandRisk.None, null);
}
