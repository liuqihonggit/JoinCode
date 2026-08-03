
namespace Core.Permission;

/// <summary>
/// 系统修改风险处理器 — CommandRisk.SystemModification 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class SystemModificationRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.SystemModification;

    /// <inheritdoc />
    protected override string OperationName => "系统修改操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "此操作可能影响系统配置或稳定性，如确需执行请切换到 Ask 模式确认";

    /// <inheritdoc />
    protected override string AskModeWarning => "此操作可能影响系统配置或稳定性";
}
