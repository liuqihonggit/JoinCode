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
    /// 是否危险（直接拒绝不提示）
    /// </summary>
    public bool IsDangerous => Level == CommandDangerLevel.Dangerous;

    /// <summary>
    /// 是否需要 ask 确认（Unknown 黄灯ask / LightValidation 绿灯ask / Execution 红灯ask）
    /// </summary>
    public bool RequiresAsk => Level == CommandDangerLevel.Unknown ||
                               Level == CommandDangerLevel.LightValidation ||
                               Level == CommandDangerLevel.Execution;

    /// <summary>
    /// 是否未知命令（黄灯ask）— 未在 catalog 中登记的命令
    /// </summary>
    public bool IsUnknown => Level == CommandDangerLevel.Unknown;

    /// <summary>
    /// 是否绿灯ask（可撤回操作）
    /// </summary>
    public bool IsLightValidation => Level == CommandDangerLevel.LightValidation;

    /// <summary>
    /// 是否红灯ask（不可撤回操作）
    /// </summary>
    public bool IsExecution => Level == CommandDangerLevel.Execution;

    /// <summary>
    /// 安全结果（无危险）
    /// </summary>
    public static DangerClassificationResult SafeResult { get; } = new(CommandDangerLevel.Safe, CommandRisk.None, null);
}
