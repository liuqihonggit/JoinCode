
namespace Core.Permission;

/// <summary>
/// 权限提升风险处理器 — CommandRisk.PrivilegeEscalation 的拦截策略
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class PrivilegeEscalationRiskHandler : SimpleCommandRiskHandler
{
    /// <inheritdoc />
    public override CommandRisk RiskType => CommandRisk.PrivilegeEscalation;

    /// <inheritdoc />
    protected override string OperationName => "权限提升操作";

    /// <inheritdoc />
    protected override string AutoModeHint => "sudo/runas 等权限提升命令在 Auto 模式下不被允许，如确需执行请切换到 Ask 模式确认";

    /// <inheritdoc />
    protected override string AskModeWarning => "此操作将以更高权限执行命令";
}
