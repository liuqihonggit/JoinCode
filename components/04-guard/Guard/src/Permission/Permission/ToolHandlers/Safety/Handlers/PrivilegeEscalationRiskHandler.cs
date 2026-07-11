
namespace Core.Permission;

/// <summary>
/// 权限提升风险处理器 — CommandRisk.PrivilegeEscalation 的拦截策略
/// Auto 模式: 拒绝 + 提示权限提升风险
/// Ask 模式: 提示确认 + 警告权限提升影响
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class PrivilegeEscalationRiskHandler : ICommandRiskHandler
{
    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.PrivilegeEscalation;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        return $"权限提升操作已被阻止（{context.Details}）。sudo/runas 等权限提升命令在 Auto 模式下不被允许，如确需执行请切换到 Ask 模式确认";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        return $"工具 '{context.ToolName}' 请求执行权限提升操作（{context.Details}）。此操作将以更高权限执行命令，是否批准？";
    }
}
